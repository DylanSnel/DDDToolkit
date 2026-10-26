using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Errors;
using DDDToolkit.Invariants;
using DDDToolkit.Validation;
using HotChocolate;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace DDDToolkit.HotChocolate.Tests.Domain;

/// <summary>What a mutation on a parcel answers when it went through.</summary>
public sealed record Parcel(int Id, string Label);

/// <summary>
/// A mutation root whose fields fail the ways a use case fails, for the mutation conventions to turn into typed
/// errors in the payload. Each field declares nothing about errors: it only throws.
/// </summary>
public sealed class Parcels
{
    /// <summary>A value that must not come back in any response.</summary>
    public const string RejectedStreet = "Secret Street That Is Far Too Long For Anyone";

    /// <summary>The code of a relabel without a label.</summary>
    public const string LabelRequired = "parcels.label-required";

    /// <summary>The most characters a label has.</summary>
    public const int LongestLabel = 40;

    /// <summary>Relabels a parcel, or refuses a blank label, naming the input and the values its message was built from.</summary>
    public Parcel ParcelRelabel(int id, string label)
        => string.IsNullOrWhiteSpace(label)
            ? throw new RefusalException(
                LabelRequired,
                RefusalKind.Invalid,
                "A parcel needs a label of at most 40 characters.",
                new Dictionary<string, object?>
                {
                    [RefusalException.FieldArgument] = "label",
                    ["MaxLength"] = LongestLabel,
                    ["Weight"] = 1.5m,
                    ["Fragile"] = true,
                    ["Depot"] = null,
                })
            : new Parcel(id, label);

    /// <summary>Refuses an address for two reasons at once.</summary>
    public Parcel ParcelAddress(int id, string street)
        => throw new InvalidValueObjectException(typeof(Parcel),
        [
            new ValidationError("A street is at most 40 characters.", "Street", "Street.TooLong", RejectedStreet).With("MaxLength", 40),
            new ValidationError("A city is required.", "City", "City.Required"),
        ]);

    /// <summary>Refuses an address without saying why.</summary>
    public Parcel ParcelReaddress(int id) => throw new InvalidValueObjectException(typeof(Parcel));

    /// <summary>Breaks two rules, the second reported by a child.</summary>
    public Parcel ParcelSeal(int id)
        => throw new InvariantViolationException(typeof(Parcel), id,
        [
            new InvariantViolation("Parcel.OverWeight", "A parcel weighs at most 30 kilograms.", typeof(Parcel), id).With("MaxWeight", 30),
            new InvariantViolation("Stamp.Missing", "A sealed parcel carries a stamp.", typeof(Stamp), "STM_7"),
        ]);

    /// <summary>Breaks a rule that was written as a sentence only.</summary>
    public Parcel ParcelReseal(int id) => throw new InvariantViolationException("A parcel is sealed once.");

    /// <summary>Loses a race with somebody else's change.</summary>
    public Parcel ParcelMove(int id) => throw new ConcurrencyConflictException(typeof(Parcel), id);

    /// <summary>Refuses with a class derived from the refusal, which the conventions do not recognize.</summary>
    public Parcel ParcelReturn(int id) => throw new ReturnRefused();

    /// <summary>Fails in a way that has nothing to do with the toolkit.</summary>
    public Parcel ParcelLose(int id) => throw new InvalidOperationException("boom");

    /// <summary>A mutation that answers a scalar and takes nothing.</summary>
    public int ParcelCount() => 3;

    /// <summary>
    /// Fails with an exception of the application's own, which this one field maps to an error type of its own,
    /// beside the toolkit's four.
    /// </summary>
    [Error<OutOfReachError>]
    public Parcel ParcelDeliver(int id) => throw new OutOfReachException("the far shore");
}

/// <summary>An exception that is the application's own: neither a refusal nor a broken rule.</summary>
public sealed class OutOfReachException(string place) : Exception("No courier reaches " + place + ".")
{
    public string Place { get; } = place;
}

/// <summary>
/// An error type of the application's own. It implements <see cref="ICodedError"/>, as every error type of a
/// schema with the conventions has to: the interface is the schema's interface for errors.
/// </summary>
public sealed class OutOfReachError : ICodedError
{
    private readonly OutOfReachException _exception;

    private OutOfReachError(OutOfReachException exception) => _exception = exception;

    public static OutOfReachError CreateErrorFrom(OutOfReachException exception) => new(exception);

    public string Code => "parcels.out-of-reach";

    public string GetMessage(IResolverContext context) => _exception.Message;

    public IReadOnlyList<FailureArgument> Arguments => [new FailureArgument("Place", _exception.Place)];
}

/// <summary>A child of a parcel, to report a violation from.</summary>
public sealed record Stamp(string Text);

/// <summary>A refusal of a type of its own: an application may derive one, and the conventions match exact types.</summary>
public sealed class ReturnRefused() : RefusalException("parcels.return-closed", RefusalKind.Conflict, "Returns are closed for this parcel.");
