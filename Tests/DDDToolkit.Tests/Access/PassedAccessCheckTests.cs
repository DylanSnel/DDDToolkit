using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using FluentAssertions;

namespace DDDToolkit.Tests.Access;

/// <summary>
/// The check a request passed is kept with the flow of work that handles it, so that what goes wrong later in
/// that flow can ask it again: a save the database refused, to tell a caller whose rights changed in between from
/// a rule C# and the policies hold differently. It is the handler's flow only, never another request's, and asking
/// again keeps nothing behind.
/// </summary>
public class PassedAccessCheckTests
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

        /// <summary>The check this flow had passed, as the check itself saw it while it was being asked.</summary>
        public List<PassedAccessCheck?> SeenWhileAsked { get; } = [];
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
                keys.SeenWhileAsked.Add(PassedAccessCheck.Current);
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
    public async Task The_handler_finds_the_check_its_request_passed_and_what_sent_it_does_not(bool reads)
    {
        var (checks, keys, _) = Module(reads);
        keys.Held.Add(("shelves.restock", 4));
        var request = new RestockShelf(4, new OnShelf("shelves.restock", 4));

        var seen = await SendAsync(checks, request, () => Task.FromResult(PassedAccessCheck.Current));

        seen.Should().NotBeNull();
        seen!.Request.Should().BeSameAs(request);
        seen.Requirement.Should().Be(new OnShelf("shelves.restock", 4));
        keys.SeenWhileAsked.Should().Equal([null], "while it is being asked the check has not passed yet");
        PassedAccessCheck.Current.Should().BeNull("it follows the flow into the handler, not back out to what sent the request");
    }

    [Fact]
    public async Task Asked_again_it_answers_whether_the_caller_still_passes_now()
    {
        var (checks, keys, _) = Module(reads: true);
        keys.Held.Add(("shelves.restock", 4));

        var answers = await SendAsync(checks, new RestockShelf(4, new OnShelf("shelves.restock", 4)), async () =>
        {
            var passed = PassedAccessCheck.Current!;
            var still = await passed.StillPassesAsync(Cancellation);

            // The key is taken away between the check and what the handler does next.
            keys.Held.Clear();
            var after = await passed.StillPassesAsync(Cancellation);
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
            (await PassedAccessCheck.Current!.StillPassesAsync(Cancellation)).Should().BeTrue();
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
            var passed = PassedAccessCheck.Current!;

            // A request that named a version the shelf is no longer at, the handler's own save among the ways it
            // moves on: a check that asks the version after the key has found the key still held.
            keys.Fails = new ConcurrencyConflictException(typeof(int), 4);
            await FluentActions.Awaiting(() => passed.StillPassesAsync(Cancellation).AsTask())
                .Should().ThrowAsync<ConcurrencyConflictException>("a version that moved on says nothing about the caller's rights");

            keys.Fails = new InvalidOperationException("The connection was lost.");
            await FluentActions.Awaiting(() => passed.StillPassesAsync(Cancellation).AsTask())
                .Should().ThrowAsync<InvalidOperationException>("a check that could not read answers neither way");

            keys.Fails = null;
            keys.Held.Clear();
            (await passed.StillPassesAsync(Cancellation)).Should().BeFalse("a refusal is the one no");
            return 0;
        });
    }

    [Fact]
    public async Task A_request_that_requires_nothing_or_was_refused_has_no_check_to_ask_again()
    {
        var (checks, keys, _) = Module();
        keys.Held.Add(("shelves.restock", 4));

        // Sent from the handler of a request that passed: the open one is not that request.
        var seen = await SendAsync(checks, new RestockShelf(4, new OnShelf("shelves.restock", 4)), () =>
            SendAsync(checks, new RestockShelf(4, new AccessRequirement.Open("Anybody may look at a shelf.")), () => Task.FromResult(PassedAccessCheck.Current)));
        seen.Should().BeNull("a request that requires nothing passed no check");

        // Asked in the handler's own flow, where a save of the handler's would look: the refused check takes the place
        // of the one the handler's request passed, and is no check that passed.
        var refused = new RestockShelf(5, new OnShelf("shelves.restock", 5));
        var seenInHandler = await SendAsync(checks, new RestockShelf(4, new OnShelf("shelves.restock", 4)), async () =>
        {
            var passedBefore = PassedAccessCheck.Current;
            try
            {
                await checks.RequireAsync(refused, Cancellation);
            }
            catch (RefusalException)
            {
            }

            return (Before: passedBefore, After: PassedAccessCheck.Current);
        });

        seenInHandler.Before.Should().NotBeNull("the handler's request passed its check");
        seenInHandler.After.Should().BeNull("a check that refused was not passed, though this flow asked it");
    }

    [Fact]
    public async Task A_request_sent_from_a_handler_has_its_own_and_the_first_has_its_own_back_after_it()
    {
        var (checks, keys, _) = Module(reads: true);
        keys.Held.Add(("shelves.restock", 4));
        keys.Held.Add(("shelves.count", 4));
        var outer = new RestockShelf(4, new OnShelf("shelves.restock", 4));
        var inner = new RestockShelf(4, new OnShelf("shelves.count", 4));

        var seen = await SendAsync(checks, outer, async () =>
        {
            var during = await SendAsync(checks, inner, () => Task.FromResult(PassedAccessCheck.Current?.Request));
            return (during, after: PassedAccessCheck.Current?.Request);
        });

        seen.during.Should().BeSameAs(inner);
        seen.after.Should().BeSameAs(outer);
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

        async Task<IRequireAccess?> Handle()
        {
            // Both have passed before either looks.
            await Task.Run(() => both.SignalAndWait(TimeSpan.FromSeconds(10)), Cancellation);
            return PassedAccessCheck.Current?.Request;
        }

        var seen = await Task.WhenAll(
            Task.Run(() => SendAsync(checks, four, Handle), Cancellation),
            Task.Run(() => SendAsync(checks, five, Handle), Cancellation));

        seen[0].Should().BeSameAs(four);
        seen[1].Should().BeSameAs(five);
    }

    [Fact]
    public async Task It_names_the_request_and_its_requirement_by_their_types_and_nothing_of_their_values()
    {
        var (checks, keys, _) = Module();
        keys.Held.Add(("shelves.restock", 4));

        var named = await SendAsync(checks, new RestockShelf(4, new OnShelf("shelves.restock", 4)), () => Task.FromResult(PassedAccessCheck.Current!.ToString()));

        named.Should().Be("PassedAccessCheckTests.RestockShelf (PassedAccessCheckTests.OnShelf)");
    }
}
