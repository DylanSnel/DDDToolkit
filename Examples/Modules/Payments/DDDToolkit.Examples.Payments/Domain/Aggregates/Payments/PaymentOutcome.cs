namespace DDDToolkit.Examples.Payments.Domain.Payments;

/// <summary>What happened at the provider, in this module's words.</summary>
public sealed record PaymentOutcome(bool Taken, string? Refusal)
{
    public static PaymentOutcome Captured { get; } = new(true, null);

    public static PaymentOutcome Declined(string reason) => new(false, reason);
}

