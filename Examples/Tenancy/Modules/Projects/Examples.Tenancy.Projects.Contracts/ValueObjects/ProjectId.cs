using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Projects.Contracts.ValueObjects;

/// <summary>A project's id.</summary>
/// <remarks>
/// Published because Inspections stores one on every inspection and the HTTP API parses it from the route. The
/// crew member's id and a crew role grant's are not: nothing outside a project names one.
/// </remarks>
[ModuleContract]
[EntityId<Guid>("PRJ")]
public readonly partial record struct ProjectId;
