using Acme.Press.Manuscripts;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
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

/// <summary>The roles a publishing house starts with, and the keys of its manuscripts a role of the organization can hold.</summary>
public static class PressCatalogue
{
    /// <summary>Heads an imprint: reads and edits its manuscripts.</summary>
    public const string ImprintHeadPack = "imprint-head";

    /// <summary>The keys of the manuscripts that a role of the organization can hold.</summary>
    public static IReadOnlyList<Permission> Permissions { get; } =
    [
        new(ManuscriptKeys.Read, "Manuscripts", "Read manuscripts", Order: 10),
        new(ManuscriptKeys.Edit, "Manuscripts", "Edit a manuscript", Order: 20),
    ];

    /// <summary>
    /// What the application passes as <c>TenancyOptions.Catalogue</c>. It declares no administrators' pack, so the
    /// catalogue adds Tenancy's own, <see cref="TenancyPacks.DefaultAdministrators"/>, which holds every key for
    /// the whole house, and the access file the export writes from <see cref="Built"/> has it.
    /// </summary>
    public static ApplicationCatalogue Application { get; } = new(
        Packs:
        [
            new(ImprintHeadPack, "Head of imprint", "Heads an imprint", [ManuscriptKeys.Read, ManuscriptKeys.Edit], Order: 20),
        ]);

    /// <summary>The catalogue as the application runs with it.</summary>
    public static TenancyCatalogue Built { get; } = TenancyCatalogue.Build(Application, Permissions);
}

/// <summary>The logical names of Tenancy's functions, which a manuscript's rules name so its own functions ask them.</summary>
public static class TenancyFunctions
{
    /// <summary>Answers the calling seat.</summary>
    public const string CallerSeat = TenancyRowAccess.Owner + "/caller_seat";

    /// <summary>Answers, for a key, the units the calling seat's hold of it reaches.</summary>
    public const string UnitsWhereIHold = TenancyRowAccess.Owner + "/units_where_i_hold";
}
