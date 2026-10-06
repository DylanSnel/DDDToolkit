using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// One of the calling seat's placements as the schema shows it: the unit, whether it is the primary one, and the
/// roles held there. Declared over the record the seat's own overview answers.
/// </summary>
[ObjectType<TenantsTenancy.PlacementSummary>]
internal static partial class PlacementOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<TenantsTenancy.PlacementSummary> descriptor) => descriptor.Name("PlacementOfMine");
}
