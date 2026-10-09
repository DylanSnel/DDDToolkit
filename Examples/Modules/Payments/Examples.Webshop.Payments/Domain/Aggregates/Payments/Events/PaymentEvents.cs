using DDDToolkit.BaseTypes;
using Examples.Webshop.Ordering.Contracts;
using Examples.Webshop.SharedKernel;

namespace Examples.Webshop.Payments.Domain.Payments;

/// <summary>Published as <c>PaymentSucceededV1</c>.</summary>
public sealed record PaymentCaptured(OrderId OrderId, Money Amount) : DomainEvent;

/// <summary>Published as <c>PaymentFailedV1</c>.</summary>
public sealed record PaymentDeclined(OrderId OrderId, string Reason) : DomainEvent;
