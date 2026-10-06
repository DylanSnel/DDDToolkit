using Examples.Tenancy.Tenants.Application.Seats;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// One of the caller's own seats as the schema shows it: the tenant it is in, and the seat as the <c>Seat</c> every
/// other answer shows, with the name the module keeps for the person in that tenant. Declared over the record the
/// module's query answers, <see cref="SeatOfMine"/>.
/// </summary>
[ObjectType<SeatOfMine>]
internal static partial class SeatOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<SeatOfMine> descriptor) => descriptor.Name("SeatOfMine");
}
