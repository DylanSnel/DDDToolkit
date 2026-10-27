using HotChocolate;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A seat and what it may do where, as the schema shows it: the seat manager's overview, the record the module's query
/// answers. It holds the tenant, the seat, the path of each unit the seat is placed at, the roles its grants name,
/// every permission it holds now and the moment the overview holds for.
/// </summary>
/// <remarks>
/// The tenant, the seat and the roles are the types every other answer shows them as. The seat's placements are a
/// field of the overview, <c>placements</c>: the overview is the seat's own, and the <c>Seat</c> type shows no
/// placements anywhere else. A grant applies at <c>asOf</c> when it starts at or before it and ends after it, or has
/// no end.
/// </remarks>
[ObjectType<SeatManager.SeatOverview>]
internal static partial class SeatOverviewType
{
    static partial void Configure(IObjectTypeDescriptor<SeatManager.SeatOverview> descriptor)
    {
        descriptor.Name("SeatOverview");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(overview => overview.Tenant);
        descriptor.Field(overview => overview.TenantName);
        descriptor.Field(overview => overview.Seat);
        descriptor.Field(overview => overview.Units);
        descriptor.Field(overview => overview.Roles);
        descriptor.Field(overview => overview.Permissions);
        descriptor.Field(overview => overview.AsOf);
    }

    /// <summary>Where the seat is placed, with the roles it holds at each for which period: the seat's own placements.</summary>
    public static IReadOnlyList<Placement<SeatId, OrganizationUnitId, RoleId>> GetPlacements([Parent] SeatManager.SeatOverview overview)
        => overview.Seat.Placements;
}
