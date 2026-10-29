using System.Globalization;

namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// A starter role's name and description in one language, for the roles a tenant is given from the starter roles.
/// <para>
/// The catalogue declares each starter role once, with one name and one description. A tenant that works in another
/// language should not get its roles named in the catalogue's, so the managers ask this when they make a
/// role from a starter role: when a tenant is provisioned with a <c>Language</c>, and when a change of shape copies
/// the starter roles the tenant has no copy of yet. The texts are chosen once. From then on the role is the tenant's
/// own, renamed like any other, and a later change of the tenant's language changes no role.
/// </para>
/// <para>
/// The application implements it over its own resources and registers it; without a language a role gets the
/// catalogue's texts, and so it does without one registered, the default administrators' starter role apart (below).
/// </para>
/// <para>
/// <see cref="TenancyStarterRoles.DefaultAdministrators"/>, which the catalogue adds when the application declares no
/// administrators' starter role, is asked for like any starter role, by <see cref="TenancyStarterRoles.DefaultAdministratorsKey"/>.
/// Where this answers <see langword="null"/> for it, or none is registered, its role gets the package's own
/// texts in the language: English and Dutch ship with the package, and any other language gets the English.
/// </para>
/// <code>
/// public sealed class ShopStarterRoleTexts(IStringLocalizer&lt;ShopStarterRoles&gt; texts) : IStarterRoleTexts
/// {
///     public (string Name, string Description)? For(StarterRole starterRole, CultureInfo culture)
///     {
///         using (CultureScope.Use(culture))
///         {
///             var name = texts[starterRole.Key + ".name"];
///             return name.ResourceNotFound ? null : (name.Value, texts[starterRole.Key + ".description"].Value);
///         }
///     }
/// }
///
/// services.AddSingleton&lt;IStarterRoleTexts, ShopStarterRoleTexts&gt;();
/// </code>
/// </summary>
public interface IStarterRoleTexts
{
    /// <summary>
    /// The texts of <paramref name="starterRole"/> in <paramref name="culture"/>, or <see langword="null"/> to keep the
    /// catalogue's own; for <see cref="TenancyStarterRoles.DefaultAdministrators"/>, to keep the package's, in English or
    /// Dutch.
    /// <para>
    /// What comes back is checked like any role's name and description: a name that is blank or too long is
    /// refused with <c>tenancy.name-invalid</c>, and one another role of the tenant has already, ignoring case,
    /// with <c>tenancy.role-name-taken</c>. A tenant holds a copy of every starter role seeded for its shape, and a flat
    /// tenant that turns hierarchical is given the starter roles seeded for a hierarchical one next to them, so every starter role
    /// needs a name of its own in every language.
    /// </para>
    /// </summary>
    /// <param name="starterRole">The starter role a role is being made from, as the catalogue built it.</param>
    /// <param name="culture">The language the role is for: the tenant's.</param>
    (string Name, string Description)? For(StarterRole starterRole, CultureInfo culture);
}
