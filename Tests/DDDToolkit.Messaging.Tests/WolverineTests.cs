using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework;
using DDDToolkit.Messaging.Wolverine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace DDDToolkit.Messaging.Tests;

/// <summary>
/// Wolverine as the transport: <see cref="WolverineSink"/> publishes the contract as a message type of its
/// own, a Wolverine local queue carries it, <see cref="IntegrationEventHandler{TContract}"/> hands it to the
/// module's inbox. The local queue stands in for RabbitMQ; Wolverine's routing, handler discovery and error
/// handling are the real ones.
/// </summary>
public sealed class WolverineTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static async Task<IHost> StartAsync(Failures? failures = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLibraryModule(failures);
        builder.UseWolverine(wolverine =>
        {
            wolverine.PublishMessage<ShelfOpenedV1>().ToLocalQueue("integration-events");
            wolverine.ReceiveIntegrationEvents(builder.Services.IntegrationEventSubscriptions(), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        });

        var host = builder.Build();
        await host.Services.CreateLibraryDatabaseAsync();
        await host.StartAsync(Cancellation);
        return host;
    }

    private static Task SendAsync(IHost host, IntegrationEventMessage message)
    {
        using var scope = host.Services.CreateScope();
        return ActivatorUtilities.CreateInstance<WolverineSink>(scope.ServiceProvider).SendAsync(message, Cancellation);
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

        // The two copies may run at once. One wins the inbox row; the other's save fails on it, rolls back
        // its own row with it, and is retried into "already applied".
        await host.Services.WaitForShelvesAsync(2);
        await Task.Delay(1000, Cancellation);
        (await host.Services.ShelvesAsync()).Should().BeEquivalentTo(["Fiction", "Poetry"], "the inbox lets one copy of a message id through");
    }

    [Fact]
    public async Task A_message_whose_handler_fails_is_retried_by_Wolverine_until_it_is_applied()
    {
        using var host = await StartAsync(new Failures(2));

        await SendAsync(host, Receiving.Message());

        (await host.Services.WaitForShelvesAsync(1)).Should().ContainSingle();
    }
}
