using Examples.Tenancy.Tenants.Application.Seats;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// One of the calling seat's placements as the schema shows it: the unit, whether it is the primary one, and the
/// roles held there. Declared over the record the seat's overview answers, <see cref="PlacementListing"/>.
/// </summary>
[ObjectType<PlacementListing>]
internal static partial class PlacementOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<PlacementListing> descriptor) => descriptor.Name("PlacementOfMine");
}
