using DDDToolkit.Supporting.Tenancy.Access;
using Examples.Tenancy.Tenants.Application.Directory.Queries;
using Examples.Tenancy.Tenants.Application.Roles;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using GreenDonut;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Directory.GraphQL;

/// <summary>
/// The seats, units and roles an answer names, each kind asked of the directory a batch at a time: the ids of one
/// batch are one question. HotChocolate's generator writes a data loader from each method:
/// <c>ISeatByIdDataLoader</c>, <c>IOrganizationUnitNameByIdDataLoader</c> and <c>IRoleByIdDataLoader</c>.
/// </summary>
/// <remarks>
/// A method sends the query the directory's route sends, and nothing else: each query asks one of Tenancy's managers.
/// The generated loader calls it in a scope of services of its own, with the sender of that scope, so the access check
/// runs as for any other caller of the query; the caller is the one the request's flow carries. An id the manager
/// leaves out, of another tenant or of nothing at all, is missing from the answer, and is nothing to whoever asked
/// for it.
/// <para>
/// One question to the directory takes no more than <c>SeatManager.MostIds</c> ids and refuses more, so a
/// wider page of references is asked in parts.
/// </para>
/// </remarks>
internal static class DirectoryDataLoaders
{
    /// <summary>The seats with these ids, by id: the module's own class, as the seat manager read them.</summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<SeatId, Seat>> GetSeatByIdAsync(
        IReadOnlyList<SeatId> ids,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var seats = new Dictionary<SeatId, Seat>();
        foreach (var part in ids.Chunk(SeatManager.MostIds))
        {
            foreach (var seat in await sender.Send(new SeatsById(part), cancellationToken))
            {
                seats[seat.Id] = seat;
            }
        }

        return seats;
    }

    /// <summary>What the units with these ids are called, with their paths and depths, by id: the organization manager's answer.</summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<OrganizationUnitId, NamedUnit<OrganizationUnitId>>> GetOrganizationUnitNameByIdAsync(
        IReadOnlyList<OrganizationUnitId> ids,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var units = new Dictionary<OrganizationUnitId, NamedUnit<OrganizationUnitId>>();
        foreach (var part in ids.Chunk(SeatManager.MostIds))
        {
            foreach (var unit in await sender.Send(new OrganizationUnitsById(part), cancellationToken))
            {
                units[unit.Id] = unit;
            }
        }

        return units;
    }

    /// <summary>The roles with these ids, by id, each with whether it manages access and whether the caller reads its permissions.</summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<RoleId, RoleListing>> GetRoleByIdAsync(
        IReadOnlyList<RoleId> ids,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var roles = new Dictionary<RoleId, RoleListing>();
        foreach (var part in ids.Chunk(SeatManager.MostIds))
        {
            foreach (var listed in await sender.Send(new RolesById(part), cancellationToken))
            {
                roles[listed.Role.Id] = listed;
            }
        }

        return roles;
    }
}
