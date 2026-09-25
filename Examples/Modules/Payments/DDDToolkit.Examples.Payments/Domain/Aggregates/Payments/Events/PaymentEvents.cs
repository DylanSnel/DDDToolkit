using DDDToolkit.BaseTypes;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.SharedKernel;

namespace DDDToolkit.Examples.Payments.Domain.Payments;

/// <summary>Published as <c>PaymentSucceededV1</c>.</summary>
public sealed record PaymentCaptured(OrderId OrderId, Money Amount) : DomainEvent;

/// <summary>Published as <c>PaymentFailedV1</c>.</summary>
public sealed record PaymentDeclined(OrderId OrderId, string Reason) : DomainEvent;
