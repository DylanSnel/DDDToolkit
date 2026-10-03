using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Tenants.Contracts.ValueObjects;

/// <summary>An invitation's id: the offer of a seat that somebody may still accept.</summary>
[ModuleContract]
[EntityId<Guid>("INV")]
public readonly partial record struct InvitationId;
