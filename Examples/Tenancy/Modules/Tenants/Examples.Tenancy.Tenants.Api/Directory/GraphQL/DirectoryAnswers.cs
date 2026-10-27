using Examples.Tenancy.Tenants.Application.Directory.Queries;
using Examples.Tenancy.Tenants.Application.Organization.Queries;
using Examples.Tenancy.Tenants.Domain;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Directory.GraphQL;

/// <summary>
/// What a mutation answers after its command: the seat, the unit or the role it changed, as Tenancy's managers answer
/// it now, or nothing when the caller may not read it.
/// </summary>
/// <remarks>
/// Each sends the directory's query on its own, never through a data loader: a loader remembers what it answered
/// earlier in the request, which is what the seat was before the command.
/// </remarks>
internal static class DirectoryAnswers
{
    /// <summary>The seat as the seat manager answers it now.</summary>
    public static async Task<Seat?> SeatNowAsync(this ISender sender, SeatId id, CancellationToken cancellationToken)
        => (await sender.Send(new SeatsById([id]), cancellationToken)).FirstOrDefault();

    /// <summary>
    /// The unit as the organization manager lists it now: among the units the caller reads, which a unit it manages is,
    /// since it manages units where it is placed.
    /// </summary>
    public static async Task<OrganizationUnit?> UnitNowAsync(this ISender sender, OrganizationUnitId id, CancellationToken cancellationToken)
        => (await sender.Send(new OrganizationUnits(), cancellationToken)).FirstOrDefault(found => found.Unit.Id == id)?.Unit;

    /// <summary>The role as the role manager answers it now.</summary>
    public static async Task<Role?> RoleNowAsync(this ISender sender, RoleId id, CancellationToken cancellationToken)
        => (await sender.Send(new RolesById([id]), cancellationToken)).FirstOrDefault()?.Role;
}
