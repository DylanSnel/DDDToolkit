using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Payments;
using DDDToolkit.Examples.SharedKernel;
using DDDToolkit.Testing;
using FluentAssertions;

namespace DDDToolkit.Examples.Tests;

/// <summary>The payment aggregate, and the anti-corruption layer in front of the provider.</summary>
public class PaymentTests
{
    private static Payment Open(decimal amount = 25m)
        => new(PaymentId.CreateSequential(), OrderId.CreateSequential(), new Money(amount, Money.Euro));

    [Fact]
    public async Task The_provider_takes_an_ordinary_amount()
    {
        var outcome = await new FakePaymentProvider().ChargeAsync("key", new Money(25m, Money.Euro), CancellationToken.None);

        outcome.Should().Be(PaymentOutcome.Captured);
    }

    [Fact]
    public async Task The_providers_refusal_arrives_in_the_domains_words()
    {
        var outcome = await new FakePaymentProvider().ChargeAsync("key", new Money(1450m, Money.Euro), CancellationToken.None);

        // Not "REFUSED" and not "LIMIT_EXCEEDED": those are the provider's words, and they stop at the
        // adapter.
        outcome.Taken.Should().BeFalse();
        outcome.Refusal.Should().Be("The amount is over the card's limit.");
    }

    [Fact]
    public async Task Asking_again_with_the_same_key_is_answered_the_same_without_charging_again()
    {
        var provider = new FakePaymentProvider();

        var first = await provider.ChargeAsync("PAY_1", new Money(25m, Money.Euro), CancellationToken.None);
        var retry = await provider.ChargeAsync("PAY_1", new Money(9999m, Money.Euro), CancellationToken.None);

        retry.Should().Be(first, "the key identifies the charge, so the retry is the same question");
    }

    [Fact]
    public void A_captured_payment_is_published_and_a_second_answer_changes_nothing()
    {
        var scenario = AggregateScenario.Given(Open());

        scenario.When(payment => payment.Settle(PaymentOutcome.Captured)).RaisedExactly<PaymentCaptured>();
        scenario.When(payment => payment.Settle(PaymentOutcome.Declined("late"))).RaisedNothing();

        scenario.Subject.Status.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public void A_declined_payment_says_why()
    {
        var scenario = AggregateScenario.Given(Open());

        scenario.When(payment => payment.Settle(PaymentOutcome.Declined("Over the limit.")))
            .RaisedExactly<PaymentDeclined>();

        scenario.Subject.Refusal.Should().Be("Over the limit.");
    }

    [Fact]
    public void Only_a_pending_payment_can_be_voided()
    {
        var captured = Open();
        captured.Settle(PaymentOutcome.Captured);

        captured.Void();
        var pending = Open();
        pending.Void();

        captured.Status.Should().Be(PaymentStatus.Captured, "money that was taken needs a refund, not a void");
        pending.Status.Should().Be(PaymentStatus.Voided);
    }
}
