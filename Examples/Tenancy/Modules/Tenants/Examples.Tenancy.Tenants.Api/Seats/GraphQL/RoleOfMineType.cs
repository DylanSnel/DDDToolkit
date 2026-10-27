using Examples.Tenancy.Tenants.Application.Seats;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A role the calling seat holds somewhere, with the permissions it brings, as the schema shows it: declared over the
/// record the seat's own overview answers. It is no <c>Role</c>: that is an entity other modules refer to, with
/// a rule on its permissions, and what a seat holds itself it may always read.
/// </summary>
[ObjectType<RoleOfMine>]
internal static partial class RoleOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<RoleOfMine> descriptor) => descriptor.Name("RoleOfMine");
}
