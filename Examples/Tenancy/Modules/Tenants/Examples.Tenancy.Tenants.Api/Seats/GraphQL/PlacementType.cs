using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// One of a seat's placements as the schema shows it: the package's entity of the seat, with the unit by id, whether it
/// is the primary placement, and the roles held there. The overview's <c>units</c> gives each unit's path.
/// </summary>
[ObjectType<Placement<SeatId, OrganizationUnitId, RoleId>>]
internal static partial class PlacementType
{
    static partial void Configure(IObjectTypeDescriptor<Placement<SeatId, OrganizationUnitId, RoleId>> descriptor)
    {
        descriptor.Name("Placement");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(placement => placement.UnitId);
        descriptor.Field(placement => placement.IsPrimary);
        descriptor.Field(placement => placement.Grants);
    }
}
