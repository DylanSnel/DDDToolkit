using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace DDDToolkit.Localization;

/// <summary>Where <see cref="FailureLocalizer"/> finds your translations, in the order it asks.</summary>
public sealed class FailureLocalizationOptions
{
    private readonly List<(string Name, Func<IServiceProvider, IStringLocalizer> Create)> _sources = [];

    /// <summary>
    /// Adds the resx that belongs to <typeparamref name="TResource"/>, found the way
    /// <c>IStringLocalizer&lt;TResource&gt;</c> finds it. Needs <c>services.AddLocalization()</c>.
    /// </summary>
    /// <typeparam name="TResource">The marker type the resx is named after, such as a <c>SharedFailures</c> class.</typeparam>
    public FailureLocalizationOptions AddResource<TResource>() => AddResource(typeof(TResource));

    /// <summary>
    /// Adds the resx that belongs to <paramref name="resourceSource"/>. Needs <c>services.AddLocalization()</c>.
    /// </summary>
    /// <param name="resourceSource">The marker type the resx is named after.</param>
    public FailureLocalizationOptions AddResource(Type resourceSource)
    {
        ArgumentNullException.ThrowIfNull(resourceSource);

        _sources.Add((resourceSource.Name + ".resx", provider => Resx(provider, resourceSource)));
        return this;
    }

    /// <summary>
    /// Adds the resx that belongs to <typeparamref name="TResource"/> for failures that carry other codes than its
    /// entries are named by. Needs <c>services.AddLocalization()</c>.
    /// <para>
    /// A package that ships its texts cannot know the codes an application gives its failures when the
    /// application chooses them, a prefix per module say. The package names each entry once, and the application
    /// says which of its codes reads which entry:
    /// </para>
    /// <code>
    /// options.AddResource&lt;SubscriptionFailures&gt;(new Dictionary&lt;string, string&gt;
    /// {
    ///     ["billing.plan-closed"] = "subscription.plan-closed",
    /// });
    /// </code>
    /// <para>
    /// Only the codes in <paramref name="entries"/> are answered: an entry's own name is not, unless it is among
    /// them. Several codes may read one entry, and the same resx may be added more than once, each time for
    /// other codes.
    /// </para>
    /// </summary>
    /// <typeparam name="TResource">The marker type the resx is named after.</typeparam>
    /// <param name="entries">For each code a failure carries, the name of the entry that holds its text.</param>
    public FailureLocalizationOptions AddResource<TResource>(IReadOnlyDictionary<string, string> entries)
        => AddResource(typeof(TResource), entries);

    /// <summary>
    /// Adds the resx that belongs to <paramref name="resourceSource"/> for failures that carry other codes than
    /// its entries are named by. Needs <c>services.AddLocalization()</c>.
    /// </summary>
    /// <param name="resourceSource">The marker type the resx is named after.</param>
    /// <param name="entries">For each code a failure carries, the name of the entry that holds its text.</param>
    /// <exception cref="ArgumentException">A code or an entry's name in <paramref name="entries"/> is empty.</exception>
    public FailureLocalizationOptions AddResource(Type resourceSource, IReadOnlyDictionary<string, string> entries)
    {
        ArgumentNullException.ThrowIfNull(resourceSource);
        ArgumentNullException.ThrowIfNull(entries);

        // Copied, so the codes a source answers are the ones it was added with, whatever happens to the caller's map.
        var renamed = new Dictionary<string, string>(entries.Count, StringComparer.Ordinal);
        foreach (var (code, entry) in entries)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(entry))
            {
                throw new ArgumentException("A code and the entry that holds its text both have a name.", nameof(entries));
            }

            renamed[code] = entry;
        }

        _sources.Add((resourceSource.Name + ".resx", provider => new RenamedLocalizer(Resx(provider, resourceSource), renamed)));
        return this;
    }

    /// <summary>Adds a localizer you built yourself, such as one that reads translations from a database.</summary>
    /// <param name="localizer">The localizer to ask.</param>
    public FailureLocalizationOptions AddLocalizer(IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        _sources.Add((localizer.GetType().Name, _ => localizer));
        return this;
    }

    internal IReadOnlyList<(string Name, Func<IServiceProvider, IStringLocalizer> Create)> Sources => _sources;

    /// <summary>The localizer of the resx named after <paramref name="resourceSource"/>, from the host's factory.</summary>
    private static IStringLocalizer Resx(IServiceProvider provider, Type resourceSource)
        => (provider.GetService<IStringLocalizerFactory>() ?? throw new InvalidOperationException(
                "AddResource(" + resourceSource.Name + ") reads its translations through IStringLocalizerFactory, and none is "
                + "registered. Call services.AddLocalization() as well, or add a localizer of your own with AddLocalizer."))
            .Create(resourceSource);
}

/// <summary>
/// One source of translations, registered as a service of its own so that every call to
/// <see cref="DependencyInjection.AddDDDToolkitLocalization"/> adds to the same list. That is what lets
/// each module of a modular monolith register its own resx from its own composition method.
/// </summary>
internal sealed class FailureLocalizationSource(string name, Func<IServiceProvider, IStringLocalizer> create)
{
    public NamedSource Create(IServiceProvider provider) => new(name, create(provider));
}
