using DDDToolkit.Validation;

namespace DDDToolkit.Exceptions;

/// <summary>
/// Why a command was refused, in the four answers an edge maps to a response: the request was not
/// acceptable, the caller may not do it, what it names does not exist, or the state it met does not allow
/// it.
/// </summary>
public enum RefusalKind
{
    /// <summary>The request itself is not acceptable: a name too long, a key nobody declared. Usually a 400.</summary>
    Invalid,

    /// <summary>The caller may not do this. Usually a 403.</summary>
    NotPermitted,

    /// <summary>Something the request names does not exist, or is not the caller's to see. Usually a 404.</summary>
    NotFound,

    /// <summary>The state the command met does not allow it: already archived, the last one of its kind. Usually a 409.</summary>
    Conflict,
}

/// <summary>
/// Thrown when a command is refused: a precondition that does not hold, a permission the caller lacks, or
/// a state that does not allow the change.
/// <para>
/// A refusal is neither a bug nor a broken invariant. An invariant says "this object is in a state the
/// domain says cannot exist", and is checked after the change; a refusal says "this change is not made",
/// and is decided before anything changed, so the aggregate is exactly as it was. Retrying the same
/// command gives the same answer until something else changes.
/// </para>
/// <para>
/// It carries what an edge needs to answer without reading the message: a stable <see cref="Code"/> to
/// branch on and to translate, a <see cref="Kind"/> to choose the status from, and the
/// <see cref="Arguments"/> the message was built from. <c>IFailureLocalizer.Localize(RefusalException)</c>
/// phrases it in the reader's language, and <c>DDDToolkit.HotChocolate</c>'s error filter turns it into
/// one coded GraphQL error.
/// </para>
/// <para>
/// There is no <c>Result&lt;T&gt;</c> beside it, for the reason there is none for value objects: a
/// result type turns up in every signature it touches, and a codebase with one ends up with two.
/// </para>
/// </summary>
public class RefusalException : DDDToolkitException
{
    /// <summary>
    /// The argument by which a refusal of kind <see cref="RefusalKind.Invalid"/> names the input it is about,
    /// such as <c>name</c>, so a form can put the text under that input. It is the domain's word for the input;
    /// an edge whose input is called otherwise maps it.
    /// </summary>
    public const string FieldArgument = "Field";

    /// <summary>Creates a refusal.</summary>
    /// <param name="code">
    /// A stable, machine-readable identifier for the rule that refused, such as <c>subscription.plan-closed</c>.
    /// It is what a caller branches on and what a translation is looked up by, so it is required.
    /// </param>
    /// <param name="kind">Which of the four answers this is.</param>
    /// <param name="message">What was refused and why, in the domain's own words.</param>
    /// <param name="arguments">
    /// The values the message was built from, by name, so a translation can put them where its own grammar
    /// wants them. Copied, so the caller's dictionary can change afterwards without the refusal changing
    /// with it.
    /// </param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is null or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not one of the four kinds.</exception>
    public RefusalException(
        string code,
        RefusalKind kind,
        string message,
        IReadOnlyDictionary<string, object?>? arguments = null,
        Exception? innerException = null)
        : base(message ?? throw new ArgumentNullException(nameof(message)), innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A refusal is Invalid, NotPermitted, NotFound or Conflict.");
        }

        Code = code;
        Kind = kind;
        Arguments = FailureArguments.Copy(arguments);
    }

    /// <summary>The rule that refused, stable across releases and languages.</summary>
    public string Code { get; }

    /// <summary>Which of the four answers this is, for the edge to choose a status from.</summary>
    public RefusalKind Kind { get; }

    /// <summary>
    /// The values <see cref="Exception.Message"/> was built from, by name. A read-only copy whose names are
    /// matched without regard to case, like the arguments of every other failure. Empty when there are
    /// none; never <see langword="null"/>.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; }
}
