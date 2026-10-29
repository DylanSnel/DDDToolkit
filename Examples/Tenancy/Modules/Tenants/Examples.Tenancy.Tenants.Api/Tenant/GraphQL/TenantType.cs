using HotChocolate;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Tenant.GraphQL;

/// <summary>
/// A tenant as the schema shows it: the module's own tenant class, the one Tenancy's managers answer, with its slug,
/// its shape and its status.
/// </summary>
/// <remarks>
/// The type is the class itself, so nothing is copied. Its fields are listed one by one, and the slug is its text: the
/// class holds more, such as why it was suspended, and the schema shows what this type chose. The tenant's name is its
/// organization's, so the records that answer a tenant with its name hold the name beside it: the seat's overview, an
/// operator's listing and the caller's own seats.
/// <para>
/// Inside this project <c>Tenant</c> is a feature's namespace, so the class is named from the domain project's.
/// </para>
/// </remarks>
[ObjectType<Domain.Aggregates.Tenants.Tenant>]
internal static partial class TenantType
{
    static partial void Configure(IObjectTypeDescriptor<Domain.Aggregates.Tenants.Tenant> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(tenant => tenant.Id);
        descriptor.Field(tenant => tenant.Shape);
        descriptor.Field(tenant => tenant.Status);
    }

    /// <summary>What a request names the tenant by, in the <c>Tenant</c> header.</summary>
    public static string GetSlug([Parent] Domain.Aggregates.Tenants.Tenant tenant) => tenant.Slug.Value;
}
