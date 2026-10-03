using System.Runtime.CompilerServices;
using DDDToolkit.Access;
using FluentAssertions;

namespace DDDToolkit.Tests.Access;

/// <summary>
/// What an access check read is kept for the handler of the request it checked, and handed out once: the
/// handler acts on what the request's latest pass read, and a handler reached round the check has nothing to
/// act on.
/// </summary>
public class CheckedTests
{
    /// <summary>What a check read of an invoice.</summary>
    private sealed record SeenInvoice(int Invoice, long Version);

    private sealed record CloseInvoice(int Invoice);

    private readonly record struct ReopenInvoice(int Invoice);

    [Fact]
    public void What_a_check_kept_is_handed_to_the_handler_once()
    {
        var seen = new Checked<SeenInvoice>();
        var command = new CloseInvoice(7);

        seen.KeepFor(command, new SeenInvoice(7, 3));

        seen.TakeFor(command).Should().Be(new SeenInvoice(7, 3));
        FluentActions.Invoking(() => seen.TakeFor(command)).Should().Throw<InvalidOperationException>(
            "what was kept is handed out once, so the same request handed to a handler once more has nothing to take");
    }

    [Fact]
    public void A_request_that_passed_no_check_has_nothing_to_take()
    {
        var seen = new Checked<SeenInvoice>();

        FluentActions.Invoking(() => seen.TakeFor(new CloseInvoice(7))).Should().Throw<InvalidOperationException>()
            .WithMessage("No CheckedTests.SeenInvoice was kept for this CheckedTests.CloseInvoice.*must pass its access checks*whose check keeps a CheckedTests.SeenInvoice.");
    }

    [Fact]
    public void What_was_not_kept_is_named_as_its_author_writes_it()
    {
        // A generic hold, as a package keeps one, and a request closed over an id: named with their type
        // arguments and the class they are nested in, as the access checks name a requirement, and not as the
        // runtime spells them, SeenOf`1.
        var seen = new Checked<SeenOf<long>>();

        FluentActions.Invoking(() => seen.TakeFor(new Close<int>(7))).Should().Throw<InvalidOperationException>()
            .WithMessage("No CheckedTests.SeenOf<Int64> was kept for this CheckedTests.Close<Int32>.*whose check keeps a CheckedTests.SeenOf<Int64>.");
    }

    /// <summary>What a check read of something known by <typeparamref name="TId"/>.</summary>
    private sealed record SeenOf<TId>(TId Id);

    private sealed record Close<TId>(TId Id);

    [Fact]
    public void A_request_is_found_by_reference_and_not_by_value()
    {
        var seen = new Checked<SeenInvoice>();
        var first = new CloseInvoice(7);
        var second = new CloseInvoice(7);
        first.Should().Be(second, "two equal commands sent in one scope are still two requests");

        seen.KeepFor(first, new SeenInvoice(7, 3));

        FluentActions.Invoking(() => seen.TakeFor(second)).Should().Throw<InvalidOperationException>("each has its own check");
        seen.TakeFor(first).Version.Should().Be(3);
    }

    [Fact]
    public void A_request_that_passed_twice_and_was_handled_once_is_handed_its_latest_pass()
    {
        var seen = new Checked<SeenInvoice>();
        var command = new CloseInvoice(7);

        // The request passed its check, and something between the check and its handler failed: what the check
        // read was never taken. A retry around the pipeline sends the same object again, in the same scope, and
        // the invoice has changed in between.
        seen.KeepFor(command, new SeenInvoice(7, 3));
        seen.KeepFor(command, new SeenInvoice(7, 4));

        // The handler acts on what the pass it follows read, not on what was read before: handed version 3,
        // it would lose a race that never was.
        seen.TakeFor(command).Should().Be(new SeenInvoice(7, 4));

        // And nothing of the first pass stays behind for a handler called directly with that object.
        FluentActions.Invoking(() => seen.TakeFor(command)).Should().Throw<InvalidOperationException>();

        // Passed again after it was handled, it is handled again, with what that pass read.
        seen.KeepFor(command, new SeenInvoice(7, 5));
        seen.TakeFor(command).Version.Should().Be(5);
    }

    [Fact]
    public void What_is_kept_is_kept_per_kind_of_thing()
    {
        var invoices = new Checked<SeenInvoice>();
        var units = new Checked<int>();
        var command = new CloseInvoice(7);

        units.KeepFor(command, 42);

        FluentActions.Invoking(() => invoices.TakeFor(command)).Should().Throw<InvalidOperationException>();
        units.TakeFor(command).Should().Be(42);
    }

    [Fact]
    public void A_struct_request_is_refused_when_something_is_kept_for_it()
    {
        var seen = new Checked<SeenInvoice>();

        FluentActions.Invoking(() => seen.KeepFor(new ReopenInvoice(7), new SeenInvoice(7, 3))).Should().Throw<ArgumentException>()
            .WithMessage("CheckedTests.ReopenInvoice is a struct*", "its handler holds another copy, and would never find what was kept");
    }

    [Fact]
    public void Nulls_are_refused_where_they_are_handed_in()
    {
        var seen = new Checked<SeenInvoice>();

        FluentActions.Invoking(() => seen.TakeFor(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => seen.KeepFor(null!, new SeenInvoice(7, 3))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => seen.KeepFor(new CloseInvoice(7), null!)).Should().Throw<ArgumentNullException>("nothing kept is nothing checked");
    }

    [Fact]
    public async Task Requests_checked_side_by_side_each_get_their_own()
    {
        var seen = new Checked<SeenInvoice>();
        var commands = Enumerable.Range(0, 200).Select(invoice => new CloseInvoice(invoice)).ToList();
        var again = new CloseInvoice(-1);

        await Task.WhenAll(commands.Select(command => Task.Run(
            () =>
            {
                seen.KeepFor(command, new SeenInvoice(command.Invoice, command.Invoice * 2L));
                seen.KeepFor(again, new SeenInvoice(-1, command.Invoice));
            },
            TestContext.Current.CancellationToken)));

        var taken = await Task.WhenAll(commands.Select(command => Task.Run(() => seen.TakeFor(command), TestContext.Current.CancellationToken)));
        taken.Should().Equal(commands.Select(command => new SeenInvoice(command.Invoice, command.Invoice * 2L)));

        // One request object that passed many times side by side is one handling: what one of those passes
        // kept, whole, handed to exactly one of those that come for it.
        var takenAgain = await Task.WhenAll(commands.Select(_ => Task.Run(
            () =>
            {
                try
                {
                    return seen.TakeFor(again);
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            },
            TestContext.Current.CancellationToken)));

        var handed = takenAgain.Where(one => one is not null).Should().ContainSingle().Which!;
        handed.Invoice.Should().Be(-1);
        commands.Select(command => (long)command.Invoice).Should().Contain(handed.Version);
        FluentActions.Invoking(() => seen.TakeFor(again)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Nothing_holds_a_request_once_it_is_done_with()
    {
        var seen = new Checked<SeenInvoice>();
        var request = KeepForANewRequest(seen);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        request.TryGetTarget(out _).Should().BeFalse("what was kept and never taken does not keep its request alive");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<CloseInvoice> KeepForANewRequest(Checked<SeenInvoice> seen)
    {
        var command = new CloseInvoice(7);
        seen.KeepFor(command, new SeenInvoice(7, 3));
        return new WeakReference<CloseInvoice>(command);
    }
}
