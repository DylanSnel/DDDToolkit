using Acme.Shared;
using DDDToolkit.BaseTypes;

namespace Acme.Billing;

// A domain event is what makes the generator write {Module}EventNames, the class the script reads the
// module name from.
public sealed record InvoiceSent(Guid InvoiceId, Money Total) : DomainEvent;
