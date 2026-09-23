namespace DDDToolkit.Invariants;

/// <summary>
/// One named rule that must hold for <typeparamref name="TEntity"/>.
/// <para>
/// Write these as nested types of the entity they are about, each in its own file under an
/// <c>Invariants</c> folder:
/// </para>
/// <code>
/// // Ordering/Invariants/MustHaveLines.cs
/// public partial class Order
/// {
///     public sealed class MustHaveLines : IInvariant&lt;Order&gt;
///     {
///         // ...
///     }
/// }
/// </code>
/// <para>
/// Nesting is not a style preference. A nested type can read the enclosing type's private state, so
/// a rule about <c>_lines</c> does not force <c>_lines</c> to become public; and the generator
/// already holds the entity's symbol, so it finds these without scanning the compilation. An
/// implementation that is not nested inside the entity it is about is reported by
/// <c>DDDToolkit.Analyzers</c> rather than silently never running.
/// </para>
/// <para>
/// Implementations must be stateless and have an accessible parameterless constructor: the generator
/// creates one instance per entity type and reuses it for every check.
/// </para>
/// </summary>
/// <typeparam name="TEntity">The entity or aggregate this rule is about.</typeparam>
public interface IInvariant<in TEntity>
{
    /// <summary>
    /// A stable machine-readable identifier for this rule, so a caller can branch on it without
    /// matching on text and without the message becoming part of your API. It is also what
    /// <c>DDDToolkit.Analyzers</c> reads to report two rules on one entity sharing a code.
    /// </summary>
    string Code { get; }

    /// <summary>
    /// Runs the rule. Returns <see langword="null"/> when it holds, and otherwise what is wrong in the
    /// domain's own words, free to name the values that broke it. A string converts to
    /// <see cref="InvariantFailure"/>, so a rule with only a message returns the message; one that names
    /// values adds them with <see cref="InvariantFailure.With"/> so the message can be translated.
    /// <para>
    /// Not an <see cref="InvariantViolation"/> so that holding is free: this runs for every changed
    /// entity on every save, and the happy path returns <see langword="null"/> and allocates nothing. The
    /// caller pairs the failure with <see cref="Code"/>, so the code lives in one place.
    /// </para>
    /// </summary>
    /// <param name="entity">The object to check. Never <see langword="null"/>.</param>
    InvariantFailure? Check(TEntity entity);
}
