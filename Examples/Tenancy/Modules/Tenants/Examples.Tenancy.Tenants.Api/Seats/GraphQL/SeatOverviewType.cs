using Examples.Tenancy.Tenants.Application.Seats;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// Who the calling seat is and what it may do where, as the schema shows it: its tenant, the seat with the name this
/// application keeps on it, its placements with the roles held at each, the roles it holds anywhere, and every key it
/// holds now. Declared over the record the package answers, closed over the module's view of a seat, under its plain
/// name: the record is nested in a generic class, and the name HotChocolate would infer for it spells every type
/// argument out.
/// </summary>
[ObjectType<TenantsTenancy.SeatOverview<SeatListing>>]
internal static partial class SeatOverviewType
{
    static partial void Configure(IObjectTypeDescriptor<TenantsTenancy.SeatOverview<SeatListing>> descriptor) => descriptor.Name("SeatOverview");
}
