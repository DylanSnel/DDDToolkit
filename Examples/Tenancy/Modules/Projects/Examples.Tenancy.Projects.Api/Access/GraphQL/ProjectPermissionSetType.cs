using Examples.Tenancy.Projects.Application.Access.Queries;
using HotChocolate;
using HotChocolate.Types;
using HotChocolate.Types.Relay;

namespace Examples.Tenancy.Projects.Api.Access.GraphQL;

/// <summary>
/// What the caller holds on a single project, of the permissions it asked about: the GraphQL type <c>ProjectPermissionSet</c>,
/// declared over the answer the application's query gives. The answer of all of them,
/// <see cref="ProjectPermissionSets"/>, needs no class: its properties are its fields.
/// </summary>
[ObjectType<ProjectPermissionSet>]
internal static partial class ProjectPermissionSetType
{
    /// <summary>The project, as its node id.</summary>
    [ID("Project")]
    public static ProjectId GetProject([Parent] ProjectPermissionSet held) => held.Project;
}
