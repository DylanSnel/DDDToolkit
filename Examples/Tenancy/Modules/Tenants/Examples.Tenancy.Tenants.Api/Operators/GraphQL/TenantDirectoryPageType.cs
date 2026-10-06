using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Operators.GraphQL;

/// <summary>A page of the tenants an operator lists, as the schema shows it: declared over the record the package answers.</summary>
[ObjectType<TenantsTenancy.TenantDirectoryPage>]
internal static partial class TenantDirectoryPageType
{
    static partial void Configure(IObjectTypeDescriptor<TenantsTenancy.TenantDirectoryPage> descriptor) => descriptor.Name("TenantDirectoryPage");
}
