using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Operators.GraphQL;

/// <summary>
/// A tenant as an operator lists it, as the schema shows it: its slug, its name, its status and how many of its
/// seats are active, and nobody in it. Declared over the record the package answers.
/// </summary>
[ObjectType<TenantsTenancy.TenantListing>]
internal static partial class TenantListingType
{
    static partial void Configure(IObjectTypeDescriptor<TenantsTenancy.TenantListing> descriptor) => descriptor.Name("TenantListing");
}
