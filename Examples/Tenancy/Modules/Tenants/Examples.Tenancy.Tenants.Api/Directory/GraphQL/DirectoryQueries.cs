using Examples.Tenancy.Tenants.Application.Organization;
using Examples.Tenancy.Tenants.Application.Roles;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types.Composite;

namespace Examples.Tenancy.Tenants.Api.Directory.GraphQL;

/// <summary>
/// How the gateway finds what another module named by its id: a seat, a unit or a role, by its key.
/// </summary>
/// <remarks>
/// The three lookups are internal: they are there for the gateway, which resolves a reference such as a crew
/// member's seat through them, and the composed schema has no such field. A client reaches a name through the
/// reference, or through Tenancy's lists. Each goes through a data loader of <see cref="DirectoryDataLoaders"/>,
/// so the seats of one batch, as a rule all a page of projects names, are asked in one question, and each
/// answers nothing for an id of another tenant or of nothing at all, which a client cannot tell apart.
/// </remarks>
internal static class DirectoryQueries
{
    /// <summary>
    /// What a field that loads through a data loader weighs for each id: a lookup here, and a field of a row that
    /// names a seat, a unit or a role, as an invitation and a row of the access history do. HotChocolate weighs a
    /// field with a resolver that reads as if every id read on its own; these read once for the ids of a batch,
    /// as a rule all of a request, so an id weighs little.
    /// </summary>
    internal const double LoadedForTheRequest = 1;

    /// <summary>A seat by its id, or nothing.</summary>
    [Query]
    [Lookup]
    [Internal]
    [Cost(LoadedForTheRequest)]
    public static async Task<SampleTenancy.SeatSummary?> GetSeatAsync(SeatId id, ISeatByIdDataLoader seats, CancellationToken cancellationToken)
        => await seats.LoadAsync(id, cancellationToken);

    /// <summary>A unit by its id, or nothing.</summary>
    [Query]
    [Lookup]
    [Internal]
    [Cost(LoadedForTheRequest)]
    public static async Task<UnitListing?> GetOrganizationUnitAsync(OrganizationUnitId id, IOrganizationUnitByIdDataLoader units, CancellationToken cancellationToken)
        => await units.LoadAsync(id, cancellationToken);

    /// <summary>A role by its id, or nothing.</summary>
    [Query]
    [Lookup]
    [Internal]
    [Cost(LoadedForTheRequest)]
    public static async Task<RoleListing?> GetRoleAsync(RoleId id, IRoleByIdDataLoader roles, CancellationToken cancellationToken)
        => await roles.LoadAsync(id, cancellationToken);
}
