using DDDToolkit.EntityFramework.Integration;
using Examples.Webshop.Payments.Contracts;

namespace Examples.Webshop.Payments.Application.Payments;

/// <summary>The provider refused: tell Ordering, which cancels the order.</summary>
public sealed class PublishPaymentDeclined : IOutboundIntegrationEvent<PaymentDeclined, PaymentFailedV1>
{
    public ValueTask<PaymentFailedV1?> CreateAsync(PaymentDeclined declined, CancellationToken cancellationToken)
        => new(new PaymentFailedV1(declined.OrderId, declined.Reason));
}
