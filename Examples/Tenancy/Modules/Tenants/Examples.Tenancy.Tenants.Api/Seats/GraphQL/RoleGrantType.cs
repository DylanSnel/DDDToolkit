using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A role a seat holds at the unit of one of its placements, for a period, as the schema shows it: the package's entity,
/// with the role by id. The overview's <c>roles</c> gives each role's name.
/// </summary>
[ObjectType<RoleGrant<SeatId, RoleId>>]
internal static partial class RoleGrantType
{
    static partial void Configure(IObjectTypeDescriptor<RoleGrant<SeatId, RoleId>> descriptor)
    {
        descriptor.Name("RoleGrant");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(grant => grant.RoleId);
        descriptor.Field(grant => grant.StartsAt);
        descriptor.Field(grant => grant.EndsAt);
    }
}
