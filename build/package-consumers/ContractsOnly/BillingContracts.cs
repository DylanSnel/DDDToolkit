using DDDToolkit.Abstractions.Attributes;

namespace Acme.Billing.Contracts;

// What a contracts project publishes, and all of it compiles against DDDToolkit.Abstractions alone:
// an identifier, a read model and an integration event. The event is what makes the generator write
// {Module}EventNames, the class the script reads the module name from. None of them is marked
// [ModuleContract]: DDD_ModuleContracts in the project file publishes every public type here.

[EntityId<Guid>("INV")]
public readonly partial record struct InvoiceId;

public sealed record InvoiceSummary(InvoiceId Id, decimal Total);

[IntegrationEvent]
public sealed record InvoiceSent(InvoiceId InvoiceId);
