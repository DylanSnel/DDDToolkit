using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.Examples.Inventory.Contracts;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Examples.Payments.Application.IntegrationEvents;

/// <summary>An order was placed: open a payment for its total, but take nothing yet.</summary>
[IntegrationEventConsumer("payments.opener")]
public sealed class OpenPayment(PaymentsContext context) : IIntegrationEventHandler<OrderPlacedV1>
{
    public Task HandleAsync(OrderPlacedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        context.Payments.Add(new Payment(PaymentId.CreateSequential(), contract.OrderId, new Money(contract.Total, contract.Currency)));
        return Task.CompletedTask;
    }
}

/// <summary>The stock is set aside: now take the money.</summary>
/// <remarks>
/// Two things can arrive in the wrong order here. With a broker between the modules, Inventory's answer
/// can reach this handler before Ordering's <c>OrderPlacedV1</c> has, and then there is no payment yet.
/// Throwing is the answer: the message is retried, and by then the payment exists.
/// <para>
/// The charge is an outside call inside a handler that may be retried after it succeeded, so the
/// payment's id goes along as the idempotency key. A retry asks the same question and hears the same
/// answer, and nobody is charged twice.
/// </para>
/// </remarks>
[IntegrationEventConsumer("payments.taker")]
public sealed class TakePayment(PaymentsContext context, IPaymentProvider provider) : IIntegrationEventHandler<StockReservedV1>
{
    public async Task HandleAsync(StockReservedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        var payment = await context.Payments.SingleOrDefaultAsync(p => p.Order == contract.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"No payment for order {contract.OrderId} yet.");

        if (payment.Status is not PaymentStatus.Pending)
        {
            return;
        }

        payment.Settle(await provider.ChargeAsync(payment.Id.ToString(), payment.Amount, cancellationToken));
    }
}

/// <summary>An order was cancelled before its money was taken: stop waiting for it.</summary>
/// <remarks>
/// A payment that was already taken or refused is left alone; <see cref="Payment.Void"/> only touches a
/// pending one. A payment that does not exist yet means the cancellation overtook the placement, and
/// the message is retried until there is one to void, like <c>ReleaseStock</c> does in Inventory.
/// </remarks>
[IntegrationEventConsumer("payments.voider")]
public sealed class VoidPayment(PaymentsContext context) : IIntegrationEventHandler<OrderCancelledV1>
{
    public async Task HandleAsync(OrderCancelledV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        var payment = await context.Payments.SingleOrDefaultAsync(p => p.Order == contract.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"No payment for order {contract.OrderId} yet.");

        payment.Void();
    }
}
