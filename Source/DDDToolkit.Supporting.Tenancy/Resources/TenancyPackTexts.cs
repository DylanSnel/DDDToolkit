using System.Globalization;
using System.Resources;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// The name and description of the package's own pack, <see cref="TenancyPacks.DefaultAdministrators"/>, in the
/// languages the package ships: <c>TenancyPackTexts.resx</c> in English, which are the pack's own texts, and
/// <c>TenancyPackTexts.nl.resx</c> in Dutch.
/// <para>
/// The use cases read them when they make a role from that pack in a language the application's
/// <see cref="IRolePackTexts"/> has no texts for, or when it registered none: the application did not declare
/// the pack, so it is the package that names it. They are read here rather than through the application's
/// localizer, which holds refusals, so the role is named in Dutch whether or not the application localizes
/// anything. A language the package does not ship falls back to the English texts, which are the catalogue's.
/// </para>
/// </summary>
internal static class TenancyPackTexts
{
    private static readonly ResourceManager Texts = new(typeof(TenancyPackTexts));

    /// <summary>
    /// The languages the package translates these texts into, each a satellite assembly beside the package's own;
    /// the English texts are the assembly's neutral resources, and the pack's own.
    /// </summary>
    private static readonly CultureInfo[] Translated = [CultureInfo.GetCultureInfo("nl")];

    /// <summary>The texts of <see cref="TenancyPacks.DefaultAdministrators"/> in <paramref name="culture"/>, or its parent's, or English.</summary>
    public static (string Name, string Description)? DefaultAdministratorsIn(CultureInfo culture)
        => Texts.GetString(TenancyPacks.DefaultAdministratorsKey + ".name", culture) is { } name
            ? (name, Texts.GetString(TenancyPacks.DefaultAdministratorsKey + ".description", culture) ?? string.Empty)
            : null;

    /// <summary>
    /// The names the package gives <see cref="TenancyPacks.DefaultAdministrators"/> in the languages it
    /// translates it into, trimmed, each with its language: the ones that differ, ignoring case, from the pack's
    /// own, which is the English one.
    /// <para>
    /// A role made from the pack can have any of them, so the catalogue refuses a pack of the application's that
    /// is named like one when it adds the default: a tenant provisioned in Dutch would otherwise be refused half
    /// way, two of its roles named alike, by a name the application never wrote. A language whose satellite
    /// assembly was left out of the deployment gives no name here, as it gives none to a role.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(string Name, CultureInfo Language)> DefaultAdministratorsTranslations()
    {
        var names = new List<(string Name, CultureInfo Language)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TenancyPacks.DefaultAdministrators.Name.Trim() };
        foreach (var language in Translated)
        {
            var name = Texts.GetResourceSet(language, createIfNotExists: true, tryParents: false)
                ?.GetString(TenancyPacks.DefaultAdministratorsKey + ".name")
                ?.Trim();
            if (!string.IsNullOrEmpty(name) && seen.Add(name))
            {
                names.Add((name, language));
            }
        }

        return names;
    }
}
