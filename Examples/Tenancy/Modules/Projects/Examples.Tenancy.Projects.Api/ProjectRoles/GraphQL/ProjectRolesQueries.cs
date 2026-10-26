using Examples.Tenancy.Projects.Application.ProjectRoles;
using Examples.Tenancy.Projects.Application.ProjectRoles.Queries;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Projects.Api.ProjectRoles.GraphQL;

/// <summary>The tenant's project roles: the field the route <c>GET /project-roles</c> answers as well.</summary>
internal static class ProjectRolesQueries
{
    /// <summary>Every project role of the tenant, archived ones too, by name.</summary>
    [Query]
    public static async Task<IReadOnlyList<ProjectRoleListing>> GetProjectRolesAsync([Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new TenantProjectRoles(), cancellationToken);
}
