using Examples.Tenancy.Tenants.Application.Catalogue.Queries;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Catalogue.GraphQL;

/// <summary>
/// A starter role a tenant's roles are copied from, as the schema shows it: declared over the record the catalogue's
/// query answers.
/// </summary>
[ObjectType<StarterRoleOverview>]
internal static partial class StarterRoleType
{
    static partial void Configure(IObjectTypeDescriptor<StarterRoleOverview> descriptor) => descriptor.Name("StarterRole");
}
