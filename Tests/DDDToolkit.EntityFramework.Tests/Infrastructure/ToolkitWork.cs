using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A host that requires explicit callers, in miniature: a sending module whose outbox publishes
/// <see cref="ShelfOpenedV3"/> to the modules of the process, and a receiving module that handles it under
/// its inbox, both on Postgres with row level security. The owner's reads that the assertions make go
/// around the interceptor.
/// </summary>
public sealed class ToolkitWork : IAsyncDisposable
{
    /// <summary>The scope a receiving handler's scoped system caller runs in, which the receipts' policies ask for.</summary>
    public const string ReceivingScope = "receiving";

    private readonly ServiceProvider _provider;

    private ToolkitWork(string connectionString, ServiceProvider provider)
    {
        ConnectionString = connectionString;
        _provider = provider;
    }

    public string ConnectionString { get; }

    public IServiceProvider Services => _provider;

    /// <summary>The callers the receiving handler saw, one per run.</summary>
    public SeenCallers Seen => _provider.GetRequiredService<SeenCallers>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// Makes the two modules' tables in <paramref name="connectionString"/>'s database and builds the host.
    /// </summary>
    /// <param name="connectionString">A database of the test's own.</param>
    /// <param name="module">The receiving module's registration: its scopes and its handlers.</param>
    /// <param name="configure">Anything else about the toolkit's options, such as an in-process dispatch.</param>
    /// <param name="services">Anything else about the host.</param>
    /// <param name="sendToModules">Whether the outbox publishes to the modules, or only dispatches in process.</param>
    /// <param name="requireExplicitCallers">Whether the host requires explicit callers.</param>
    public static async Task<ToolkitWork> StartAsync(
        string connectionString,
        Action<ModuleIntegrationEvents<CallerInboxContext>>? module = null,
        Action<DDDEntityFrameworkOptions>? configure = null,
        Action<IServiceCollection>? services = null,
        bool sendToModules = true,
        bool requireExplicitCallers = true)
    {
        await CreateTablesAsync(connectionString);

        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton<SeenCallers>();
        collection.AddDDDToolkitEntityFramework(options =>
        {
            options.MapIntegrationEvents(contracts => contracts.Register<ShelfOpenedV3>());
            options.UseOutbox<CallerOutboxContext>(outbox =>
            {
                outbox.RegisterEvent<ShelfCreated>();
                if (sendToModules)
                {
                    outbox.PublishAs<ShelfCreated, ShelfOpenedV3>(created => new ShelfOpenedV3(created.ShelfId.ToString(), created.Name));
                    outbox.SendToModules();
                }
            });
            configure?.Invoke(options);
        });

        collection.AddPostgresRowLevelSecurity();
        if (requireExplicitCallers)
        {
            collection.RequireExplicitCallers();
        }

        collection.AddDbContext<CallerOutboxContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseDDDToolkit(provider)
            .UsePostgresRowLevelSecurity(provider));
        collection.AddDbContext<CallerInboxContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseDDDToolkit(provider)
            .UsePostgresRowLevelSecurity(provider));

        collection.AddOutboxProcessor<CallerOutboxContext>();
        collection.AddModuleIntegrationEvents<CallerInboxContext>(registration => module?.Invoke(registration));
        services?.Invoke(collection);

        return new ToolkitWork(connectionString, collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }));
    }

    /// <summary>A shelf opened, as a transport hands it to <see cref="IntegrationEventReceiver"/>.</summary>
    public static IntegrationEventMessage Opened(string name = "Fiction", Guid? id = null) => new()
    {
        MessageId = id ?? Guid.CreateVersion7(),
        Name = "library.shelf-opened",
        Version = 3,
        Payload = JsonSerializer.Serialize(new ShelfOpenedV3("SHELF_1", name)),
        OccurredAt = DateTimeOffset.UtcNow,
        AggregateType = "Shelf",
        AggregateId = "SHELF_1",
    };

    /// <summary>
    /// A shelf opened, as the sending module's outbox would have written it, written as the owner; delivered
    /// at <paramref name="processedAt"/> already, or waiting.
    /// </summary>
    public async Task<Guid> QueueAsync(string name = "Fiction", DateTimeOffset? processedAt = null)
    {
        var outbox = _provider.GetRequiredService<DDDEntityFrameworkOptions>().OutboxFor(typeof(CallerOutboxContext))!;
        var created = new ShelfCreated(ShelfId.CreateUnique(), name);

        await using var owner = Owner<CallerOutboxContext>(options => new CallerOutboxContext(options));
        owner.Outbox.Add(new OutboxMessage
        {
            Id = created.EventId,
            EventName = "shelf.created",
            Version = 1,
            Payload = JsonSerializer.Serialize(created, created.GetType(), outbox.JsonOptions),
            OccurredAt = created.OccurredAt,
            CreatedAt = processedAt ?? DateTimeOffset.UtcNow,
            ProcessedAt = processedAt,
            Attempts = processedAt is null ? 0 : 1,
            AggregateType = "Shelf",
            AggregateId = created.ShelfId.ToString(),
        });
        await owner.SaveChangesAsync(Cancellation);

        return created.EventId;
    }

    /// <summary>What the outbox processor does on one tick, in a scope of its own, and with no caller begun around it.</summary>
    public async Task<int> ProcessAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor<CallerOutboxContext>>().ProcessPendingAsync(cancellationToken: Cancellation);
    }

    /// <summary>The outbox's rows, read as the owner.</summary>
    public async Task<List<OutboxMessage>> OutboxAsync()
    {
        await using var owner = Owner<CallerOutboxContext>(options => new CallerOutboxContext(options));
        return await owner.Outbox.AsNoTracking().ToListAsync(Cancellation);
    }

    /// <summary>The sending module's labels, read as the owner.</summary>
    public async Task<List<string>> LabelsAsync()
    {
        await using var owner = Owner<CallerOutboxContext>(options => new CallerOutboxContext(options));
        return await owner.Labels.Select(label => label.Text).ToListAsync(Cancellation);
    }

    /// <summary>The inbox's rows, read as the owner, with who wrote each and under which claims.</summary>
    public Task<List<Written>> InboxAsync() => WrittenAsync("""SELECT "Consumer", "WrittenBy", coalesce("WrittenClaims", '') FROM receiver."InboxMessages" ORDER BY "ProcessedAt" """);

    /// <summary>The rows the receiving handler wrote, read as the owner, with who wrote each and under which claims.</summary>
    public Task<List<Written>> ReceiptsAsync() => WrittenAsync("""SELECT "Note", "WrittenBy", coalesce("WrittenClaims", '') FROM receiver."Receipts" ORDER BY "Note" """);

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    private async Task<List<Written>> WrittenAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);

        var rows = new List<Written>();
        while (await reader.ReadAsync(Cancellation))
        {
            rows.Add(new Written(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    private TContext Owner<TContext>(Func<DbContextOptions<TContext>, TContext> create) where TContext : DbContext
        => create(new DbContextOptionsBuilder<TContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>
    /// Both modules' tables, as the owner. Who wrote a row of the inbox and of the receipts is kept in columns
    /// the model does not know, filled by the database, so the assertions can ask it. The receipts are the
    /// scoped system role's to write only in the receiving scope.
    /// </summary>
    private static async Task CreateTablesAsync(string connectionString)
    {
        await using (var sender = new CallerOutboxContext(new DbContextOptionsBuilder<CallerOutboxContext>().UseNpgsql(connectionString).Options))
        {
            await sender.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(Cancellation);
        }

        await using (var receiver = new CallerInboxContext(new DbContextOptionsBuilder<CallerInboxContext>().UseNpgsql(connectionString).Options))
        {
            await receiver.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(Cancellation);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand($"""
            ALTER TABLE receiver."InboxMessages"
                ADD COLUMN "WrittenBy" text NOT NULL DEFAULT current_user,
                ADD COLUMN "WrittenClaims" text DEFAULT current_setting('request.jwt.claims', true);
            ALTER TABLE receiver."Receipts"
                ADD COLUMN "WrittenBy" text NOT NULL DEFAULT current_user,
                ADD COLUMN "WrittenClaims" text DEFAULT current_setting('request.jwt.claims', true);

            GRANT USAGE ON SCHEMA receiver TO {ExplicitCallersPostgres.SystemInRole};
            GRANT SELECT, INSERT ON receiver."InboxMessages", receiver."Receipts" TO {ExplicitCallersPostgres.SystemInRole};

            ALTER TABLE receiver."Receipts" ENABLE ROW LEVEL SECURITY;
            CREATE POLICY "Receipts are the receiving scope's to write" ON receiver."Receipts"
                FOR INSERT TO {ExplicitCallersPostgres.SystemInRole}
                WITH CHECK ((current_setting('request.jwt.claims', true)::jsonb ->> 'scope') = '{ReceivingScope}');
            CREATE POLICY "Receipts are the receiving scope's to read" ON receiver."Receipts"
                FOR SELECT TO {ExplicitCallersPostgres.SystemInRole}
                USING ((current_setting('request.jwt.claims', true)::jsonb ->> 'scope') = '{ReceivingScope}');
            """, connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }
}

/// <summary>A row, who wrote it, and the claims it was written under.</summary>
public sealed record Written(string Key, string By, string Claims);

/// <summary>The callers a handler saw: <see cref="Callers.Ambient"/> at the moment it ran.</summary>
public sealed class SeenCallers
{
    private readonly List<Caller?> _callers = [];

    public IReadOnlyList<Caller?> Callers => _callers;

    public void Add(Caller? caller)
    {
        lock (_callers)
        {
            _callers.Add(caller);
        }
    }
}

/// <summary>The sending module: its outbox, and labels of its own, in a schema of its own.</summary>
public class CallerOutboxContext(DbContextOptions<CallerOutboxContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public DbSet<Label> Labels => Set<Label>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddDomainEventOutbox(Database, schema: "sender");
        modelBuilder.Entity<Label>().ToTable("Labels", "sender");
    }
}

/// <summary>A row of the sending module's own, which an in-process handler may write through the outbox's context.</summary>
public class Label
{
    public Guid Id { get; set; }

    public string Text { get; set; } = "";
}

/// <summary>The receiving module: its inbox, and the receipts its handler writes.</summary>
public class CallerInboxContext(DbContextOptions<CallerInboxContext> options) : DbContext(options)
{
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    public DbSet<Receipt> Receipts => Set<Receipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddDomainEventInbox(Database, schema: "receiver");
        modelBuilder.Entity<Receipt>().ToTable("Receipts", "receiver");
    }
}

/// <summary>What the receiving handler writes for each shelf it hears of.</summary>
public class Receipt
{
    public Guid Id { get; set; }

    public string Note { get; set; } = "";
}

/// <summary>
/// The receiving module's handler: it records who it ran as, and writes a receipt through the module's own
/// context, in the inbox's transaction.
/// </summary>
[IntegrationEventConsumer("callers.receipt-log")]
public sealed class ReceiptLog(CallerInboxContext context, SeenCallers seen) : IIntegrationEventHandler<ShelfOpenedV3>
{
    public Task HandleAsync(ShelfOpenedV3 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        seen.Add(Callers.Ambient);
        context.Receipts.Add(new Receipt { Id = Guid.CreateVersion7(), Note = contract.DisplayName });
        return Task.CompletedTask;
    }
}

/// <summary>The same handler without writing anything: for a caller that has no right to the receipts.</summary>
[IntegrationEventConsumer("callers.receipt-watch")]
public sealed class ReceiptWatch(SeenCallers seen) : IIntegrationEventHandler<ShelfOpenedV3>
{
    public Task HandleAsync(ShelfOpenedV3 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
    {
        seen.Add(Callers.Ambient);
        return Task.CompletedTask;
    }
}
