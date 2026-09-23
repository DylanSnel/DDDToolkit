using DDDToolkit.BaseTypes;
using DDDToolkit.Messaging.Wolverine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace DDDToolkit.Messaging.Tests;

/// <summary>
/// Wolverine as the transport: <see cref="WolverineSink"/> publishes, a Wolverine local queue carries the
/// envelope, <see cref="IntegrationEventEnvelopeHandler"/> hands it to the module's inbox. The local queue
/// stands in for RabbitMQ; Wolverine's routing and error handling are the real ones.
/// </summary>
public sealed class WolverineTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static async Task<IHost> StartAsync(ShelfCounter counter)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLibraryModule(counter);
        builder.UseWolverine(wolverine =>
        {
            wolverine.PublishMessage<IntegrationEventEnvelope>().ToLocalQueue("integration-events");
            wolverine.ReceiveIntegrationEvents(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
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
        var counter = new ShelfCounter();
        using var host = await StartAsync(counter);

        await SendAsync(host, Receiving.Message(displayName: "Fiction"));

        await counter.WaitForAsync(1);
        counter.Seen.Should().Equal("Fiction");
    }

    [Fact]
    public async Task The_same_message_published_twice_is_applied_once()
    {
        var counter = new ShelfCounter();
        using var host = await StartAsync(counter);
        var id = Guid.CreateVersion7();

        await SendAsync(host, Receiving.Message(id));
        await SendAsync(host, Receiving.Message(id));
        await SendAsync(host, Receiving.Message(displayName: "Poetry"));

        await counter.WaitForAsync(2);
        await Task.Delay(300, Cancellation);
        counter.Seen.Should().BeEquivalentTo(["Fiction", "Poetry"], "the inbox knows it applied that message id");
    }

    [Fact]
    public async Task A_message_whose_handler_fails_is_retried_by_Wolverine_until_it_is_applied()
    {
        var counter = new ShelfCounter().FailFirst(2);
        using var host = await StartAsync(counter);

        await SendAsync(host, Receiving.Message());

        await counter.WaitForAsync(1);
        counter.Seen.Should().ContainSingle();
    }
}
