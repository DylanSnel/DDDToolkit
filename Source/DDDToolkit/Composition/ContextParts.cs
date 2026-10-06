namespace DDDToolkit.Composition;

/// <summary>
/// The parts a host's registrations brought for one kind of builder, in the order they are applied.
/// <see cref="ContextPartServiceCollectionExtensions"/> keeps one in the host's services, the call that applies the
/// parts resolves it, and a test reads it to see what a host would apply.
/// </summary>
/// <remarks>
/// The parts are registered while the host is composed, one registration after the other, and read once its
/// services are built, by every builder they are applied to: what is read then is a list no registration changes
/// any more.
/// </remarks>
/// <typeparam name="TBuilder">What the parts are applied to.</typeparam>
public sealed class ContextParts<TBuilder>
    where TBuilder : class
{
    private readonly List<ContextPart<TBuilder>> _registered = [];
    private ContextPart<TBuilder>[] _inOrder = [];

    /// <summary>
    /// Every registered part in the order they are applied: by <see cref="ContextPart{TBuilder}.Position"/>, and two
    /// of one position as they were registered.
    /// </summary>
    public IReadOnlyList<ContextPart<TBuilder>> InOrder => _inOrder;

    /// <summary>
    /// Applies to <paramref name="builder"/> every part that belongs on it, in order, and says which those were.
    /// </summary>
    /// <param name="builder">What the parts are applied to.</param>
    /// <param name="services">The services handed to each part.</param>
    /// <returns>The parts that belong on <paramref name="builder"/>, in the order they were applied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="services"/> is null.</exception>
    public IReadOnlyList<ContextPart<TBuilder>> ApplyTo(TBuilder builder, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(services);

        List<ContextPart<TBuilder>> applied = [];
        foreach (var part in _inOrder)
        {
            if (part.AppliesTo is null || part.AppliesTo(builder))
            {
                part.Apply(builder, services);
                applied.Add(part);
            }
        }

        return applied;
    }

    /// <summary>Registers <paramref name="part"/>, unless a part of its name is registered already.</summary>
    /// <returns>Whether it was registered.</returns>
    internal bool Add(ContextPart<TBuilder> part)
    {
        if (_registered.Exists(registered => string.Equals(registered.Name, part.Name, StringComparison.Ordinal)))
        {
            return false;
        }

        _registered.Add(part);

        // OrderBy is a stable sort, so two parts of one position stay as they were registered.
        _inOrder = [.. _registered.OrderBy(registered => registered.Position)];
        return true;
    }
}
