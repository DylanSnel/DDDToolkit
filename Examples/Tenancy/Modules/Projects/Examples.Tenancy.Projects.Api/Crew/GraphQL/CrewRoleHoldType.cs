using Examples.Tenancy.Projects.Api.Overview.GraphQL;
using Examples.Tenancy.Projects.Api.ProjectRoles.GraphQL;
using Examples.Tenancy.Projects.Application.Crew;
using Examples.Tenancy.Projects.Application.ProjectRoles;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Examples.Tenancy.Projects.Api.Crew.GraphQL;

/// <summary>
/// A project role a crew member holds on the crew, for a period: the GraphQL type <c>CrewRoleHold</c>, declared
/// over the answer the application's queries give.
/// </summary>
[ObjectType<CrewRoleOverview>]
internal static partial class CrewRoleHoldType
{
    static partial void Configure(IObjectTypeDescriptor<CrewRoleOverview> descriptor) => descriptor.Name("CrewRoleHold");

    /// <summary>
    /// The project role held: this schema's own, read for every hold of an answer at once when anything of it but
    /// its id is asked. Nothing for a role that is no longer the tenant's to read.
    /// </summary>
    [Cost(ProjectType.LoadedForThePage)]
    [BindMember(nameof(CrewRoleOverview.RoleId))]
    public static async Task<ProjectRoleListing?> GetRoleAsync([Parent] CrewRoleOverview held, IResolverContext context, IProjectRoleByIdDataLoader roles, CancellationToken cancellationToken)
        => (await ProjectRolesDataLoaders.NamedAsync(context, [held.RoleId], roles, cancellationToken)).SingleOrDefault();
}
