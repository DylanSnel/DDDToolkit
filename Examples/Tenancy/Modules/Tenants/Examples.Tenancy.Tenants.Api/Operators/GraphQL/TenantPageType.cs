using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Operators.GraphQL;

/// <summary>
/// A page of the tenants an operator lists, as the schema shows it: the tenant manager's page, with the tenants of
/// the page and the marker of the next one.
/// </summary>
[ObjectType<TenantManager.TenantPage>]
internal static partial class TenantPageType
{
    static partial void Configure(IObjectTypeDescriptor<TenantManager.TenantPage> descriptor) => descriptor.Name("TenantPage");
}
