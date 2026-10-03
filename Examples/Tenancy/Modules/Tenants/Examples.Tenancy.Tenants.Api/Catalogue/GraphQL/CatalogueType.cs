using Examples.Tenancy.Tenants.Application.Catalogue.Queries;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Catalogue.GraphQL;

/// <summary>
/// What the application offers a tenant, as the schema shows it: every permission key, the packs roles are copied
/// from, and the kinds of unit. Declared over the record the catalogue's query answers.
/// </summary>
[ObjectType<CatalogueOverview>]
internal static partial class CatalogueType
{
    static partial void Configure(IObjectTypeDescriptor<CatalogueOverview> descriptor) => descriptor.Name("Catalogue");
}
