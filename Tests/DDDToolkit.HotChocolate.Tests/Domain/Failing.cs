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

    /// <summary>Refuses a command, naming the value its message was built from.</summary>
    public string Refused()
        => throw new RefusalException(
            "subscription.plan-closed",
            RefusalKind.Conflict,
            "The gold plan is closed.",
            new Dictionary<string, object?> { ["Plan"] = "gold", ["Seats"] = 3 });

    /// <summary>Refuses a command for one of its inputs, which it names.</summary>
    public string RefusedAbout()
        => throw new RefusalException(
            "subscription.plan-unknown",
            RefusalKind.Invalid,
            "There is no such plan.",
            new Dictionary<string, object?> { [RefusalException.FieldArgument] = "plan" });

    /// <summary>Refuses a command because of one of its inputs, and names that input.</summary>
    public string RefusedInput()
        => throw new RefusalException(
            "subscription.plan-name-invalid",
            RefusalKind.Invalid,
            "A plan's name takes 1 to 40 characters.",
            new Dictionary<string, object?> { [RefusalException.FieldArgument] = "name", ["Max"] = 40 });

    /// <summary>Fails in a way that has nothing to do with the toolkit.</summary>
    public string Crash() => throw new InvalidOperationException("boom");
}
