using DDDToolkit.Supporting.Tenancy.Access;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// One of the caller's own seats as the schema shows it: the package's record of a seat in its tenant, which the
/// module's query answers. It holds the seat, as the <c>Seat</c> every other answer shows, beside the slug a request
/// selects the tenant by, the tenant's name and its status.
/// </summary>
[ObjectType<SeatInTenant<Seat>>]
internal static partial class SeatOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<SeatInTenant<Seat>> descriptor) => descriptor.Name("SeatOfMine");
}
