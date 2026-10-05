using Examples.Tenancy.Tenants.Application.Directory.Queries;
using Examples.Tenancy.Tenants.Application.Organization;
using Examples.Tenancy.Tenants.Application.Roles;
using GreenDonut;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Directory.GraphQL;

/// <summary>
/// The seats, units and roles an answer names, each kind asked of the directory a batch at a time: the ids of one
/// batch are one question. HotChocolate's generator writes a data loader from each method:
/// <c>ISeatByIdDataLoader</c>, <c>IOrganizationUnitByIdDataLoader</c> and <c>IRoleByIdDataLoader</c>.
/// </summary>
/// <remarks>
/// A method sends the query the directory's route sends, and nothing else. The generated loader calls it in a
/// scope of services of its own, with the sender of that scope, so the access check runs as for any other caller
/// of the query; the caller is the one the request's flow carries. An id the directory leaves out, of another
/// tenant or of nothing at all, is missing from the answer, and is nothing to whoever asked for it.
/// <para>
/// One question to the directory takes no more than <c>TenancyDirectory.MostIds</c> ids and refuses more, so a
/// wider page of references is asked in parts.
/// </para>
/// </remarks>
internal static class DirectoryDataLoaders
{
    /// <summary>The seats with these ids, by id.</summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<SeatId, SampleTenancy.SeatSummary>> GetSeatByIdAsync(
        IReadOnlyList<SeatId> ids,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var seats = new Dictionary<SeatId, SampleTenancy.SeatSummary>();
        foreach (var part in ids.Chunk(SampleTenancy.TenancyDirectory.MostIds))
        {
            foreach (var seat in await sender.Send(new SeatsById(part), cancellationToken))
            {
                seats[seat.Id] = seat;
            }
        }

        return seats;
    }

    /// <summary>The units with these ids, by id, each with its kind.</summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<OrganizationUnitId, UnitListing>> GetOrganizationUnitByIdAsync(
        IReadOnlyList<OrganizationUnitId> ids,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var units = new Dictionary<OrganizationUnitId, UnitListing>();
        foreach (var part in ids.Chunk(SampleTenancy.TenancyDirectory.MostIds))
        {
            foreach (var unit in await sender.Send(new OrganizationUnitsById(part), cancellationToken))
            {
                units[unit.Id] = unit;
            }
        }

        return units;
    }

    /// <summary>The roles with these ids, by id, each with what the tenant uses it for.</summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<RoleId, RoleListing>> GetRoleByIdAsync(
        IReadOnlyList<RoleId> ids,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var roles = new Dictionary<RoleId, RoleListing>();
        foreach (var part in ids.Chunk(SampleTenancy.TenancyDirectory.MostIds))
        {
            foreach (var listed in await sender.Send(new RolesById(part), cancellationToken))
            {
                roles[listed.Id] = listed;
            }
        }

        return roles;
    }
}
