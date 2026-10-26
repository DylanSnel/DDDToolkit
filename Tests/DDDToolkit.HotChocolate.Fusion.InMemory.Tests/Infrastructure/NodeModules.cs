using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;

// Two modules for Relay: Projects serves a node type, with node(id) as a lookup beside its own plain one, and
// Inspections has no nodes of its own and names a project by its key.

/// <summary>Registers the two modules.</summary>
internal static class NodeModules
{
    public const string Projects = "projects";

    public const string Inspections = "inspections";

    /// <summary>A source schema with one node type: global object identification, and node(id) marked as a lookup.</summary>
    public static IRequestExecutorBuilder AddProjects(this IServiceCollection services)
        => services
            .AddGraphQLServer(Projects)
            .AddSourceSchemaDefaults()
            .AddGlobalObjectIdentification(options => options.MarkNodeFieldAsLookup = true)
            .AddQueryType()
            .AddTypeExtension<ProjectLookups>();

    /// <summary>A source schema without a node type, which names the other schema's node by its id.</summary>
    /// <param name="nodeIds">
    /// Whether this schema writes and reads node ids: global object identification without the Node interface
    /// and without a node field of its own.
    /// </param>
    public static IRequestExecutorBuilder AddInspections(this IServiceCollection services, bool nodeIds)
    {
        var graphql = services
            .AddGraphQLServer(Inspections)
            .AddSourceSchemaDefaults()
            .AddQueryType()
            .AddTypeExtension<InspectionQueries>();

        return nodeIds ? graphql.AddGlobalObjectIdentification(registerNodeInterface: false) : graphql;
    }
}

/// <summary>A project: the one node type.</summary>
[Node]
[GraphQLName("Project")]
public sealed record ProjectNode(int Id, string Name)
{
    private static readonly Dictionary<int, string> Names = new() { [1] = "Bridge", [2] = "Tunnel" };

    /// <summary>What node(id) answers a project by: nothing for one that is not there.</summary>
    [NodeResolver]
    public static ProjectNode? Find(int id) => Names.TryGetValue(id, out var name) ? new ProjectNode(id, name) : null;
}

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class ProjectLookups
{
    /// <summary>The plain lookup, beside the node field.</summary>
    [Lookup]
    public ProjectNode? GetProject([ID<ProjectNode>] int id) => ProjectNode.Find(id);
}

/// <summary>An inspection, which names its project.</summary>
[GraphQLName("Inspection")]
public sealed record InspectionRow(int Id, ReferencedProject? Project);

/// <summary>A project as Inspections knows it: its key, written as the node id Projects gives it.</summary>
[GraphQLName("Project")]
[EntityKey("id")]
public sealed record ReferencedProject([property: ID("Project")] int Id);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class InspectionQueries
{
    public InspectionRow[] GetInspections() => [new(10, new ReferencedProject(1)), new(11, new ReferencedProject(2))];
}
