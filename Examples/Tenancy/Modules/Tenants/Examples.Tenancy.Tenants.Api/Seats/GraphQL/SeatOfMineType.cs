using Examples.Tenancy.Tenants.Application.Seats;
using HotChocolate;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// One of the caller's own seats as the schema shows it: the tenant it is in, and the seat.
/// </summary>
/// <remarks>
/// Declared over the record the module's query answers, <see cref="SeatOfMine"/>: what the package found, and the seat
/// as the <c>Seat</c> every other answer shows, with the name the module keeps for the person in that tenant. What
/// the package found is no field: its tenant's values are put together as <see cref="TenantOfSeat"/>, and its seat's
/// are the <c>Seat</c>'s already.
/// </remarks>
[ObjectType<SeatOfMine>]
internal static partial class SeatOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<SeatOfMine> descriptor)
    {
        descriptor.Name("SeatOfMine");
        descriptor.Ignore(mine => mine.Found);
    }

    /// <summary>The tenant, with the slug a request selects it by.</summary>
    public static TenantOfSeat GetTenant([Parent] SeatOfMine mine)
        => new(mine.Found.Tenant, mine.Found.Slug, mine.Found.OrganizationName, mine.Found.TenantStatus);
}
