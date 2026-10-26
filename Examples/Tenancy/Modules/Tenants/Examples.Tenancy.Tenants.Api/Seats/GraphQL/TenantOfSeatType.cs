using Examples.Tenancy.Tenants.Application.Seats;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// The tenant one of the caller's seats is in, as the list a tenant is picked from shows it: declared over the record
/// the module's query answers, <see cref="TenantOfSeat"/>, with the slug a request selects it by.
/// </summary>
[ObjectType<TenantOfSeat>]
internal static partial class TenantOfSeatType
{
    static partial void Configure(IObjectTypeDescriptor<TenantOfSeat> descriptor) => descriptor.Name("TenantOfSeat");
}
