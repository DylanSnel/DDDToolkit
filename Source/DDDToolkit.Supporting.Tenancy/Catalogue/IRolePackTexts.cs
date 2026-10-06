using System.Globalization;

namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// A role pack's name and description in one language, for the roles a tenant is given from the packs.
/// <para>
/// The catalogue declares each pack once, with one name and one description. A tenant that works in another
/// language should not get its roles named in the catalogue's, so the use cases ask this when they make a
/// role from a pack: when a tenant is provisioned with a <c>Language</c>, and when a change of shape copies
/// the packs the tenant has no copy of yet. The texts are chosen once. From then on the role is the tenant's
/// own, renamed like any other, and a later change of the tenant's language changes no role.
/// </para>
/// <para>
/// The application implements it over its own resources and registers it; without a language a role gets the
/// catalogue's texts, and so it does without one registered, the default administrators' pack apart (below).
/// </para>
/// <para>
/// <see cref="TenancyPacks.DefaultAdministrators"/>, which the catalogue adds when the application declares no
/// administrators' pack, is asked for like any pack, by <see cref="TenancyPacks.DefaultAdministratorsKey"/>.
/// Where this answers <see langword="null"/> for it, or none is registered, its role gets the package's own
/// texts in the language: English and Dutch ship with the package, and any other language gets the English.
/// </para>
/// <code>
/// public sealed class ShopPackTexts(IStringLocalizer&lt;ShopPacks&gt; texts) : IRolePackTexts
/// {
///     public (string Name, string Description)? For(RolePack pack, CultureInfo culture)
///     {
///         using (CultureScope.Use(culture))
///         {
///             var name = texts[pack.Key + ".name"];
///             return name.ResourceNotFound ? null : (name.Value, texts[pack.Key + ".description"].Value);
///         }
///     }
/// }
///
/// services.AddSingleton&lt;IRolePackTexts, ShopPackTexts&gt;();
/// </code>
/// </summary>
public interface IRolePackTexts
{
    /// <summary>
    /// The texts of <paramref name="pack"/> in <paramref name="culture"/>, or <see langword="null"/> to keep the
    /// catalogue's own; for <see cref="TenancyPacks.DefaultAdministrators"/>, to keep the package's, in English or
    /// Dutch.
    /// <para>
    /// What comes back is checked like any role's name and description: a name that is blank or too long is
    /// refused with <c>tenancy.name-invalid</c>, and one another role of the tenant has already, ignoring case,
    /// with <c>tenancy.role-name-taken</c>. A tenant holds a copy of every pack seeded for its shape, and a flat
    /// tenant that turns hierarchical is given the packs seeded for a hierarchical one next to them, so every pack
    /// needs a name of its own in every language.
    /// </para>
    /// </summary>
    /// <param name="pack">The pack a role is being made from, as the catalogue built it.</param>
    /// <param name="culture">The language the role is for: the tenant's.</param>
    (string Name, string Description)? For(RolePack pack, CultureInfo culture);
}
