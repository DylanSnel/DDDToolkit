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

        _sources.Add((resourceSource.Name + ".resx", provider => (provider.GetService<IStringLocalizerFactory>() ?? throw new InvalidOperationException(
                "AddResource(" + resourceSource.Name + ") reads its translations through IStringLocalizerFactory, and none is "
                + "registered. Call services.AddLocalization() as well, or add a localizer of your own with AddLocalizer."))
            .Create(resourceSource)));
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
