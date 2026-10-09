using Examples.Tenancy.Tenants.Application.Seats;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A seat and what it may do where, as the schema shows it: its tenant, the seat with the name this application keeps
/// on it, its placements with the roles held at each, the roles it holds anywhere, and every key it holds now.
/// Declared over the record the module's query selects from the package's overview, <see cref="SeatOverviewListing"/>.
/// </summary>
[ObjectType<SeatOverviewListing>]
internal static partial class SeatOverviewType
{
    static partial void Configure(IObjectTypeDescriptor<SeatOverviewListing> descriptor) => descriptor.Name("SeatOverview");
}
