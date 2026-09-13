using System.Text.Json;
using DDDToolkit.BaseTypes;
using DDDToolkit.ExampleLibrary.GraphQl;
using DDDToolkit.HotChocolate.Subscriptions;
using DDDToolkit.HotChocolate.Tests.Domain;
using DDDToolkit.HotChocolate.Tests.GraphQl;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// Pushing a published contract to the clients connected right now.
/// <para>
/// The tests run against HotChocolate's real in-memory subscription transport rather than a stand-in
/// sender, because the thing worth proving is that a message published by the outbox arrives at a client
/// that asked for it, as the schema type the schema declares.
/// </para>
/// </summary>
public sealed class SubscriptionSinkTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly TicketIssuedContract Contract = new(TestData.TicketGuid, "ada@example.com", 42);

    private static ServiceProvider BuildProvider(Action<GraphQlSubscriptionMap>? map = null)
    {
        var services = new ServiceCollection();

        services
            .AddGraphQL()
            .AddDDDToolkitTypes()
            .AddCommonGraphQlRuntimeBindings()
            .AddGraphQlTestsGraphQlRuntimeBindings()
            .AddInMemorySubscriptions()
            .AddQueryType<Query>()
            .AddSubscriptionType<Subscription>();

        services.AddIntegrationEventSubscriptions(configure => (map ?? (m => m.Publish<TicketIssuedContract>(Subscription.Topic)))(configure));

        return services.BuildServiceProvider();
    }

    private static IntegrationEventMessage Message(object? body = null, string name = "library.ticket-issued", int version = 2, string? aggregateId = null)
        => new()
        {
            MessageId = TestData.EventGuid,
            Name = name,
            Version = version,
            Payload = JsonSerializer.Serialize(Contract),
            OccurredAt = TestData.OccurredAt,
            AggregateType = "Ticket",
            AggregateId = aggregateId ?? TestData.TicketGuid.ToString(),
            Body = body,
        };

    [Fact]
    public async Task A_published_contract_reaches_a_client_that_subscribed_to_its_topic()
    {
        await using var provider = BuildProvider();
        var receiver = provider.GetRequiredService<ITopicEventReceiver>();
        var sink = provider.GetRequiredService<GraphQlSubscriptionSink>();

        // Subscribe first: nothing is stored, so a payload sent before this line would simply be gone.
        var stream = await receiver.SubscribeAsync<TicketIssuedContract>(Subscription.Topic, Cancellation);

        await sink.SendAsync(Message(body: Contract), Cancellation);

        var received = await FirstAsync(stream);
        received.Should().Be(Contract);
    }

    [Fact]
    public async Task The_subscription_field_publishes_the_contract_as_a_schema_type()
    {
        await using var provider = BuildProvider();
        var executor = await provider.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync(cancellationToken: Cancellation);

        var schema = executor.Schema.ToString();

        schema.Should().Contain("type Subscription").And.Contain("ticketIssued: TicketIssuedContract!");
        schema.Should().Contain("type TicketIssuedContract")
            .And.Contain("holder: String!")
            .And.Contain("seat: Int!");
    }

    [Fact]
    public async Task A_subscribing_client_receives_what_the_outbox_published()
    {
        await using var provider = BuildProvider();
        var executor = await provider.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync(cancellationToken: Cancellation);
        var sink = provider.GetRequiredService<GraphQlSubscriptionSink>();

        var result = await executor.ExecuteAsync("subscription { ticketIssued { ticketId holder seat } }", Cancellation);
        var stream = result.Should().BeAssignableTo<IResponseStream>().Subject;

        var reader = ReadFirstAsync(stream);
        await sink.SendAsync(Message(body: Contract), Cancellation);
        var json = await reader;

        var ticket = json.GetProperty("data").GetProperty("ticketIssued");
        ticket.GetProperty("holder").GetString().Should().Be("ada@example.com");
        ticket.GetProperty("seat").GetInt32().Should().Be(42);
        ticket.GetProperty("ticketId").GetString().Should().Be(TestData.TicketGuid.ToString());
    }

    [Fact]
    public async Task An_envelope_with_no_body_is_read_from_its_payload()
    {
        await using var provider = BuildProvider();
        var receiver = provider.GetRequiredService<ITopicEventReceiver>();
        var sink = provider.GetRequiredService<GraphQlSubscriptionSink>();
        var stream = await receiver.SubscribeAsync<TicketIssuedContract>(Subscription.Topic, Cancellation);

        // What an envelope rebuilt from a transport looks like: text, and nothing else.
        await sink.SendAsync(Message(body: null), Cancellation);

        (await FirstAsync(stream)).Should().Be(Contract);
    }

    [Fact]
    public async Task A_contract_the_map_does_not_name_reaches_nobody()
    {
        await using var provider = BuildProvider();
        var receiver = provider.GetRequiredService<ITopicEventReceiver>();
        var sink = provider.GetRequiredService<GraphQlSubscriptionSink>();
        var stream = await receiver.SubscribeAsync<TicketIssuedContract>(Subscription.Topic, Cancellation);

        // Same name, older version: a subscription is a schema, and last year's shape is not in it.
        await sink.SendAsync(Message(body: null, version: 1), Cancellation);
        await sink.SendAsync(Message(body: null, name: "library.something-else"), Cancellation);
        await sink.SendAsync(Message(body: Contract), Cancellation);

        (await FirstAsync(stream)).Should().Be(Contract, "only the mapped contract was pushed");
    }

    [Fact]
    public async Task A_topic_built_from_the_message_scopes_the_subscription_to_one_aggregate()
    {
        await using var provider = BuildProvider(map => map.Publish<TicketIssuedContract>(message => $"ticket:{message.AggregateId}"));
        var receiver = provider.GetRequiredService<ITopicEventReceiver>();
        var sink = provider.GetRequiredService<GraphQlSubscriptionSink>();

        var mine = await receiver.SubscribeAsync<TicketIssuedContract>($"ticket:{TestData.TicketGuid}", Cancellation);
        var theirs = await receiver.SubscribeAsync<TicketIssuedContract>("ticket:someone-else", Cancellation);

        await sink.SendAsync(Message(body: Contract), Cancellation);

        (await FirstAsync(mine)).Should().Be(Contract);
        await theirs.DisposeAsync();
    }

    [Fact]
    public async Task A_topic_factory_returning_null_drops_that_occurrence()
    {
        await using var provider = BuildProvider(map => map
            .Publish<TicketIssuedContract>(message => message.AggregateId == "quiet" ? null : Subscription.Topic));
        var receiver = provider.GetRequiredService<ITopicEventReceiver>();
        var sink = provider.GetRequiredService<GraphQlSubscriptionSink>();
        var stream = await receiver.SubscribeAsync<TicketIssuedContract>(Subscription.Topic, Cancellation);

        await sink.SendAsync(Message(body: Contract, aggregateId: "quiet"), Cancellation);
        await sink.SendAsync(Message(body: Contract), Cancellation);

        (await FirstAsync(stream)).Should().Be(Contract);
    }

    [Fact]
    public void A_contract_cannot_be_published_to_two_topics()
    {
        var map = new GraphQlSubscriptionMap().Publish<TicketIssuedContract>("a");

        var twice = () => map.Publish<TicketIssuedContract>("b");

        twice.Should().Throw<ArgumentException>().WithMessage("*library.ticket-issued*version 2*");
    }

    [Fact]
    public void The_map_keys_on_the_published_name_and_version()
    {
        var map = new GraphQlSubscriptionMap()
            .Publish<TicketIssuedContract>("current")
            .Publish<TicketIssuedContractV1>("legacy");

        map.Published.Should().BeEquivalentTo([("library.ticket-issued", 2), ("library.ticket-issued", 1)]);

        map.TryGetTopic(Message(version: 1), out var type, out var topic).Should().BeTrue();
        type.Should().Be<TicketIssuedContractV1>();
        topic.Should().Be("legacy");
    }

    private static async Task<TicketIssuedContract> FirstAsync(ISourceStream<TicketIssuedContract> stream)
    {
        await using (stream)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            await foreach (var message in stream.ReadEventsAsync().WithCancellation(timeout.Token))
            {
                return message;
            }
        }

        throw new InvalidOperationException("The stream closed without carrying a message.");
    }

    private static async Task<JsonElement> ReadFirstAsync(IResponseStream stream)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        await foreach (var result in stream.ReadResultsAsync().WithCancellation(timeout.Token))
        {
            using var document = JsonDocument.Parse(result.ToJson());
            return document.RootElement.Clone();
        }

        throw new InvalidOperationException("The response stream closed without carrying a result.");
    }
}
