using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Examples.Ordering.Contracts;

[assembly: Module("Payments")]

namespace DDDToolkit.Examples.Payments.Contracts;

/// <summary>The money for the order was taken.</summary>
[IntegrationEvent]
public sealed record PaymentSucceededV1(OrderId OrderId, decimal Amount, string Currency);

/// <summary>The payment provider refused. No money was taken.</summary>
[IntegrationEvent]
public sealed record PaymentFailedV1(OrderId OrderId, string Reason);
