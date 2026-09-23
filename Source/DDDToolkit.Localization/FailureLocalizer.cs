using System.Globalization;
using System.Resources;
using DDDToolkit.Invariants;
using DDDToolkit.Validation;
using Microsoft.Extensions.Localization;

namespace DDDToolkit.Localization;

/// <summary>
/// Looks a failure's translation up by its code in the <see cref="IStringLocalizer"/>s you give it, and
/// fills the translation with the failure's values.
/// <para>
/// The key is the failure's code. An invariant violation is first looked up as
/// <c>{EntityType}.{Code}</c> (<c>Order.MustHaveLines</c>) and then as the bare code, so a rule shared
/// by several entities can have one translation and still be told apart where it has to be. Sources are
/// asked in the order they were added; the toolkit's own messages
/// (<see cref="ValidationError.UnspecifiedCode"/>, <c>MustBeValid()</c>'s <c>ValueObjectValidator</c>)
/// come last, in English and Dutch, so any of them can be overridden. When nothing knows the code, the
/// failure's own message is returned unchanged.
/// </para>
/// <para>
/// A template may name any of the failure's <c>Arguments</c>, and besides those:
/// </para>
/// <list type="bullet">
/// <item>for a <see cref="ValidationError"/>, <c>{PropertyName}</c>, <c>{AttemptedValue}</c> and <c>{Code}</c>;</item>
/// <item>for an <see cref="InvariantViolation"/>, <c>{EntityType}</c>, <c>{EntityId}</c> and <c>{Code}</c>.</item>
/// </list>
/// <para>An argument of the same name wins.</para>
/// </summary>
public sealed class FailureLocalizer : IFailureLocalizer
{
    private static readonly ResourceManager BuiltIn
        = new("DDDToolkit.Localization.Resources.FailureMessages", typeof(FailureLocalizer).Assembly);

    private readonly NamedSource[] _sources;

    /// <summary>Creates a localizer that asks <paramref name="sources"/> in order, and the toolkit's own messages last.</summary>
    /// <param name="sources">Where your translations live, such as the localizer for a shared resx.</param>
    public FailureLocalizer(IEnumerable<IStringLocalizer> sources)
        : this(Named(sources))
    {
    }

    /// <summary>The same, with a name per source for <see cref="FailureTranslations"/> to report.</summary>
    internal FailureLocalizer(IEnumerable<NamedSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = [.. sources];
        foreach (var source in _sources)
        {
            ArgumentNullException.ThrowIfNull(source.Localizer, nameof(sources));
        }
    }

    /// <summary>Your sources, in the order they are asked.</summary>
    internal IReadOnlyList<NamedSource> Sources => _sources;

    /// <summary>The toolkit's own messages, asked after every source.</summary>
    internal static ResourceManager ToolkitResources => BuiltIn;

    private static IEnumerable<NamedSource> Named(IEnumerable<IStringLocalizer> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return sources.Select(source => new NamedSource(source?.GetType().Name ?? "null", source!));
    }

    /// <summary>A localizer that knows only the toolkit's own messages.</summary>
    public static FailureLocalizer Default { get; } = new(Array.Empty<IStringLocalizer>());

    /// <inheritdoc />
    public string Localize(ValidationError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var template = error.Code is null ? null : Find(error.Code);
        if (template is null)
        {
            return error.Message;
        }

        return FailureTemplate.Format(template, (string name, out object? value) =>
        {
            if (error.Arguments.TryGetValue(name, out value))
            {
                return true;
            }

            value = Builtin(name) switch
            {
                nameof(ValidationError.PropertyName) => error.PropertyName,
                nameof(ValidationError.AttemptedValue) => error.AttemptedValue,
                nameof(ValidationError.Code) => error.Code,
                _ => Unknown,
            };

            return !ReferenceEquals(value, Unknown);
        });
    }

    /// <inheritdoc />
    public string Localize(InvariantViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);

        var template = (violation.EntityType is null ? null : Find(violation.EntityType.Name + "." + violation.Code))
                       ?? Find(violation.Code);
        if (template is null)
        {
            return violation.Message;
        }

        return FailureTemplate.Format(template, (string name, out object? value) =>
        {
            if (violation.Arguments.TryGetValue(name, out value))
            {
                return true;
            }

            value = Builtin(name) switch
            {
                nameof(InvariantViolation.EntityType) => violation.EntityType,
                nameof(InvariantViolation.EntityId) => violation.EntityId,
                nameof(InvariantViolation.Code) => violation.Code,
                _ => Unknown,
            };

            return !ReferenceEquals(value, Unknown);
        });
    }

    /// <summary>Stands for "no such placeholder", which is different from a placeholder whose value is null.</summary>
    private static readonly object Unknown = new();

    /// <summary>The built-in placeholder names, matched without regard to case like the arguments are.</summary>
    private static string? Builtin(string name)
    {
        foreach (var known in (ReadOnlySpan<string>)
                 [
                     nameof(ValidationError.PropertyName),
                     nameof(ValidationError.AttemptedValue),
                     nameof(InvariantViolation.EntityType),
                     nameof(InvariantViolation.EntityId),
                     nameof(ValidationError.Code),
                 ])
        {
            if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return null;
    }

    private string? Find(string key)
    {
        foreach (var source in _sources)
        {
            var found = source.Localizer[key];
            if (!found.ResourceNotFound)
            {
                return found.Value;
            }
        }

        return BuiltIn.GetString(key, CultureInfo.CurrentUICulture);
    }
}

/// <summary>A source of translations and the name a report calls it by, such as <c>SharedFailures.resx</c>.</summary>
internal sealed record NamedSource(string Name, IStringLocalizer Localizer);
