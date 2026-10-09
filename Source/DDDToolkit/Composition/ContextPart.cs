namespace DDDToolkit.Composition;

/// <summary>
/// A part of a context: what one package adds to the options of an Entity Framework context, as row level security
/// and Tenancy's save check do, which <c>UseDDDToolkit</c> applies to every context it is called on. The package
/// registers it where it registers what the part is about, so a host that uses the package gets the part without
/// naming it, and the one call puts every registered part in its place.
/// <code>
/// services.AddContextPart(new ContextPart&lt;DbContextOptionsBuilder&gt;(
///     "billing.audit",
///     position: 150,                     // before Tenancy's save check, which comes last
///     (options, provider) => options.UseBillingAudit(provider)));
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The call that applies the parts goes through them by <see cref="Position"/>, and passes over a part that does not
/// belong on the builder at hand (<see cref="AppliesTo"/>). Whether it belongs is worked out from what the builder
/// holds at that moment, its provider say; what the builder cannot say yet is the part's own to decide later, where it
/// can: an interceptor that does nothing for a context whose model, or whose provider configured after the call,
/// gives it nothing to do.
/// </para>
/// <para>
/// A part is applied with the package's own method, the one a host calls when it puts the parts together itself, and
/// that method adds nothing the builder already has: a host that calls it as well, before or after the one call, gets
/// the part once.
/// </para>
/// </remarks>
/// <typeparam name="TBuilder">What the part is applied to: <c>DbContextOptionsBuilder</c> for a context's options.</typeparam>
public sealed class ContextPart<TBuilder>
    where TBuilder : class
{
    /// <summary>A part named <paramref name="name"/>, at <paramref name="position"/>, applied by <paramref name="apply"/>.</summary>
    /// <param name="name">
    /// What a log and a test say it as: the package's own prefix, a dot, and what it adds, such as
    /// <c>postgres.row-level-security</c>. A second part registered under a name already taken is not registered, so a
    /// registration a host calls more than once brings its part once.
    /// </param>
    /// <param name="position">
    /// Where it goes among the parts: one with a lower position is applied before one with a higher, and two with the
    /// same position in the order they were registered. Whatever the call that applies the parts adds of its own comes
    /// before every part.
    /// </param>
    /// <param name="apply">
    /// Adds the part: handed the builder and the services the call that applies the parts was handed. It adds nothing
    /// the builder already has, so a host may call the package's own method as well.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or holds white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="apply"/> is null.</exception>
    public ContextPart(string name, int position, Action<TBuilder, IServiceProvider> apply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(apply);

        if (name.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"A part's name holds no white space, and '{name}' does: it is written in code and in logs as one word.", nameof(name));
        }

        Name = name;
        Position = position;
        Apply = apply;
    }

    /// <summary>What a log and a test say it as.</summary>
    public string Name { get; }

    /// <summary>Where it goes among the parts: lower first.</summary>
    public int Position { get; }

    /// <summary>Adds the part to the builder, with the services the call that applies the parts was handed.</summary>
    public Action<TBuilder, IServiceProvider> Apply { get; }

    /// <summary>
    /// Whether the part belongs on the builder at hand, worked out from what the builder holds when the parts are
    /// applied: row level security on a context whose provider is Postgres's, and on no other. Left out, the part goes
    /// on every builder. Where the builder cannot tell yet, its provider not configured say, a part answers that it
    /// belongs, and what it adds passes over at use what it has nothing to do for: so it fails closed, and a context
    /// that sets its provider later, in <c>OnConfiguring</c> say, still gets it. What it throws comes out of the call
    /// that applies the parts as it is, so its message is what the developer reads.
    /// </summary>
    public Func<TBuilder, bool>? AppliesTo { get; init; }

    /// <inheritdoc />
    public override string ToString() => Name + " (" + Position + ")";
}
