using Acme.Press.Manuscripts;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Acme.Press.Tenants;

// The application's organization: a tenant is a publishing house, with its head office at the root and its imprints
// below it, people who have a seat in it, and roles of its own. Each class is Tenancy's, declared with its
// template under the application's own name and id; the toolkit's generator writes the rest of each from the
// package's parent.

/// <summary>A publishing house's id, and its organization's.</summary>
[EntityId<Guid>]
public readonly partial record struct TenantId;

/// <summary>A seat's id: what a member of a manuscript is known by.</summary>
[EntityId<Guid>]
public readonly partial record struct SeatId;

/// <summary>An organization unit's id: where a manuscript sits.</summary>
[EntityId<Guid>]
public readonly partial record struct OrganizationUnitId;

/// <summary>The id of a role a publishing house makes for itself.</summary>
[EntityId<Guid>]
public readonly partial record struct RoleId;

/// <summary>A publishing house.</summary>
[TenantAggregate<TenantId>]
public sealed partial class Tenant;

/// <summary>A publishing house's organization: the house and its imprints, in a tree.</summary>
[OrganizationAggregate<TenantId>]
public sealed partial class Organization;

/// <summary>The head office or one of the imprints.</summary>
[OrganizationUnit<OrganizationUnitId>]
public sealed partial class OrganizationUnit;

/// <summary>The place somebody has in a publishing house.</summary>
[SeatAggregate<SeatId>]
public sealed partial class Seat;

/// <summary>A role a publishing house makes for itself, given at a place in its organization.</summary>
[RoleAggregate<RoleId>]
public sealed partial class Role;

/// <summary>The roles a publishing house starts with.</summary>
public static class PressCatalogue
{
    /// <summary>Heads an imprint: reads and edits its manuscripts.</summary>
    public const string ImprintHeadStarterRole = "imprint-head";

    /// <summary>
    /// What the application passes as <c>TenancyOptions.Catalogue</c>, marked so the export writes Tenancy's policies
    /// from it, with the module's permissions, which <see cref="ManuscriptPermissions"/> states: Tenancy on Postgres
    /// writes them because the infrastructure project references it, and the host gets nothing more to write. It
    /// declares no administrators' starter role, so the catalogue adds Tenancy's own,
    /// <see cref="TenancyStarterRoles.DefaultAdministrators"/>, which holds every permission for the whole house, and the
    /// access file the export writes has it.
    /// </summary>
    [TenancyCatalogue]
    public static ApplicationCatalogue Application { get; } = new(
        StarterRoles:
        [
            new(ImprintHeadStarterRole, "Head of imprint", "Heads an imprint", [ManuscriptPermissions.Read, ManuscriptPermissions.Edit], Order: 20),
        ]);
}
