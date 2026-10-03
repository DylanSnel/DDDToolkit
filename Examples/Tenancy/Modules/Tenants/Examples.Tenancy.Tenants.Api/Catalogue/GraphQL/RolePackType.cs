using Examples.Tenancy.Tenants.Application.Catalogue.Queries;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Catalogue.GraphQL;

/// <summary>
/// A pack a tenant's roles are copied from, as the schema shows it: declared over the record the catalogue's
/// query answers.
/// </summary>
[ObjectType<PackOverview>]
internal static partial class RolePackType
{
    static partial void Configure(IObjectTypeDescriptor<PackOverview> descriptor) => descriptor.Name("RolePack");
}
