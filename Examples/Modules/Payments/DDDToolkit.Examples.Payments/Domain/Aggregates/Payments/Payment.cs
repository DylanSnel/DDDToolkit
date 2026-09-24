using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.SharedKernel;

namespace DDDToolkit.Examples.Payments.Domain.Payments;

/// <summary>The money for one order: waiting, taken, refused, or no longer wanted.</summary>
/// <remarks>
/// It is opened when the order is placed and settled only once Inventory has set the stock aside, so a
/// customer is never charged for an order that could not be filled. <see cref="Settle"/> takes the
/// provider's answer already translated into <see cref="PaymentOutcome"/>: the aggregate never sees the
/// provider's own vocabulary. See <c>Infrastructure/PaymentProvider/FakePaymentProvider.cs</c>.
/// </remarks>
[AggregateRoot<Guid>("PAY")]
public partial class Payment
{
    public Payment(PaymentId id, OrderId order, Money amount) : base(id)
    {
        ArgumentNullException.ThrowIfNull(amount);

        Order = order;
        Amount = amount;
        Status = PaymentStatus.Pending;
    }

    public OrderId Order { get; private set; }

    public Money Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>Why the provider refused, when it did.</summary>
    public string? Refusal { get; private set; }

    /// <summary>
    /// Records what the provider said. A payment that is no longer pending ignores a second answer, so
    /// a retried message settles nothing twice.
    /// </summary>
    public void Settle(PaymentOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (Status is not PaymentStatus.Pending)
        {
            return;
        }

        if (outcome.Taken)
        {
            Status = PaymentStatus.Captured;
            RaiseDomainEvent(new PaymentCaptured(Order, Amount));
        }
        else
        {
            Status = PaymentStatus.Declined;
            Refusal = outcome.Refusal;
            RaiseDomainEvent(new PaymentDeclined(Order, outcome.Refusal ?? "Declined."));
        }
    }

    /// <summary>The order was cancelled before the money was taken. Nothing to refund, nothing to publish.</summary>
    public void Void()
    {
        if (Status is PaymentStatus.Pending)
        {
            Status = PaymentStatus.Voided;
        }
    }
}

public enum PaymentStatus
{
    Pending,
    Captured,
    Declined,
    Voided,
}
