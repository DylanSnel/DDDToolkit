using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using FluentAssertions;

namespace DDDToolkit.Tests.Access;

/// <summary>
/// The request in hand: the request whose access checks a flow of work asked last and that they let through. It
/// is what the handler and the save it ends with serve, found without being handed the request, and what a check
/// kept for it is found there, left where it is.
/// </summary>
public class RequestInHandTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public interface IBillingRequest : IRequireAccess;

    /// <summary>The one case of the tests: a key on an invoice, held by whoever <see cref="BillingCheck"/> says.</summary>
    public sealed record OnInvoice(int Invoice) : AccessRequirement;

    public sealed record CloseInvoice(int Invoice) : IBillingRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => new OnInvoice(Invoice);
    }

    public sealed record PriceList : IBillingRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.AllowAnonymous();
    }

    /// <summary>What the check read of an invoice: here, the version it saw, which is the invoice's number times ten.</summary>
    public sealed record SeenInvoice(int Invoice, long Version);

    /// <summary>
    /// Lets every invoice through but 13, after a turn of the scheduler, as a check that reads does, and keeps what it
    /// read. It notes what was in hand while it ran.
    /// </summary>
    public sealed class BillingCheck(Checked<SeenInvoice> kept) : IAccessCheck
    {
        public List<object?> InHandWhileChecking { get; } = [];

        public bool Decides(AccessRequirement requirement) => requirement is OnInvoice;

        public async ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            lock (InHandWhileChecking)
            {
                InHandWhileChecking.Add(RequestInHand.Current?.Request);
            }

            var invoice = ((OnInvoice)requirement).Invoice;
            if (invoice == 13)
            {
                throw new RefusalException("billing.not-permitted", RefusalKind.NotPermitted, "Not that one.");
            }

            kept.KeepFor(request, new SeenInvoice(invoice, invoice * 10));
        }
    }

    private readonly Checked<SeenInvoice> _kept = new();

    private readonly BillingCheck _check;

    private readonly AccessChecks<IBillingRequest> _checks;

    public RequestInHandTests()
    {
        _check = new BillingCheck(_kept);
        _checks = new AccessChecks<IBillingRequest>([_check]);
    }

    /// <summary>What a handler sees of the request in hand: the request, and what its check kept.</summary>
    private sealed record Seen(object? Request, SeenInvoice? Kept);

    /// <summary>A handler: it is handed the request, and looks at what is in hand after a turn of the scheduler, as its save would.</summary>
    private static async Task<Seen> HandleAsync()
    {
        await Task.Yield();
        return new Seen(RequestInHand.Current?.Request, Checked<SeenInvoice>.TryFindInHand(out var kept) ? kept : null);
    }

    /// <summary>A dispatcher: the checks, then the handler, in one method.</summary>
    private async Task<Seen> SendAsync(IBillingRequest request)
    {
        await _checks.RequireAsync(request, Cancellation);
        return await HandleAsync();
    }

    [Fact]
    public async Task The_request_its_checks_let_through_is_in_hand_for_the_handler_with_what_they_kept()
    {
        var command = new CloseInvoice(7);

        var seen = await SendAsync(command);

        seen.Request.Should().BeSameAs(command, "found by reference: the very request that passed");
        seen.Kept.Should().Be(new SeenInvoice(7, 70));
    }

    [Fact]
    public async Task Nothing_is_in_hand_outside_the_handling_of_a_request()
    {
        RequestInHand.Current.Should().BeNull();
        Checked<SeenInvoice>.TryFindInHand(out _).Should().BeFalse();

        // Kept for a request outside any checks, as a test of a check does: in nobody's hand.
        _kept.KeepFor(new CloseInvoice(7), new SeenInvoice(7, 70));
        (await HandleAsync()).Should().Be(new Seen(null, null));
    }

    [Fact]
    public async Task A_request_is_in_hand_in_the_method_that_asked_its_checks_and_not_after_it_returned()
    {
        var command = new CloseInvoice(7);

        (await SendAsync(command)).Request.Should().BeSameAs(command);

        // The dispatcher returned: what it put in hand stays in its own flow, so the next handler, called directly,
        // serves no request.
        (await HandleAsync()).Should().Be(new Seen(null, null));
    }

    [Fact]
    public async Task A_helper_that_only_asks_the_checks_gives_the_handler_after_it_nothing_in_hand()
    {
        var command = new CloseInvoice(7);

        // Asked in a method of its own, the request is in hand there alone: what runs after that method returned is
        // not part of its handling.
        await AskOnlyAsync(command);

        (await HandleAsync()).Should().Be(new Seen(null, null));

        async Task AskOnlyAsync(IBillingRequest request) => await _checks.RequireAsync(request, Cancellation);
    }

    [Fact]
    public async Task A_request_its_checks_refused_is_in_nobody_s_hand()
    {
        var passed = new CloseInvoice(7);
        var refused = new CloseInvoice(13);

        // Asked in this method, as a dispatcher asks: a request the checks let through is in hand here.
        await _checks.RequireAsync(passed, Cancellation);
        (RequestInHand.Current?.Request).Should().BeSameAs(passed);

        // Asked the same way, a request they refuse is not, and the one before it is not in hand any more either.
        RefusalException? refusal = null;
        try
        {
            await _checks.RequireAsync(refused, Cancellation);
        }
        catch (RefusalException thrown)
        {
            refusal = thrown;
        }

        refusal.Should().NotBeNull("the check refuses invoice 13");

        // Whatever runs next in the flow that was refused, a handler called anyway included, serves no request.
        (await HandleAsync()).Should().Be(new Seen(null, null));
    }

    [Fact]
    public async Task A_dispatcher_that_is_not_an_async_method_leaves_its_request_in_hand_for_its_caller()
    {
        var command = new CloseInvoice(7);

        // The limit a dispatcher is written to: only an async method gives its caller the flow back as it was. This
        // one is not: it asks the checks and hands the handling on, so what the checks put in hand is its caller's
        // from then on, and the next handler that caller calls directly is taken for part of the request's handling.
        await SendWithoutAsync(command);

        (await HandleAsync()).Request.Should().BeSameAs(command);

        Task<Seen> SendWithoutAsync(IBillingRequest request) => HandleAfterAsync(_checks.RequireAsync(request, Cancellation));

        static async Task<Seen> HandleAfterAsync(ValueTask checking)
        {
            await checking;
            return await HandleAsync();
        }
    }

    [Fact]
    public async Task A_request_counts_as_in_hand_only_once_its_check_let_it_through()
    {
        var command = new CloseInvoice(7);

        await _checks.RequireAsync(command, Cancellation);

        _check.InHandWhileChecking.Should().Equal([null], "while the check runs it has not let the request through yet");
        (RequestInHand.Current?.Request).Should().BeSameAs(command);
    }

    [Fact]
    public async Task A_request_anyone_may_send_is_in_hand_too()
    {
        var query = new PriceList();

        var seen = await SendAsync(query);

        seen.Request.Should().BeSameAs(query);
        seen.Kept.Should().BeNull("no check ran, so none kept anything");
    }

    [Fact]
    public async Task What_the_handler_takes_is_still_in_hand_for_its_save()
    {
        var command = new CloseInvoice(7);

        await _checks.RequireAsync(command, Cancellation);
        _kept.TakeFor(command).Should().Be(new SeenInvoice(7, 70));

        // Taken from the table the handler takes from; still what the request passed with, for the rest of its handling.
        Checked<SeenInvoice>.TryFindInHand(out var kept).Should().BeTrue();
        kept.Should().Be(new SeenInvoice(7, 70));
    }

    [Fact]
    public async Task Requests_sent_side_by_side_each_have_their_own_in_hand()
    {
        var first = new CloseInvoice(7);
        var second = new CloseInvoice(8);

        var both = await Task.WhenAll(SendAsync(first), SendAsync(second));

        both[0].Should().Be(new Seen(first, new SeenInvoice(7, 70)));
        both[1].Should().Be(new Seen(second, new SeenInvoice(8, 80)));
    }

    [Fact]
    public async Task A_request_sent_inside_the_handling_of_another_is_in_hand_for_its_own_and_the_other_after()
    {
        var outer = new CloseInvoice(7);
        var inner = new CloseInvoice(8);

        await _checks.RequireAsync(outer, Cancellation);
        var insideInner = await SendAsync(inner);
        var afterInner = await HandleAsync();

        insideInner.Should().Be(new Seen(inner, new SeenInvoice(8, 80)));
        afterInner.Should().Be(new Seen(outer, new SeenInvoice(7, 70)), "the inner request's sender returned, and the outer one is in hand again");
    }

    [Fact]
    public async Task A_request_sent_again_is_in_hand_with_what_its_latest_pass_kept()
    {
        var command = new CloseInvoice(7);

        await _checks.RequireAsync(command, Cancellation);
        await _checks.RequireAsync(new CloseInvoice(9), Cancellation);
        (await HandleAsync()).Request.Should().BeOfType<CloseInvoice>().Which.Invoice.Should().Be(9, "the flow asked about another request since");

        await _checks.RequireAsync(command, Cancellation);
        (await HandleAsync()).Should().Be(new Seen(command, new SeenInvoice(7, 70)));
    }

    [Fact]
    public async Task What_a_check_keeps_for_a_request_that_is_not_in_hand_is_not_found_in_hand()
    {
        var command = new CloseInvoice(7);
        await _checks.RequireAsync(command, Cancellation);

        // A check of another request, asked outside the checks, keeps for that request: the one in hand is untouched.
        _kept.KeepFor(new CloseInvoice(8), new SeenInvoice(8, 80));

        (await HandleAsync()).Should().Be(new Seen(command, new SeenInvoice(7, 70)));
    }
}
