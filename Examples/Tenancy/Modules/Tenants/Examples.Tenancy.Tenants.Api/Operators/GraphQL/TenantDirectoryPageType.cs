using Examples.Tenancy.Tenants.Application.Operators;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Operators.GraphQL;

/// <summary>A page of the tenants an operator lists, as the schema shows it: declared over the record the package answers.</summary>
[ObjectType<ListedTenants>]
internal static partial class TenantDirectoryPageType
{
    static partial void Configure(IObjectTypeDescriptor<ListedTenants> descriptor) => descriptor.Name("TenantDirectoryPage");
}
