using HotChocolate.Types.Relay;

namespace Examples.Tenancy.Inspections.Api.GraphQL;

/// <summary>
/// A project as Inspections knows it: its key, written as the node id Projects gives it. Its number and name are
/// Projects' to give, and the gateway asks Projects for them when a client asks for more than the id.
/// </summary>
/// <remarks>
/// There is no record of the application to declare the type over, since Inspections keeps nothing of a project
/// but its id, so this one stands in for it. What Inspections has to say about a project, its inspections, is a
/// field the feature adds to the type in its own <c>GraphQL</c> folder.
/// </remarks>
/// <param name="Id">The project.</param>
internal sealed record ReferencedProject([property: ID("Project")] ProjectId Id);
