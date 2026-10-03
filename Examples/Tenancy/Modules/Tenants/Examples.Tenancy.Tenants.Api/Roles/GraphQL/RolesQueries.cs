using Examples.Tenancy.Tenants.Application.Roles;
using Examples.Tenancy.Tenants.Application.Roles.Queries;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Roles.GraphQL;

/// <summary>What is asked about the tenant's roles. A field sends the query its route sends, and nothing else.</summary>
internal static class RolesQueries
{
    /// <summary>
    /// The tenant's roles, the active ones first, each with what it is used for. A list, not pages: the package's
    /// directory answers a tenant's roles whole.
    /// </summary>
    [Query]
    public static async Task<IReadOnlyList<RoleListing>> GetRolesAsync([Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new TenantRoles(), cancellationToken);
}
