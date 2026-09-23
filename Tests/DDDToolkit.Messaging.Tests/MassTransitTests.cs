using DDDToolkit.BaseTypes;
using DDDToolkit.Messaging.MassTransit;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Messaging.Tests;

/// <summary>
/// MassTransit as the transport: <see cref="MassTransitSink"/> publishes, MassTransit's in-memory transport
/// carries the envelope, <see cref="IntegrationEventEnvelopeConsumer"/> hands it to the module's inbox. The
/// in-memory transport stands in for RabbitMQ; the consumer pipeline and the retry are MassTransit's own.
/// </summary>
public sealed class MassTransitTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static async Task<IHost> StartAsync(Failures? failures = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLibraryModule(failures);
        builder.Services.AddMassTransit(bus =>
        {
            bus.AddIntegrationEventConsumer();
            bus.AddConfigureEndpointsCallback((_, _, endpoint) => endpoint.UseMessageRetry(retry => retry.Intervals(50, 50, 50)));
            bus.UsingInMemory((context, memory) => memory.ConfigureEndpoints(context));
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
}
