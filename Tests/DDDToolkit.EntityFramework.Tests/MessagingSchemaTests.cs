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
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");

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
    public void The_migration_helpers_reject_an_empty_table_name()
    {
        var builder = new MigrationBuilder(SqlServer);

        var outbox = () => builder.CreateDomainEventOutbox(tableName: " ");
        var inbox = () => builder.CreateDomainEventInbox(tableName: " ");

        outbox.Should().Throw<ArgumentException>();
        inbox.Should().Throw<ArgumentException>();
    }

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
