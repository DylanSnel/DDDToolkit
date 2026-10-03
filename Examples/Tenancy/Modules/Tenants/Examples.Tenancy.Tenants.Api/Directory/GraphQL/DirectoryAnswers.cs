using Examples.Tenancy.Tenants.Application.Directory.Queries;
using Examples.Tenancy.Tenants.Application.Roles;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Directory.GraphQL;

/// <summary>
/// What a mutation answers after its command: the seat, the unit or the role it changed, as the directory
/// answers it now, or nothing when the caller may not read it.
/// </summary>
/// <remarks>
/// Each sends the directory's query on its own, never through a data loader: a loader remembers what it answered
/// earlier in the request, which is what the seat was before the command.
/// </remarks>
internal static class DirectoryAnswers
{
    /// <summary>The seat as the directory answers it now.</summary>
    public static async Task<SampleTenancy.SeatSummary?> SeatNowAsync(this ISender sender, SeatId id, CancellationToken cancellationToken)
        => (await sender.Send(new SeatsById([id]), cancellationToken)).FirstOrDefault();

    /// <summary>The unit as the directory answers it now.</summary>
    public static async Task<SampleTenancy.UnitSummary?> UnitNowAsync(this ISender sender, OrganizationUnitId id, CancellationToken cancellationToken)
        => (await sender.Send(new OrganizationUnitsById([id]), cancellationToken)).FirstOrDefault();

    /// <summary>The role as the directory answers it now, with what the tenant uses it for.</summary>
    public static async Task<RoleListing?> RoleNowAsync(this ISender sender, RoleId id, CancellationToken cancellationToken)
        => (await sender.Send(new RolesById([id]), cancellationToken)).FirstOrDefault();
}
