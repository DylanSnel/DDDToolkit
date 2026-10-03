using DDDToolkit.EntityFramework.Integration;
using Examples.Webshop.Payments.Contracts;

namespace Examples.Webshop.Payments.Application.Payments;

/// <summary>The money was taken: tell Ordering, which confirms the order.</summary>
/// <remarks>
/// Captured is Payments' word; succeeded is what the others were promised. The names differ on purpose:
/// the contract is written for its readers, the domain event for this module.
/// </remarks>
public sealed class PublishPaymentCaptured : IOutboundIntegrationEvent<PaymentCaptured, PaymentSucceededV1>
{
    public ValueTask<PaymentSucceededV1?> CreateAsync(PaymentCaptured captured, CancellationToken cancellationToken)
        => new(new PaymentSucceededV1(captured.OrderId, captured.Amount.Amount, captured.Amount.Currency));
}
