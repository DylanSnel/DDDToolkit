using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects.ValueObjects;

/// <summary>The id of a seat's row on a project's crew: what its table is keyed by, beside the project.</summary>
/// <remarks>
/// Declared rather than left to the entity generator, because the crew member is declared with Membership's
/// member template, which takes its id as a type of its own, and the package's member list makes a new row's id
/// with its <c>Create()</c>, which the generator writes for it. Nothing outside a project names one, so it is not
/// published.
/// </remarks>
[EntityId<Guid>("CREW")]
public readonly partial record struct CrewMemberId;
