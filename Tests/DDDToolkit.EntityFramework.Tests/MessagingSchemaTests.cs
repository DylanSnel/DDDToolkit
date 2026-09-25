using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Migrations;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Where the outbox and inbox tables live, and the hand-written migration path for teams that do not
/// scaffold.
/// </summary>
public sealed class MessagingSchemaTests : IDisposable
{
    private const string SqlServer = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string Sqlite = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string Postgres = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private readonly SqliteDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Both_tables_default_to_the_ddd_schema()
    {
        using var context = _db.CreateLibraryContext();

        var outbox = context.Model.FindEntityType(typeof(OutboxMessage))!;
        outbox.GetTableName().Should().Be("OutboxMessages");
        outbox.GetSchema().Should().Be(DomainEventStorage.DefaultSchema).And.Be("ddd");

        var inbox = context.Model.FindEntityType(typeof(InboxMessage))!;
        inbox.GetTableName().Should().Be("InboxMessages");
        inbox.GetSchema().Should().Be("ddd");
    }

    [Fact]
    public void The_table_name_and_the_schema_can_both_be_overridden()
    {
        using var context = new RenamedStorageContext(_db.Options<RenamedStorageContext>());

        var outbox = context.Model.FindEntityType(typeof(OutboxMessage))!;
        outbox.GetTableName().Should().Be("EventsOut");
        outbox.GetSchema().Should().Be("messaging");

        var inbox = context.Model.FindEntityType(typeof(InboxMessage))!;
        inbox.GetTableName().Should().Be("EventsIn");
        inbox.GetSchema().Should().BeNull("null means the provider's own default schema");
    }

    [Fact]
    public void Sqlite_has_no_schemas_so_the_tables_are_created_plain_and_still_work()
    {
        using var context = _db.CreateLibraryContext();
        context.Database.EnsureCreated();

        TableNames().Should().Contain(["OutboxMessages", "InboxMessages"])
            .And.NotContain(name => name.Contains("ddd", StringComparison.Ordinal));

        // And the schema in the model does not stop the provider writing to them.
        context.Inbox.Add(new InboxMessage { MessageId = Guid.CreateVersion7(), Consumer = "c", ProcessedAt = DateTimeOffset.UtcNow });
        context.SaveChanges();

        _db.CountRows("InboxMessages").Should().Be(1);
    }

    [Fact]
    public void A_renamed_outbox_and_inbox_are_created_under_their_new_names()
    {
        using var context = new RenamedStorageContext(_db.Options<RenamedStorageContext>());
        context.Database.EnsureCreated();

        TableNames().Should().Contain(["EventsOut", "EventsIn"]).And.NotContain("OutboxMessages");
    }

    [Fact]
    public void The_migration_helpers_write_a_schema_and_the_columns_the_model_maps()
    {
        using var context = _db.CreateLibraryContext();
        var builder = new MigrationBuilder(SqlServer);

        builder.CreateDomainEventOutbox();
        builder.CreateDomainEventInbox();

        builder.Operations.OfType<EnsureSchemaOperation>().Select(o => o.Name).Should().Equal("ddd", "ddd");

        var outbox = builder.Operations.OfType<CreateTableOperation>().Single(o => o.Name == "OutboxMessages");
        outbox.Schema.Should().Be("ddd");
        outbox.PrimaryKey!.Name.Should().Be("PK_OutboxMessages");
        outbox.PrimaryKey.Columns.Should().Equal(nameof(OutboxMessage.Id));
        AssertMatchesModel(outbox, context.Model.FindEntityType(typeof(OutboxMessage))!);

        var inbox = builder.Operations.OfType<CreateTableOperation>().Single(o => o.Name == "InboxMessages");
        inbox.Schema.Should().Be("ddd");
        // The pair is the key, which is what makes a repeat fail instead of writing a second row.
        inbox.PrimaryKey!.Columns.Should().Equal(nameof(InboxMessage.MessageId), nameof(InboxMessage.Consumer));
        AssertMatchesModel(inbox, context.Model.FindEntityType(typeof(InboxMessage))!);

        var index = builder.Operations.OfType<CreateIndexOperation>().Single(o => o.Table == "OutboxMessages");
        index.Name.Should().Be("IX_OutboxMessages_ProcessedAt");
        index.Schema.Should().Be("ddd");
        index.Columns.Should().Equal(nameof(OutboxMessage.ProcessedAt));
    }

    [Fact]
    public void The_migration_helpers_leave_the_schema_off_where_the_provider_has_none()
    {
        var builder = new MigrationBuilder(Sqlite);

        builder.CreateDomainEventOutbox();
        builder.CreateDomainEventInbox();
        builder.DropDomainEventInbox();
        builder.DropDomainEventOutbox();

        builder.Operations.OfType<EnsureSchemaOperation>().Should().BeEmpty("EnsureSchema means nothing to SQLite");
        builder.Operations.OfType<CreateTableOperation>().Should().OnlyContain(o => o.Schema == null);
        builder.Operations.OfType<CreateIndexOperation>().Should().OnlyContain(o => o.Schema == null);
        builder.Operations.OfType<DropTableOperation>().Select(o => o.Name).Should().Equal("InboxMessages", "OutboxMessages");
        builder.Operations.OfType<DropTableOperation>().Should().OnlyContain(o => o.Schema == null);
    }

    [Fact]
    public void A_hand_written_migration_creates_tables_the_model_can_read_and_write()
    {
        using var database = new SqliteDatabase();
        using var context = database.CreateLibraryContext();

        var builder = new MigrationBuilder(context.Database.ProviderName);
        builder.CreateDomainEventOutbox();
        builder.CreateDomainEventInbox();
        Execute(context, builder);

        var messageId = Guid.CreateVersion7();
        context.Outbox.Add(new OutboxMessage
        {
            Id = messageId,
            EventName = "shelf.created",
            Payload = "{}",
            OccurredAt = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 9, 13, 12, 0, 1, TimeSpan.Zero),
        });
        context.Inbox.Add(new InboxMessage { MessageId = messageId, Consumer = "billing", ProcessedAt = DateTimeOffset.UtcNow });
        context.SaveChanges();

        using var check = database.CreateLibraryContext();
        check.Outbox.Single().EventName.Should().Be("shelf.created");
        check.Inbox.Single().Consumer.Should().Be("billing");

        // The composite key the helper wrote is the thing that stops a second mark.
        using var repeat = database.CreateLibraryContext();
        repeat.Inbox.Add(new InboxMessage { MessageId = messageId, Consumer = "billing", ProcessedAt = DateTimeOffset.UtcNow });
        var twice = () => repeat.SaveChanges();
        twice.Should().Throw<DbUpdateException>();
    }

    [Fact]
    public void The_migration_helpers_write_the_timestamp_column_the_provider_would_have_got()
    {
        // MigrationBuilder knows its provider, so a hand-written migration lands on the same column the
        // model does. If these two ever disagree, the model says datetimeoffset over a table that says
        // datetime2 and nothing complains until a query does.
        var sqlServer = new MigrationBuilder(SqlServer);
        sqlServer.CreateDomainEventOutbox();
        sqlServer.CreateDomainEventInbox();

        TimestampColumns(sqlServer).Should().OnlyContain(column => column.ClrType == typeof(DateTimeOffset));

        var sqlite = new MigrationBuilder(Sqlite);
        sqlite.CreateDomainEventOutbox();
        sqlite.CreateDomainEventInbox();

        TimestampColumns(sqlite).Should().OnlyContain(column => column.ClrType == typeof(DateTime),
            "SQLite cannot order by a DateTimeOffset, so it keeps the UTC DateTime whatever anyone asks for");
    }

    [Fact]
    public void UtcDateTime_writes_the_same_migration_on_every_provider()
    {
        // The promise to a database that already exists: ask for the old shape and the helper writes the
        // old shape, whichever provider is running.
        foreach (var provider in new[] { SqlServer, Postgres, Sqlite })
        {
            var builder = new MigrationBuilder(provider);
            builder.CreateDomainEventOutbox(timestamps: DomainEventTimestamps.UtcDateTime);
            builder.CreateDomainEventInbox(timestamps: DomainEventTimestamps.UtcDateTime);

            TimestampColumns(builder).Should().OnlyContain(column => column.ClrType == typeof(DateTime), provider);
        }
    }

    [Fact]
    public void Only_ProcessedAt_and_NextAttemptAt_are_nullable_timestamps()
    {
        var builder = new MigrationBuilder(SqlServer);
        builder.CreateDomainEventOutbox();
        builder.CreateDomainEventInbox();

        // The outbox's ProcessedAt is what "pending" means, so it has to stay nullable through the change
        // of column type, and a null NextAttemptAt is what "due now" means, which is also what lets the
        // column be added to an existing table without touching a row. The inbox's ProcessedAt is
        // written when the row is, so it is not nullable.
        var outbox = builder.Operations.OfType<CreateTableOperation>().Single(o => o.Name == "OutboxMessages");
        outbox.Columns.Single(c => c.Name == nameof(OutboxMessage.ProcessedAt)).IsNullable.Should().BeTrue();
        outbox.Columns.Single(c => c.Name == nameof(OutboxMessage.NextAttemptAt)).IsNullable.Should().BeTrue();
        outbox.Columns.Single(c => c.Name == nameof(OutboxMessage.CreatedAt)).IsNullable.Should().BeFalse();
        outbox.Columns.Single(c => c.Name == nameof(OutboxMessage.OccurredAt)).IsNullable.Should().BeFalse();

        var inbox = builder.Operations.OfType<CreateTableOperation>().Single(o => o.Name == "InboxMessages");
        inbox.Columns.Single(c => c.Name == nameof(InboxMessage.ProcessedAt)).IsNullable.Should().BeFalse();
    }

    [Fact]
    public void The_migration_helpers_reject_an_empty_table_name()
    {
        var builder = new MigrationBuilder(SqlServer);

        var outbox = () => builder.CreateDomainEventOutbox(tableName: " ");
        var inbox = () => builder.CreateDomainEventInbox(tableName: " ");

        outbox.Should().Throw<ArgumentException>();
        inbox.Should().Throw<ArgumentException>();
    }

    /// <summary>Every timestamp column the builder wrote, across both tables.</summary>
    private static IEnumerable<AddColumnOperation> TimestampColumns(MigrationBuilder builder)
        => builder.Operations
            .OfType<CreateTableOperation>()
            .SelectMany(table => table.Columns)
            .Where(column => column.Name is nameof(OutboxMessage.OccurredAt) or nameof(OutboxMessage.CreatedAt) or nameof(OutboxMessage.ProcessedAt) or nameof(OutboxMessage.NextAttemptAt));

    private static void AssertMatchesModel(CreateTableOperation operation, IEntityType entityType)
    {
        operation.Columns.Select(column => column.Name).Should().BeEquivalentTo(
            entityType.GetProperties().Select(property => property.Name),
            "a hand-written migration and a scaffolded one have to produce the same table");

        foreach (var column in operation.Columns)
        {
            var property = entityType.FindProperty(column.Name)!;
            column.IsNullable.Should().Be(property.IsNullable, $"{column.Name} nullability");
            column.MaxLength.Should().Be(property.GetMaxLength(), $"{column.Name} length");
        }
    }

    private static void Execute(DbContext context, MigrationBuilder builder)
    {
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var connection = context.GetService<IRelationalConnection>();

        foreach (var command in generator.Generate(builder.Operations, context.Model))
        {
            command.ExecuteNonQuery(connection);
        }
    }

    private List<string> TableNames()
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();

        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
