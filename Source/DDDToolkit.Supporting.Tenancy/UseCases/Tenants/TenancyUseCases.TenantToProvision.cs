using System.Globalization;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public static partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>What a new tenant is provisioned with.</summary>
    /// <param name="Slug">The slug it is selected by; trimmed and lowercased.</param>
    /// <param name="Name">The tenant's name, which is its organization's.</param>
    /// <param name="Shape">Whether its organization is flat or a tree.</param>
    /// <param name="RootName">The root unit's name.</param>
    /// <param name="AdminIdentity">The verified identity of the first administrator.</param>
    /// <param name="AdminDisplayName">The name the first administrator's seat is shown by.</param>
    /// <param name="TenantId">The tenant's id, for imports and seeding; a new one otherwise.</param>
    /// <param name="RootId">The root's id, for imports and seeding; a new one otherwise.</param>
    /// <param name="AdminSeatId">The first seat's id, for imports and seeding; a new one otherwise.</param>
    /// <param name="RoleIds">The ids of the roles copied from the packs, by pack key; the others get new ids.</param>
    /// <param name="Language">
    /// The language the tenant's roles are named in: each pack's name and description are asked of the
    /// application's <see cref="IRolePackTexts"/> in it. <see langword="null"/>, or an application that
    /// registered no texts, keeps the catalogue's own; the default administrators' pack, which the package adds,
    /// falls back to the package's own texts in it, English or Dutch. The package keeps no language itself:
    /// where the tenant remembers its language is a field of the application's tenant class, set in
    /// <paramref name="ConfigureTenant"/>.
    /// </param>
    /// <param name="ConfigureTenant">
    /// Sets the fields the application added to its tenant class, on the new tenant before it is activated, so
    /// they are saved in the same transaction and a domain event the class raises there goes out with the
    /// provisioning. When it throws, nothing is saved.
    /// </param>
    /// <param name="ConfigureRoot">
    /// The same for the fields the application added to its unit class, on the root: what kind of unit it is,
    /// say, when the class keeps one. A unit raises no events of its own; the organization raises the root's.
    /// </param>
    /// <param name="ConfigureFirstSeat">
    /// The same for the first administrator's seat, which is placed at the root and holds the administrators'
    /// role by then.
    /// </param>
    public sealed record TenantToProvision(
        string Slug,
        string Name,
        TenantShape Shape,
        string RootName,
        Guid AdminIdentity,
        string AdminDisplayName,
        TTenantId? TenantId = null,
        TUnitId? RootId = null,
        TSeatId? AdminSeatId = null,
        IReadOnlyDictionary<string, TRoleId>? RoleIds = null,
        CultureInfo? Language = null,
        Action<TTenant>? ConfigureTenant = null,
        Action<TUnit>? ConfigureRoot = null,
        Action<TSeat>? ConfigureFirstSeat = null);
}
