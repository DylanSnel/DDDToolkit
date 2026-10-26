using Examples.Tenancy.Projects.Application.Access.Queries;
using HotChocolate;
using HotChocolate.Types;
using HotChocolate.Types.Relay;

namespace Examples.Tenancy.Projects.Api.Access.GraphQL;

/// <summary>
/// What the caller holds on a single project, of the keys it asked about: the GraphQL type <c>ProjectKeySet</c>,
/// declared over the answer the application's query gives. The answer of all of them,
/// <see cref="ProjectKeySets"/>, needs no class: its properties are its fields.
/// </summary>
[ObjectType<ProjectKeySet>]
internal static partial class ProjectKeySetType
{
    /// <summary>The project, as its node id.</summary>
    [ID("Project")]
    public static ProjectId GetProject([Parent] ProjectKeySet held) => held.Project;
}
