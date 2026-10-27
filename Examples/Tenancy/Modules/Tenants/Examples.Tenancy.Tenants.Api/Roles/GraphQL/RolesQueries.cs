using Examples.Tenancy.Tenants.Application.Roles.Queries;
using Examples.Tenancy.Tenants.Domain;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Roles.GraphQL;

/// <summary>What is asked about the tenant's roles. A field sends the query its route sends, and nothing else.</summary>
internal static class RolesQueries
{
    /// <summary>
    /// The tenant's roles, the active ones first. A list, not pages: the role manager answers a tenant's roles whole.
    /// The query answers each role with whether the caller reads its permissions, which the route writes by; here the
    /// role's type asks the permission on its own field.
    /// </summary>
    [Query]
    public static async Task<IReadOnlyList<Role>> GetRolesAsync([Service] ISender sender, CancellationToken cancellationToken)
        => [.. (await sender.Send(new TenantRoles(), cancellationToken)).Select(listed => listed.Role)];
}
