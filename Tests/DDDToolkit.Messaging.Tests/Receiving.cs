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

/// <summary>A row the handler writes, in the same save as the inbox row.</summary>
public sealed class OpenedShelf
{
    public int Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>A module's context: the inbox the handlers run under, and the table they write.</summary>
public sealed class LibraryContext(DbContextOptions<LibraryContext> options) : DbContext(options)
{
    public DbSet<OpenedShelf> OpenedShelves => Set<OpenedShelf>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddDomainEventInbox(Database);
}

/// <summary>How often the handler is to fail before it starts to succeed.</summary>
public sealed class Failures
{
    private int _left;

    public Failures(int left = 0) => _left = left;

    public bool Next() => Interlocked.Decrement(ref _left) >= 0;
}

/// <summary>
/// The consuming module's handler. It writes a row and does not save: the inbox saves the row together
/// with the one that says the message was applied, so the row is exactly-once whatever the transport
/// does. Anything a handler does outside its module's context has no such guarantee, which is why the
/// tests count rows rather than calls.
/// </summary>
[IntegrationEventConsumer("library.shelf-recorder")]
public sealed class ShelfRecorder(LibraryContext context, Failures failures) : IIntegrationEventHandler<ShelfOpenedV1>
{
    public Task HandleAsync(ShelfOpenedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        if (failures.Next())
        {
            throw new InvalidOperationException("Not yet.");
        }

        context.OpenedShelves.Add(new OpenedShelf { DisplayName = contract.DisplayName });
        return Task.CompletedTask;
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
        // What the outbox hands a sink: the contract itself, which a broker that routes by type publishes.
        Body = new ShelfOpenedV1("SHELF_1", displayName),
        OccurredAt = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero),
        AggregateType = "Shelf",
        AggregateId = "SHELF_1",
    };

    /// <summary>Registers the consuming module, on a SQLite file of its own.</summary>
    public static IServiceCollection AddLibraryModule(this IServiceCollection services, Failures? failures = null)
    {
        var file = Path.Combine(Path.GetTempPath(), $"dddtoolkit-messaging-{Guid.NewGuid():N}.db");

        services.AddSingleton(failures ?? new Failures());
        services.AddDDDToolkitEntityFramework(options => options.MapIntegrationEvents(contracts => contracts.Register<ShelfOpenedV1>()));
        services.AddDbContext<LibraryContext>((provider, options) => options.UseSqlite($"Data Source={file}").UseDDDToolkit(provider));
        services.AddModuleIntegrationEvents<LibraryContext>(module => module.Handle<ShelfOpenedV1, ShelfRecorder>());
        return services;
    }

    public static async Task CreateLibraryDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LibraryContext>().Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The shelves applied so far.</summary>
    public static async Task<List<string>> ShelvesAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryContext>().OpenedShelves
            .Select(shelf => shelf.DisplayName)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Waits until <paramref name="count"/> shelves were applied, or fails the test.</summary>
    public static async Task<List<string>> WaitForShelvesAsync(this IServiceProvider services, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (true)
        {
            var shelves = await services.ShelvesAsync();
            if (shelves.Count >= count)
            {
                return shelves;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"{count} shelves were expected; {shelves.Count} were applied.");
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
