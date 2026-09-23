using DDDToolkit.Examples.Payments.Domain.Payments;
using DDDToolkit.Examples.SharedKernel;

namespace DDDToolkit.Examples.Payments.Domain.Services;

/// <summary>
/// Takes money through an outside payment provider. The port of an <b>anti-corruption layer</b>: the
/// domain asks in its own terms and hears back a <see cref="PaymentOutcome"/>, whatever the provider
/// calls things.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>
    /// Charges <paramref name="amount"/>. <paramref name="idempotencyKey"/> must make a repeated call
    /// return the first call's answer without charging again: the handler that calls this can be retried
    /// after the charge went through and before its own save did.
    /// </summary>
    Task<PaymentOutcome> ChargeAsync(string idempotencyKey, Money amount, CancellationToken cancellationToken);
}

