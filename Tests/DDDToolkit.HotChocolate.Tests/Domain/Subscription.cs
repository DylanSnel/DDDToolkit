using DDDToolkit.Abstractions.Attributes;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;

namespace DDDToolkit.HotChocolate.Tests.Domain;

/// <summary>
/// The published contract a client may subscribe to. Primitives only: it crosses a socket to a browser,
/// so it is a schema type first and an internal record never.
/// </summary>
[IntegrationEvent("library.ticket-issued", Version = 2)]
public sealed record TicketIssuedContract(Guid TicketId, string Holder, int Seat);

/// <summary>A second version of the same contract, so the map's keying on version can be shown.</summary>
[IntegrationEvent("library.ticket-issued", Version = 1)]
public sealed record TicketIssuedContractV1(Guid TicketId);

/// <summary>
/// The subscription root. It exists to prove the claim the documentation makes: what a client receives
/// is a schema type, declared here, resolved through the schema, and not whatever object the producing
/// module happened to be holding.
/// </summary>
public sealed class Subscription
{
    /// <summary>The topic the sink is configured to send to.</summary>
    public const string Topic = "ticketIssued";

    [Subscribe(With = nameof(OnTicketIssuedAsync))]
    public TicketIssuedContract TicketIssued([EventMessage] TicketIssuedContract contract) => contract;

    public ValueTask<ISourceStream<TicketIssuedContract>> OnTicketIssuedAsync(
        [Service] ITopicEventReceiver receiver,
        CancellationToken cancellationToken)
        => receiver.SubscribeAsync<TicketIssuedContract>(Topic, cancellationToken);
}
