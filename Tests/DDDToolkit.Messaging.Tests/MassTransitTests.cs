using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework;
using DDDToolkit.Messaging.MassTransit;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Messaging.Tests;

/// <summary>
/// MassTransit as the transport: <see cref="MassTransitSink"/> publishes the contract as a message type of
/// its own, MassTransit's in-memory transport carries it, <see cref="IntegrationEventConsumer{TContract}"/>
/// hands it to the module's inbox. The in-memory transport stands in for RabbitMQ; the topology, the
/// consumer pipeline and the retry are MassTransit's own.
/// </summary>
public sealed class MassTransitTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static async Task<IHost> StartAsync(Failures? failures = null, bool integrationEventNames = false)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLibraryModule(failures);
        builder.Services.AddMassTransit(bus =>
        {
            bus.AddIntegrationEventConsumers(builder.Services.IntegrationEventSubscriptions());
            bus.AddConfigureEndpointsCallback((_, _, endpoint) => endpoint.UseMessageRetry(retry => retry.Intervals(50, 50, 50)));
            bus.UsingInMemory((context, memory) =>
            {
                if (integrationEventNames)
                {
                    memory.UseIntegrationEventNames();
                }

                memory.ConfigureEndpoints(context);
            });
        });

        var host = builder.Build();
        await host.Services.CreateLibraryDatabaseAsync();
        await host.StartAsync(Cancellation);
        return host;
    }

    private static async Task SendAsync(IHost host, IntegrationEventMessage message)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await ActivatorUtilities.CreateInstance<MassTransitSink>(scope.ServiceProvider).SendAsync(message, Cancellation);
    }

    [Fact]
    public async Task A_published_message_reaches_the_module_that_consumes_its_contract()
    {
        using var host = await StartAsync();

        await SendAsync(host, Receiving.Message(displayName: "Fiction"));

        (await host.Services.WaitForShelvesAsync(1)).Should().Equal("Fiction");
    }

    [Fact]
    public async Task The_same_message_published_twice_is_applied_once()
    {
        using var host = await StartAsync();
        var id = Guid.CreateVersion7();

        await SendAsync(host, Receiving.Message(id));
        await SendAsync(host, Receiving.Message(id));
        await SendAsync(host, Receiving.Message(displayName: "Poetry"));

        await host.Services.WaitForShelvesAsync(2);
        await Task.Delay(1000, Cancellation);
        (await host.Services.ShelvesAsync()).Should().BeEquivalentTo(["Fiction", "Poetry"], "the inbox lets one copy of a message id through");
    }

    [Fact]
    public async Task A_message_whose_handler_fails_is_retried_by_MassTransit_until_it_is_applied()
    {
        using var host = await StartAsync(new Failures(2));

        await SendAsync(host, Receiving.Message());

        (await host.Services.WaitForShelvesAsync(1)).Should().ContainSingle();
    }

    [Fact]
    public async Task With_the_toolkit_names_a_contract_has_the_exchange_of_its_name_and_version_and_still_arrives()
    {
        using var host = await StartAsync(integrationEventNames: true);

        host.Services.GetRequiredService<IBus>().Topology.Message<ShelfOpenedV1>().EntityName
            .Should().Be("library.shelf-opened.v1", "the published name and version, not the CLR type");

        await SendAsync(host, Receiving.Message(displayName: "Fiction"));

        (await host.Services.WaitForShelvesAsync(1)).Should().Equal(["Fiction"], "the consumer's queue is bound through the same formatter");
    }

    [Fact]
    public void The_toolkit_names_only_its_own_events_and_leaves_every_other_message_to_MassTransit()
    {
        var formatter = new IntegrationEventEntityNameFormatter(new FixedName("mass-transit's own"));

        formatter.FormatEntityName<ShelfOpenedV1>().Should().Be("library.shelf-opened.v1");
        formatter.FormatEntityName<OpenedShelf>().Should().Be("mass-transit's own");
    }

    private sealed class FixedName(string name) : IEntityNameFormatter
    {
        public string FormatEntityName<T>() => name;
    }
}
