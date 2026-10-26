using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using FluentAssertions;

namespace DDDToolkit.Tests.Access;

/// <summary>
/// The request in hand carries the check it passed, so that what goes wrong later in its handling can ask that
/// check again: a save the database refused, to tell a caller whose rights changed in between from a rule C# and
/// the policies hold differently. Asking again keeps nothing behind. Where the request is in hand, and where it is
/// not, is <see cref="RequestInHandTests"/>'s.
/// </summary>
public class RequestInHandAskedAgainTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>What every request of the test's module implements.</summary>
    public interface IShelfRequest : IRequireAccess;

    /// <summary>The caller holds a key on a shelf.</summary>
    public sealed record OnShelf(string Key, int Shelf) : AccessRequirement;

    /// <summary>Restocks a shelf; what it requires is said when it is made.</summary>
    public sealed record RestockShelf(int Shelf, AccessRequirement Requires) : IShelfRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => Requires;
    }

    /// <summary>The keys the caller holds, as the database would answer: changed by a test between two askings.</summary>
    public sealed class HeldKeys
    {
        public HashSet<(string Key, int Shelf)> Held { get; } = [];

        public int Asked { get; set; }

        /// <summary>Whether the check takes a turn of the scheduler before it answers, as one that reads does.</summary>
        public bool Reads { get; init; }

        /// <summary>What the check throws when it is asked, in place of an answer.</summary>
        public Exception? Fails { get; set; }
    }

    /// <summary>Lets a caller through for the keys it holds, and keeps the shelf for the handler.</summary>
    public sealed class ShelfCheck(HeldKeys keys, Checked<int> checkedShelf) : IAccessCheck
    {
        public bool Decides(AccessRequirement requirement) => requirement is OnShelf;

        public async ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
        {
            lock (keys)
            {
                keys.Asked++;
            }

            if (keys.Reads)
            {
                await Task.Yield();
            }

            if (keys.Fails is { } failure)
            {
                throw failure;
            }

            var required = (OnShelf)requirement;
            if (!keys.Held.Contains((required.Key, required.Shelf)))
            {
                throw new RefusalException("shelves.not-permitted", RefusalKind.NotPermitted, "That takes a key on the shelf.");
            }

            checkedShelf.KeepFor(request, required.Shelf);
        }
    }

    /// <summary>What sends a request: the checks, then the handler, in one flow, as a pipeline behavior does.</summary>
    private static async Task<T> SendAsync<T>(AccessChecks<IShelfRequest> checks, IShelfRequest request, Func<Task<T>> handler)
    {
        await checks.RequireAsync(request, Cancellation);
        return await handler();
    }

    private static (AccessChecks<IShelfRequest> Checks, HeldKeys Keys, Checked<int> Kept) Module(bool reads = false)
    {
        var keys = new HeldKeys { Reads = reads };
        var kept = new Checked<int>();
        return (new AccessChecks<IShelfRequest>([new ShelfCheck(keys, kept)]), keys, kept);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_request_in_hand_carries_the_requirement_it_passed_with(bool reads)
    {
        var (checks, keys, _) = Module(reads);
        keys.Held.Add(("shelves.restock", 4));
        var request = new RestockShelf(4, new OnShelf("shelves.restock", 4));

        var seen = await SendAsync(checks, request, () => Task.FromResult(RequestInHand.Current));

        seen.Should().NotBeNull();
        seen!.Request.Should().BeSameAs(request);
        seen.Requirement.Should().Be(new OnShelf("shelves.restock", 4));
        RequestInHand.Current.Should().BeNull("it follows the flow into the handler, not back out to what sent the request");
    }

    [Fact]
    public async Task Asked_again_it_answers_whether_the_caller_still_passes_now()
    {
        var (checks, keys, _) = Module(reads: true);
        keys.Held.Add(("shelves.restock", 4));

        var answers = await SendAsync(checks, new RestockShelf(4, new OnShelf("shelves.restock", 4)), async () =>
        {
            var inHand = RequestInHand.Current!;
            var still = await inHand.StillPassesAsync(Cancellation);

            // The key is taken away between the check and what the handler does next.
            keys.Held.Clear();
            var after = await inHand.StillPassesAsync(Cancellation);
            return (still, after);
        });

        answers.Should().Be((true, false));
        keys.Asked.Should().Be(3, "once to pass, and once for each asking again");
    }

    [Fact]
    public async Task Asked_again_the_check_keeps_nothing_for_the_handler()
    {
        var (checks, keys, kept) = Module();
        keys.Held.Add(("shelves.restock", 4));
        var request = new RestockShelf(4, new OnShelf("shelves.restock", 4));

        await SendAsync(checks, request, async () =>
        {
            kept.TakeFor(request).Should().Be(4, "the handler takes what the check kept");
            (await RequestInHand.Current!.StillPassesAsync(Cancellation)).Should().BeTrue();
            return 0;
        });

        FluentActions.Invoking(() => kept.TakeFor(request)).Should().Throw<InvalidOperationException>(
            "asking again left nothing behind that a handler called later with the same request could take");
    }

    [Fact]
    public async Task Only_a_refusal_when_asked_again_is_a_no_and_a_conflict_or_any_other_failure_is_no_answer()
    {
        var (checks, keys, _) = Module();
        keys.Held.Add(("shelves.restock", 4));

        await SendAsync(checks, new RestockShelf(4, new OnShelf("shelves.restock", 4)), async () =>
        {
            var inHand = RequestInHand.Current!;

            // A request that named a version the shelf is no longer at, the handler's own save among the ways it
            // moves on: a check that asks the version after the key has found the key still held.
            keys.Fails = new ConcurrencyConflictException(typeof(int), 4);
            await FluentActions.Awaiting(() => inHand.StillPassesAsync(Cancellation).AsTask())
                .Should().ThrowAsync<ConcurrencyConflictException>("a version that moved on says nothing about the caller's rights");

            keys.Fails = new InvalidOperationException("The connection was lost.");
            await FluentActions.Awaiting(() => inHand.StillPassesAsync(Cancellation).AsTask())
                .Should().ThrowAsync<InvalidOperationException>("a check that could not read answers neither way");

            keys.Fails = null;
            keys.Held.Clear();
            (await inHand.StillPassesAsync(Cancellation)).Should().BeFalse("a refusal is the one no");
            return 0;
        });
    }

    [Fact]
    public async Task A_request_anyone_may_send_passes_again_without_asking_anybody()
    {
        var (checks, keys, _) = Module();
        keys.Held.Add(("shelves.restock", 4));
        var open = new RestockShelf(4, AccessRequirement.AllowAnonymous());

        // Sent from the handler of a request that passed a check: the open one is in hand for its own handling, and
        // it passed by asking nobody, so asking again asks nobody either.
        var seen = await SendAsync(checks, new RestockShelf(4, new OnShelf("shelves.restock", 4)), () =>
            SendAsync(checks, open, async () =>
            {
                var inHand = RequestInHand.Current!;
                return (inHand.Request, inHand.Requirement, Again: await inHand.StillPassesAsync(Cancellation));
            }));

        seen.Request.Should().BeSameAs(open);
        seen.Requirement.Should().BeOfType<AccessRequirement.Anyone>();
        seen.Again.Should().BeTrue();
        keys.Asked.Should().Be(1, "only the outer request's check was asked, once");
    }

    [Fact]
    public async Task Requests_handled_side_by_side_each_find_their_own()
    {
        var (checks, keys, _) = Module(reads: true);
        keys.Held.Add(("shelves.restock", 4));
        keys.Held.Add(("shelves.restock", 5));
        var four = new RestockShelf(4, new OnShelf("shelves.restock", 4));
        var five = new RestockShelf(5, new OnShelf("shelves.restock", 5));
        using var both = new Barrier(2);

        async Task<AccessRequirement?> Handle()
        {
            // Both have passed before either looks.
            await Task.Run(() => both.SignalAndWait(TimeSpan.FromSeconds(10)), Cancellation);
            return RequestInHand.Current?.Requirement;
        }

        var seen = await Task.WhenAll(
            Task.Run(() => SendAsync(checks, four, Handle), Cancellation),
            Task.Run(() => SendAsync(checks, five, Handle), Cancellation));

        seen[0].Should().Be(new OnShelf("shelves.restock", 4));
        seen[1].Should().Be(new OnShelf("shelves.restock", 5));
    }

    [Fact]
    public async Task It_names_the_request_and_its_requirement_by_their_types_and_nothing_of_their_values()
    {
        var (checks, keys, _) = Module();
        keys.Held.Add(("shelves.restock", 4));

        var named = await SendAsync(checks, new RestockShelf(4, new OnShelf("shelves.restock", 4)), () => Task.FromResult(RequestInHand.Current!.ToString()));

        named.Should().Be("RequestInHandAskedAgainTests.RestockShelf (RequestInHandAskedAgainTests.OnShelf)");
    }
}
