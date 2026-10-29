using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Operators.GraphQL;

/// <summary>
/// A tenant as an operator lists it, as the schema shows it: the tenant manager's record, with the tenant, the name of
/// its organization and how many of its seats are active, and nobody in it.
/// </summary>
/// <remarks>
/// The name and the count are not on the tenant: the tenant manager reads them beside it, for the whole page at once.
/// So the record holds them, and the schema shows the record as it is.
/// </remarks>
[ObjectType<TenantManager.TenantListing>]
internal static partial class TenantListingType
{
    static partial void Configure(IObjectTypeDescriptor<TenantManager.TenantListing> descriptor) => descriptor.Name("TenantListing");
}
