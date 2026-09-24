using DDDToolkit.Examples.Payments.Domain.Payments;
using DDDToolkit.Examples.Payments.Domain.Services;
using DDDToolkit.Examples.SharedKernel;

namespace DDDToolkit.Examples.Payments.Infrastructure.PaymentProvider;

/// <summary>
/// A stand-in for a real provider, with the translation a real adapter would need. It declines anything
/// over 1,000, which is how the example shows a refused payment without a card.
/// </summary>
/// <remarks>
/// The interesting half is <see cref="Translate"/>. <see cref="ProviderResponse"/> is shaped the way
/// providers answer: result codes, amounts in minor units, reasons in their words. None of it leaks past
/// this class. If the shop changes provider, this file changes and <c>Payment</c> does not.
/// </remarks>
public sealed class FakePaymentProvider : IPaymentProvider
{
    private const long LimitInCents = 1_000_00;

    private readonly Dictionary<string, ProviderResponse> _answered = [];
    private readonly Lock _gate = new();

    public Task<PaymentOutcome> ChargeAsync(string idempotencyKey, Money amount, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(amount);

        ProviderResponse response;
        lock (_gate)
        {
            if (!_answered.TryGetValue(idempotencyKey, out response!))
            {
                var cents = (long)(amount.Amount * 100);
                response = cents > LimitInCents
                    ? new ProviderResponse("REFUSED", cents, amount.Currency, "LIMIT_EXCEEDED")
                    : new ProviderResponse("AUTHORISED", cents, amount.Currency, null);
                _answered[idempotencyKey] = response;
            }
        }

        return Task.FromResult(Translate(response));
    }

    /// <summary>The provider's vocabulary in, the domain's out.</summary>
    private static PaymentOutcome Translate(ProviderResponse response) => response switch
    {
        { ResultCode: "AUTHORISED" } => PaymentOutcome.Captured,
        { RefusalReason: "LIMIT_EXCEEDED" } => PaymentOutcome.Declined("The amount is over the card's limit."),
        { RefusalReason: { } reason } => PaymentOutcome.Declined($"Refused by the provider ({reason})."),
        _ => PaymentOutcome.Declined("Refused by the provider."),
    };

    /// <summary>What a provider sends back. Nothing outside this class ever sees one.</summary>
    private sealed record ProviderResponse(string ResultCode, long AmountInMinorUnits, string Currency, string? RefusalReason);
}
