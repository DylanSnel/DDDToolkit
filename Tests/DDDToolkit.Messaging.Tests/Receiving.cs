using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Messaging.Tests;

/// <summary>The contract the tests publish.</summary>
[IntegrationEvent("library.shelf-opened", Version = 1)]
public sealed record ShelfOpenedV1(string ShelfId, string DisplayName);

/// <summary>A module's context: nothing but the inbox the handlers run under.</summary>
public sealed class LibraryContext(DbContextOptions<LibraryContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddDomainEventInbox(Database);
}

/// <summary>The consuming module's handler: counts what it applied, and fails when told to.</summary>
[IntegrationEventConsumer("library.shelf-counter")]
public sealed class ShelfCounter : IIntegrationEventHandler<ShelfOpenedV1>
{
    private int _failuresLeft;

    public List<string> Seen { get; } = [];

    public ShelfCounter FailFirst(int times)
    {
        _failuresLeft = times;
        return this;
    }

    public Task HandleAsync(ShelfOpenedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        lock (Seen)
        {
            if (_failuresLeft-- > 0)
            {
                throw new InvalidOperationException("Not yet.");
            }

            Seen.Add(contract.DisplayName);
        }

        return Task.CompletedTask;
    }

    /// <summary>Waits until <paramref name="count"/> messages were applied, or fails the test.</summary>
    public async Task WaitForAsync(int count, TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            lock (Seen)
            {
                if (Seen.Count >= count)
                {
                    return;
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"{count} message(s) were expected; {Seen.Count} arrived.");
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}

/// <summary>What every broker test sets up the same way: one consuming module with an inbox, on SQLite.</summary>
public static class Receiving
{
    public static IntegrationEventMessage Message(Guid? id = null, string displayName = "Fiction") => new()
    {
        MessageId = id ?? Guid.CreateVersion7(),
        Name = "library.shelf-opened",
        Version = 1,
        Payload = System.Text.Json.JsonSerializer.Serialize(new ShelfOpenedV1("SHELF_1", displayName)),
        OccurredAt = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero),
        AggregateType = "Shelf",
        AggregateId = "SHELF_1",
    };

    /// <summary>
    /// Registers a module whose handler is <paramref name="counter"/>, on a SQLite file of its own, and
    /// returns the file so the caller can create it once the provider is built.
    /// </summary>
    public static IServiceCollection AddLibraryModule(this IServiceCollection services, ShelfCounter counter)
    {
        var file = Path.Combine(Path.GetTempPath(), $"dddtoolkit-messaging-{Guid.NewGuid():N}.db");

        services.AddDDDToolkitEntityFramework(options => options.MapIntegrationEvents(contracts => contracts.Register<ShelfOpenedV1>()));
        services.AddDbContext<LibraryContext>((provider, options) => options.UseSqlite($"Data Source={file}").UseDDDToolkit(provider));
        services.AddModuleIntegrationEvents<LibraryContext>(module => module.Handle<ShelfOpenedV1>(counter));
        return services;
    }

    public static async Task CreateLibraryDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LibraryContext>().Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }
}
