using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A role the calling seat holds somewhere, with the keys it brings, as the schema shows it: declared over the
/// record the seat's own overview answers. It is no <c>Role</c>: that is an entity other modules refer to, with
/// a rule on its keys, and what a seat holds itself it may always read.
/// </summary>
[ObjectType<TenancyUseCases.RoleSummary>]
internal static partial class RoleOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<TenancyUseCases.RoleSummary> descriptor) => descriptor.Name("RoleOfMine");
}
