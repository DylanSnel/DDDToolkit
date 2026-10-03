using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;

/// <summary>A project role's id: what a crew member holds a role by.</summary>
/// <remarks>
/// A project role is this module's own, so its id is too: the routes and the GraphQL fields of this module name
/// it, and no other module does.
/// </remarks>
[EntityId<Guid>("PROL")]
public readonly partial record struct ProjectRoleId;
