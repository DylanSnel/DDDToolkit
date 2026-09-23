using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using DDDToolkit.Validation;

namespace DDDToolkit.HotChocolate.Tests.Domain;

/// <summary>A query root whose fields fail the ways a domain fails, for the error filter to translate.</summary>
public sealed class Failing
{
    /// <summary>A value that must not come back in any response.</summary>
    public const string RejectedStreet = "Secret Street That Is Far Too Long For Anyone";

    /// <summary>Refuses a value object for two reasons at once.</summary>
    public string Refuse()
        => throw new InvalidValueObjectException(typeof(Failing),
        [
            new ValidationError("A street is at most 40 characters.", "Street", "Street.TooLong", RejectedStreet).With("MaxLength", 40),
            new ValidationError("A city is required.", "City", "City.Required"),
        ]);

    /// <summary>Breaks an invariant that names its values.</summary>
    public string BreakInvariant()
        => throw new InvariantViolationException(typeof(Failing), "ORD_1",
        [
            new InvariantViolation("Order.OverCreditLimit", "An order may total at most 1000.", typeof(Failing), "ORD_1")
                .With("CreditLimit", 1000m),
        ]);

    /// <summary>Fails in a way that has nothing to do with the toolkit.</summary>
    public string Crash() => throw new InvalidOperationException("boom");
}
