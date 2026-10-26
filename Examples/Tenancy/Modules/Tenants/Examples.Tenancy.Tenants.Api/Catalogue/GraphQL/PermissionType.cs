using Examples.Tenancy.Tenants.Application.Catalogue.Queries;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Catalogue.GraphQL;

/// <summary>
/// A permission key of the catalogue, as the schema shows it: declared over the record the catalogue's query
/// answers.
/// </summary>
[ObjectType<PermissionOverview>]
internal static partial class PermissionType
{
    static partial void Configure(IObjectTypeDescriptor<PermissionOverview> descriptor) => descriptor.Name("Permission");
}
