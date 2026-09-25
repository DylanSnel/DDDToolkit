using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Storage;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace DDDToolkit.EntityFramework.Migrations;

/// <summary>
/// Creates and drops the outbox and inbox tables from a hand-written migration, for teams that do not
/// scaffold. If you do scaffold, you need none of this: the tables are part of your model as soon as
/// <c>AddDomainEventOutbox()</c> or <c>AddDomainEventInbox()</c> is in <c>OnModelCreating</c>, so
/// <c>dotnet ef migrations add</c> already writes them.
/// <code>
/// public partial class AddMessaging : Migration
/// {
///     protected override void Up(MigrationBuilder migrationBuilder)
///     {
///         migrationBuilder.CreateDomainEventOutbox();
///         migrationBuilder.CreateDomainEventInbox();
///     }
///
///     protected override void Down(MigrationBuilder migrationBuilder)
///     {
///         migrationBuilder.DropDomainEventInbox();
///         migrationBuilder.DropDomainEventOutbox();
///     }
/// }
/// </code>
/// <para>
/// The shapes here are the same ones the model builder extensions configure, so a scaffolded migration
/// and a hand-written one produce the same table. Pass the same <c>tableName</c> and <c>schema</c> you
/// passed there.
/// </para>
/// <para>
/// On a provider without schemas the schema is dropped rather than guessed at. SQLite is the one that
/// matters in practice: <c>EnsureSchema</c> means nothing to it, and its identifiers carry no schema,
/// so these methods leave the schema off entirely when they see the SQLite provider.
/// </para>
/// <para>
/// The timestamp columns follow the same rule the model does. <c>MigrationBuilder.ActiveProvider</c>
/// names the provider, so <see cref="DomainEventTimestamps.ProviderDefault"/> means the same thing
/// here as it does in <c>AddDomainEventOutbox</c>. Pass whatever you passed there; if you passed
/// nothing, pass nothing.
/// </para>
/// </summary>
public static class DomainEventMigrationBuilderExtensions
{
    /// <summary>
    /// Creates the outbox table, with its primary key and the index on <c>ProcessedAt</c> the processor
    /// queries by. Creates the schema first when the provider has schemas.
    /// </summary>
    /// <param name="migrationBuilder">The migration being written.</param>
    /// <param name="tableName">The table to create.</param>
    /// <param name="schema">The schema to create it in, or <see langword="null"/> for the provider's default.</param>
    /// <param name="timestamps">What the timestamp columns become; pass what you passed to <c>AddDomainEventOutbox</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is empty or white space.</exception>
    public static OperationBuilder<CreateTableOperation> CreateDomainEventOutbox(
        this MigrationBuilder migrationBuilder,
        string tableName = DomainEventStorage.DefaultOutboxTableName,
        string? schema = DomainEventStorage.DefaultSchema,
        DomainEventTimestamps timestamps = DomainEventTimestamps.ProviderDefault)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var utcDateTime = DomainEventTimestampMapping.StoresUtcDateTime(migrationBuilder.ActiveProvider, timestamps);
        schema = EnsureSchema(migrationBuilder, schema);

        var table = migrationBuilder.CreateTable(
            name: tableName,
            schema: schema,
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                EventName = table.Column<string>(maxLength: DomainEventStorage.MaxNameLength, nullable: false),
                Payload = table.Column<string>(nullable: false),
                // Rows written before this column existed are version 1, which is what a payload with no
                // [IntegrationEvent(Version = n)] is, so the default makes an upgrade a no-op.
                Version = table.Column<int>(nullable: false, defaultValue: 1),
                OccurredAt = table.TimestampColumn(utcDateTime),
                AggregateType = table.Column<string>(maxLength: DomainEventStorage.MaxAggregateTypeLength, nullable: true),
                AggregateId = table.Column<string>(maxLength: DomainEventStorage.MaxAggregateIdLength, nullable: true),
                CreatedAt = table.TimestampColumn(utcDateTime),
                ProcessedAt = table.TimestampColumn(utcDateTime, nullable: true),
                Attempts = table.Column<int>(nullable: false),
                NextAttemptAt = table.TimestampColumn(utcDateTime, nullable: true),
                LastError = table.Column<string>(maxLength: DomainEventStorage.MaxErrorLength, nullable: true),
            },
            constraints: table => table.PrimaryKey($"PK_{tableName}", x => x.Id));

        migrationBuilder.CreateIndex(
            name: $"IX_{tableName}_{nameof(OutboxMessage.ProcessedAt)}",
            table: tableName,
            schema: schema,
            column: nameof(OutboxMessage.ProcessedAt));

        return table;
    }

    /// <summary>Drops the outbox table. The index goes with it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is empty or white space.</exception>
    public static OperationBuilder<DropTableOperation> DropDomainEventOutbox(
        this MigrationBuilder migrationBuilder,
        string tableName = DomainEventStorage.DefaultOutboxTableName,
        string? schema = DomainEventStorage.DefaultSchema)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        return migrationBuilder.DropTable(tableName, SchemaFor(migrationBuilder, schema));
    }

    /// <summary>
    /// Creates the inbox table, keyed on (<c>MessageId</c>, <c>Consumer</c>). Creates the schema first
    /// when the provider has schemas.
    /// </summary>
    /// <param name="migrationBuilder">The migration being written.</param>
    /// <param name="tableName">The table to create.</param>
    /// <param name="schema">The schema to create it in, or <see langword="null"/> for the provider's default.</param>
    /// <param name="timestamps">What the <c>ProcessedAt</c> column becomes; pass what you passed to <c>AddDomainEventInbox</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is empty or white space.</exception>
    public static OperationBuilder<CreateTableOperation> CreateDomainEventInbox(
        this MigrationBuilder migrationBuilder,
        string tableName = DomainEventStorage.DefaultInboxTableName,
        string? schema = DomainEventStorage.DefaultSchema,
        DomainEventTimestamps timestamps = DomainEventTimestamps.ProviderDefault)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var utcDateTime = DomainEventTimestampMapping.StoresUtcDateTime(migrationBuilder.ActiveProvider, timestamps);
        schema = EnsureSchema(migrationBuilder, schema);

        var table = migrationBuilder.CreateTable(
            name: tableName,
            schema: schema,
            columns: table => new
            {
                MessageId = table.Column<Guid>(nullable: false),
                Consumer = table.Column<string>(maxLength: DomainEventStorage.MaxConsumerLength, nullable: false),
                MessageName = table.Column<string>(maxLength: DomainEventStorage.MaxNameLength, nullable: true),
                ProcessedAt = table.TimestampColumn(utcDateTime),
            },
            constraints: table => table.PrimaryKey($"PK_{tableName}", x => new { x.MessageId, x.Consumer }));

        migrationBuilder.CreateIndex(
            name: $"IX_{tableName}_{nameof(InboxMessage.ProcessedAt)}",
            table: tableName,
            schema: schema,
            column: nameof(InboxMessage.ProcessedAt));

        return table;
    }

    /// <summary>Drops the inbox table. The index goes with it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is empty or white space.</exception>
    public static OperationBuilder<DropTableOperation> DropDomainEventInbox(
        this MigrationBuilder migrationBuilder,
        string tableName = DomainEventStorage.DefaultInboxTableName,
        string? schema = DomainEventStorage.DefaultSchema)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        return migrationBuilder.DropTable(tableName, SchemaFor(migrationBuilder, schema));
    }

    /// <summary>
    /// The schema to use on this provider: the one asked for, or <see langword="null"/> where the
    /// provider has no schemas.
    /// </summary>
    private static string? SchemaFor(MigrationBuilder migrationBuilder, string? schema)
        => SupportsSchemas(migrationBuilder) ? schema : null;

    private static string? EnsureSchema(MigrationBuilder migrationBuilder, string? schema)
    {
        schema = SchemaFor(migrationBuilder, schema);

        if (schema is not null)
        {
            migrationBuilder.EnsureSchema(schema);
        }

        return schema;
    }

    private static bool SupportsSchemas(MigrationBuilder migrationBuilder)
        => !DomainEventTimestampMapping.IsSqlite(migrationBuilder.ActiveProvider);
}
