using System.Globalization;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDDToolkit.EntityFramework.Providers.Tests.Providers;

/// <summary>
/// The SQLite mapping suite, run against a real server. Every test here has a twin in
/// <c>Tests/DDDToolkit.EntityFramework.Tests/MappingTests.cs</c> and asserts the same thing, on the
/// same aggregates, because the question is not "does the toolkit work" but "does it work when the
/// provider stops being forgiving".
/// <para>
/// The tests that read <c>information_schema</c> are the other half. They pin down what a column
/// actually became. That is where the providers stop agreeing, and each place they differ is either a
/// deliberate choice, in which case the test says which, or a portability limit somebody deserves to
/// hear about before production rather than during it.
/// </para>
/// </summary>
public abstract class ProviderMappingTests(ProviderFixture fixture) : ProviderTestBase(fixture)
{
    /// <summary>What <c>information_schema</c> calls the column a <see cref="Guid"/> identifier lands in.</summary>
    protected abstract string GuidColumnType { get; }

    /// <summary>What <c>information_schema</c> calls the column behind the primitive collection of ids.</summary>
    protected abstract string TagsColumnType { get; }

    /// <summary>
    /// What the outbox and inbox timestamps become here with
    /// <see cref="DomainEventTimestamps.ProviderDefault"/>, which is the provider's own instant type.
    /// </summary>
    protected abstract string ProviderTimestampColumnType { get; }

    /// <summary>
    /// What they become with <see cref="DomainEventTimestamps.UtcDateTime"/>, the shape every provider
    /// used to get. On PostgreSQL this is the same answer as
    /// <see cref="ProviderTimestampColumnType"/>; on SQL Server it is not, and that difference is the
    /// whole reason the choice exists.
    /// </summary>
    protected abstract string UtcDateTimeColumnType { get; }

    /// <summary>
    /// Whether a database written with <see cref="DomainEventTimestamps.UtcDateTime"/> can still be
    /// read by the default mapping. True where the two shapes are the same column, which is
    /// PostgreSQL; false where the column really moved, which is SQL Server.
    /// </summary>
    protected abstract bool UnmigratedDatabaseStillReads { get; }

    /// <summary>
    /// A <c>WHERE</c> clause, in this provider's own SQL, that counts the rows of <c>Book</c> whose
    /// <c>Tags</c> collection contains the tag 7. There is no portable spelling of this, which is the
    /// finding it exists to record.
    /// </summary>
    protected abstract string TagsContainSevenSql { get; }

    private static Shelf NewShelf(ShelfId id, CatId? favourite = null)
        => new(id, "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), favourite);

    [Fact]
    public async Task Struct_id_key_plain_struct_id_nullable_struct_id_and_class_id_round_trip()
    {
        SkipIfUnavailable();

        var id = ShelfId.CreateUnique();
        var owner = UserId.CreateUnique();
        var cat = CatId.CreateUnique();
        var favourite = CatId.CreateUnique();

        await using (var context = Database.CreateContext())
        {
            context.Shelves.Add(new Shelf(id, "Fiction", owner, cat, favourite));
            await context.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            var shelf = await context.Shelves.SingleAsync(s => s.Id == id, Cancellation);

            shelf.Id.Should().Be(id);
            shelf.Owner.Should().Be(owner);
            shelf.Owner.Should().BeOfType<UserId>();
            shelf.Cat.Should().Be(cat);
            shelf.FavouriteCat.Should().Be(favourite);

            // Struct ids are usable in query predicates, both as key and as plain property.
            (await context.Shelves.CountAsync(s => s.Cat == cat, Cancellation)).Should().Be(1);
            (await context.Shelves.CountAsync(s => s.Owner == owner, Cancellation)).Should().Be(1);
        }
    }

    [Fact]
    public async Task Struct_id_key_is_stored_as_the_provider_type_of_its_value()
    {
        SkipIfUnavailable();

        // The Guid's own column type, not a string and not a blob. The name differs per provider; that
        // all four of these are the same name is the part that matters.
        var type = await Database.ColumnTypeAsync("Shelves", "Id", Cancellation);

        type.Should().Be(GuidColumnType);
        (await Database.ColumnTypeAsync("Shelves", "Cat", Cancellation)).Should().Be(type);
        (await Database.ColumnTypeAsync("Shelves", "FavouriteCat", Cancellation)).Should().Be(type, "a nullable id is the same column, nullable");
        (await Database.ColumnTypeAsync("Shelves", "Owner", Cancellation)).Should().Be(type, "a record id converts to exactly the same provider type as a struct id");
    }

    [Fact]
    public async Task Nullable_struct_id_stores_and_reads_null()
    {
        SkipIfUnavailable();

        var id = ShelfId.CreateUnique();

        await using (var context = Database.CreateContext())
        {
            context.Shelves.Add(NewShelf(id));
            await context.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            var shelf = await context.Shelves.SingleAsync(s => s.Id == id, Cancellation);
            shelf.FavouriteCat.Should().BeNull();

            shelf.SetFavouriteCat(CatId.CreateUnique());
            await context.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            (await context.Shelves.SingleAsync(s => s.Id == id, Cancellation)).FavouriteCat.Should().NotBeNull();
            (await context.Shelves.CountAsync(s => s.FavouriteCat == null, Cancellation)).Should().Be(0);
        }
    }

    [Fact]
    public async Task Owned_collection_round_trips_is_read_only_and_removes_rows()
    {
        SkipIfUnavailable();

        var id = ShelfId.CreateUnique();
        BookId duneId;

        await using (var context = Database.CreateContext())
        {
            var shelf = NewShelf(id);
            duneId = shelf.AddBook("Dune", new TagId(1), new TagId(2)).Id;
            shelf.AddBook("Emma");
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            var shelf = await context.Shelves.SingleAsync(s => s.Id == id, Cancellation);

            shelf.Books.Should().HaveCount(2);
            shelf.Books.Select(b => b.Title).Should().BeEquivalentTo("Dune", "Emma");
            shelf.Books.Single(b => b.Id == duneId).Tags.Should().Equal(new TagId(1), new TagId(2));
            shelf.Books.Should().NotBeAssignableTo<List<Book>>();
            (await Database.CountRowsAsync("Book", schema: null, Cancellation)).Should().Be(2);

            shelf.RemoveBook(duneId).Should().BeTrue();
            await context.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            var shelf = await context.Shelves.SingleAsync(s => s.Id == id, Cancellation);
            shelf.Books.Should().ContainSingle().Which.Title.Should().Be("Emma");
            (await Database.CountRowsAsync("Book", schema: null, Cancellation)).Should().Be(1);
        }
    }

    [Fact]
    public async Task Primitive_collection_of_struct_ids_round_trips_and_stores_the_converted_value()
    {
        SkipIfUnavailable();

        var id = ShelfId.CreateUnique();

        await using (var context = Database.CreateContext())
        {
            var shelf = NewShelf(id);
            shelf.AddBook("Dune", new TagId(7), new TagId(8), new TagId(9));
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            var book = (await context.Shelves.SingleAsync(s => s.Id == id, Cancellation)).Books.Single();
            book.Tags.Should().Equal(new TagId(7), new TagId(8), new TagId(9));
            book.Tags.Should().NotBeAssignableTo<List<TagId>>();

            book.Tag(new TagId(10));
            await context.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            (await context.Shelves.SingleAsync(s => s.Id == id, Cancellation)).Books.Single()
                .Tags.Should().Equal(new TagId(7), new TagId(8), new TagId(9), new TagId(10));
        }

        // The elements are the ids' int values. How they are held is the provider's business, and the two
        // providers answer differently, which is exactly why this is asserted rather than assumed.
        (await Database.ColumnTypeAsync("Book", "Tags", Cancellation)).Should().Be(TagsColumnType);
    }

    [Fact]
    public async Task ReadOnlySet_of_owned_entities_backed_by_HashSet_round_trips()
    {
        SkipIfUnavailable();

        var id = ShelfId.CreateUnique();

        await using (var context = Database.CreateContext())
        {
            var shelf = NewShelf(id);
            shelf.AddNote("first");
            shelf.AddNote("second");
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            var shelf = await context.Shelves.SingleAsync(s => s.Id == id, Cancellation);
            shelf.Notes.Should().HaveCount(2);
            shelf.Notes.Select(n => n.Text).Should().BeEquivalentTo("first", "second");
            shelf.Notes.Should().NotBeAssignableTo<HashSet<Note>>();
        }
    }

    [Fact]
    public async Task Complex_type_and_single_value_objects_round_trip_including_null()
    {
        SkipIfUnavailable();

        var withEmail = MemberId.CreateUnique();
        var withoutEmail = MemberId.CreateUnique();
        var name = new PersonName("Ada", "Lovelace");
        var birthday = new ValidDateOfBirth(new DateOnly(1815, 12, 10));

        await using (var context = Database.CreateContext())
        {
            context.People.Add(new Person(withEmail, name, EmailAddress.Create("ada@example.com"), birthday));
            context.People.Add(new Person(withoutEmail, new PersonName("Charles", "Babbage"), null, new ValidDateOfBirth(new DateOnly(1791, 12, 26))));
            await context.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            var ada = await context.People.SingleAsync(p => p.Id == withEmail, Cancellation);
            ada.Name.Should().Be(name);
            ada.Name.MiddleNames.Should().BeNull();
            ada.Email.Should().Be(EmailAddress.Create("ada@example.com"));
            ada.DateOfBirth.Value.Should().Be(birthday.Value);

            (await context.People.SingleAsync(p => p.Id == withoutEmail, Cancellation)).Email.Should().BeNull();
            (await context.People.CountAsync(p => p.Email == null, Cancellation)).Should().Be(1);
            (await context.People.CountAsync(p => p.Name.LastName == "Lovelace", Cancellation)).Should().Be(1);
        }
    }

    [Fact]
    public async Task Complex_type_columns_are_inlined_in_the_owner_table()
    {
        SkipIfUnavailable();

        (await Database.ColumnTypeAsync("People", "Name_FirstName", Cancellation)).Should().NotBeNull();
        (await Database.ColumnTypeAsync("People", "Name_LastName", Cancellation)).Should().NotBeNull();
        (await Database.ColumnTypeAsync("People", "Email", Cancellation)).Should().NotBeNull("a single value object is one column, not a table");

        await using var context = Database.CreateContext();
        context.Model.FindEntityType(typeof(Person))!.FindProperty(nameof(Person.Email))!.GetMaxLength().Should().Be(EmailAddress.MaxLength);
    }

    [Fact]
    public async Task Version_is_a_concurrency_token_on_aggregate_roots_only()
    {
        SkipIfUnavailable();

        await using var context = Database.CreateContext();

        context.Model.FindEntityType(typeof(Shelf))!.FindProperty(nameof(IAggregateRoot.Version))!.IsConcurrencyToken.Should().BeTrue();
        context.Model.FindEntityType(typeof(Person))!.FindProperty(nameof(IAggregateRoot.Version))!.IsConcurrencyToken.Should().BeTrue();
        context.Model.FindEntityType(typeof(Book))!.FindProperty(nameof(IAggregateRoot.Version)).Should().BeNull();

        (await Database.ColumnTypeAsync("Shelves", nameof(IAggregateRoot.Version), Cancellation)).Should().NotBeNull();
    }

    [Fact]
    public async Task Internal_members_such_as_DomainEvents_are_not_mapped()
    {
        SkipIfUnavailable();

        await using var context = Database.CreateContext();
        var shelf = context.Model.FindEntityType(typeof(Shelf))!;

        shelf.FindProperty(nameof(IHasDomainEvents.DomainEvents)).Should().BeNull();
        shelf.FindNavigation(nameof(IHasDomainEvents.DomainEvents)).Should().BeNull();
        shelf.GetProperties().Select(p => p.Name).Should().BeEquivalentTo("Id", "Cat", "FavouriteCat", "Name", "Owner", "Version");

        (await Database.ColumnTypeAsync("Shelves", nameof(IHasDomainEvents.DomainEvents), Cancellation))
            .Should().BeNull("an unmapped member must not become a column either");
    }

    [Fact]
    public async Task Aggregate_is_read_back_without_pending_events()
    {
        SkipIfUnavailable();

        var id = ShelfId.CreateUnique();

        await using (var context = Database.CreateContext())
        {
            var shelf = NewShelf(id);
            ((IHasDomainEvents)shelf).DomainEvents.Should().ContainSingle();
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            var shelf = await context.Shelves.SingleAsync(s => s.Id == id, Cancellation);
            ((IHasDomainEvents)shelf).DomainEvents.Should().BeEmpty("materialization goes through the protected constructor, not the domain one");
        }
    }

    [Fact]
    public async Task Outbox_and_inbox_tables_land_in_the_ddd_schema()
    {
        SkipIfUnavailable();

        // SQLite has no schemas and silently drops the argument, so this default is untested there.
        (await Database.TableSchemaAsync(DomainEventStorage.DefaultOutboxTableName, Cancellation)).Should().Be(DomainEventStorage.DefaultSchema);
        (await Database.TableSchemaAsync(DomainEventStorage.DefaultInboxTableName, Cancellation)).Should().Be(DomainEventStorage.DefaultSchema);

        (await Database.CountRowsAsync(DomainEventStorage.DefaultOutboxTableName, DomainEventStorage.DefaultSchema, Cancellation)).Should().Be(0);
        (await Database.CountRowsAsync(DomainEventStorage.DefaultInboxTableName, DomainEventStorage.DefaultSchema, Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task Outbox_and_inbox_timestamps_are_stored_in_this_providers_own_instant_type()
    {
        SkipIfUnavailable();

        // The default is no longer SQLite's shape imposed on everybody. Every provider that can order
        // its own offset type gets it; SQLite, which cannot, still gets the UTC DateTime.
        foreach (var column in new[] { nameof(OutboxMessage.OccurredAt), nameof(OutboxMessage.CreatedAt), nameof(OutboxMessage.ProcessedAt), nameof(OutboxMessage.NextAttemptAt) })
        {
            (await Database.ColumnTypeAsync(DomainEventStorage.DefaultOutboxTableName, column, Cancellation))
                .Should().Be(ProviderTimestampColumnType);
        }

        (await Database.ColumnTypeAsync(DomainEventStorage.DefaultInboxTableName, nameof(InboxMessage.ProcessedAt), Cancellation))
            .Should().Be(ProviderTimestampColumnType);
    }

    [Fact]
    public void The_provider_default_is_the_type_this_provider_maps_a_DateTimeOffset_to()
    {
        SkipIfUnavailable();

        using var context = Database.CreateContext();

        // Asked of the provider itself rather than hard-coded twice: the outbox column has to be the
        // same type the provider would have picked on its own, or "provider default" is a lie.
        var native = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(DateTimeOffset))!.StoreType;
        var stored = context.Model.FindEntityType(typeof(OutboxMessage))!.FindProperty(nameof(OutboxMessage.CreatedAt))!.GetColumnType();

        stored.Should().Be(native);
        stored.Should().Be(ProviderTimestampColumnType);
    }

    [Fact]
    public async Task UtcDateTime_still_writes_the_columns_an_existing_database_already_has()
    {
        SkipIfUnavailable();

        // The promise to somebody who already has a 3.0 database: pass DomainEventTimestamps.UtcDateTime
        // and nothing about the columns moves. Tested as DDL in the same server, because that promise is
        // about DDL.
        await using (var legacy = Database.CreateTimestampShapeContext())
        {
            await Database.ExecuteScriptAsync(legacy.Database.GenerateCreateScript(), Cancellation);
        }

        foreach (var column in new[] { nameof(OutboxMessage.OccurredAt), nameof(OutboxMessage.CreatedAt), nameof(OutboxMessage.ProcessedAt), nameof(OutboxMessage.NextAttemptAt) })
        {
            (await Database.ColumnTypeAsync(TimestampShapeContext.OutboxTable, column, Cancellation))
                .Should().Be(UtcDateTimeColumnType);
        }

        (await Database.ColumnTypeAsync(TimestampShapeContext.InboxTable, nameof(InboxMessage.ProcessedAt), Cancellation))
            .Should().Be(UtcDateTimeColumnType);
    }

    [Fact]
    public async Task Both_timestamp_shapes_round_trip_the_same_instant()
    {
        SkipIfUnavailable();

        // Whichever column it lands in, the value that comes back is the same instant with an offset of
        // zero. That is what makes the choice a storage decision rather than a semantic one, and it is
        // what lets the documentation say an upgrade does not change what a row means.
        var written = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var local = written.ToOffset(TimeSpan.FromHours(2));

        await using (var legacy = Database.CreateTimestampShapeContext())
        {
            await Database.ExecuteScriptAsync(legacy.Database.GenerateCreateScript(), Cancellation);

            legacy.Set<OutboxMessage>().Add(new OutboxMessage { Id = Guid.CreateVersion7(), EventName = "E", Payload = "{}", OccurredAt = local, CreatedAt = local });
            await legacy.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            context.Outbox.Add(new OutboxMessage { Id = Guid.CreateVersion7(), EventName = "E", Payload = "{}", OccurredAt = local, CreatedAt = local });
            await context.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            var row = await context.Outbox.SingleAsync(Cancellation);
            row.CreatedAt.Should().Be(written);
            row.CreatedAt.Offset.Should().Be(TimeSpan.Zero, "an offset the writer happened to be in is not a fact worth storing");
        }

        await using (var legacy = Database.CreateTimestampShapeContext())
        {
            var row = await legacy.Set<OutboxMessage>().SingleAsync(Cancellation);
            row.CreatedAt.Should().Be(written);
            row.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task Upgrading_the_code_without_running_the_migration_breaks_loudly_where_the_column_moved()
    {
        SkipIfUnavailable();

        // The state somebody arrives in by upgrading the package and not scaffolding a migration: the
        // model asks for the provider's own instant type, the table still has the old UTC DateTime
        // column. What happens then is the single most useful thing this file can tell a reader, and it
        // is not the same on both providers, so it is asserted rather than assumed.
        var written = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

        await using (var legacy = Database.CreateTimestampShapeContext())
        {
            await Database.ExecuteScriptAsync(legacy.Database.GenerateCreateScript(), Cancellation);
        }

        // The write goes through either way. The server converts the parameter into the column it has.
        await using (var upgraded = Database.CreateUpgradedTimestampContext())
        {
            upgraded.Set<OutboxMessage>().Add(new OutboxMessage { Id = Guid.CreateVersion7(), EventName = "E", Payload = "{}", OccurredAt = written, CreatedAt = written });
            await upgraded.SaveChangesAsync(Cancellation);
        }

        // And it landed as the right instant, which the mapping that matches the table can confirm.
        await using (var legacy = Database.CreateTimestampShapeContext())
        {
            (await legacy.Set<OutboxMessage>().SingleAsync(Cancellation)).CreatedAt.Should().Be(written);
        }

        // The read is where the two providers part company.
        await using (var upgraded = Database.CreateUpgradedTimestampContext())
        {
            var read = async () => await upgraded.Set<OutboxMessage>().SingleAsync(Cancellation);

            if (UnmigratedDatabaseStillReads)
            {
                (await read()).CreatedAt.Should().Be(written);
                return;
            }

            // Not a slow query, not a wrong answer: an InvalidCastException on the first row read,
            // because the driver hands back the column's own CLR type and the model wanted the other
            // one. That is the good outcome. An unmigrated database announces itself the first time
            // the outbox is polled, rather than quietly disagreeing with the model for months.
            await read.Should().ThrowAsync<InvalidCastException>();
        }
    }

    [Fact]
    public async Task A_primitive_collection_is_queryable_through_LINQ_on_both_providers()
    {
        SkipIfUnavailable();

        // The portable half of the answer. The column is an integer[] on PostgreSQL and a JSON string on
        // SQL Server, but Contains and Count translate on both, so LINQ over the collection ports.
        await using (var context = Database.CreateContext())
        {
            var shelf = NewShelf(ShelfId.CreateUnique());
            shelf.AddBook("Dune", new TagId(7), new TagId(8));
            shelf.AddBook("Emma", new TagId(9));
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync(Cancellation);
        }

        await using (var context = Database.CreateContext())
        {
            var titles = await context.Shelves
                .SelectMany(s => s.Books)
                .Where(b => b.Tags.Contains(new TagId(7)))
                .Select(b => b.Title)
                .ToListAsync(Cancellation);

            titles.Should().Equal("Dune");

            (await context.Shelves.SelectMany(s => s.Books).CountAsync(b => b.Tags.Count == 2, Cancellation)).Should().Be(1);
        }
    }

    [Fact]
    public async Task The_same_question_in_SQL_needs_a_different_query_per_provider()
    {
        SkipIfUnavailable();

        // The other half, and the honest one. The SQL that reaches inside the column is written by the
        // subclass because there is no spelling that runs on both. Anything below LINQ, a report, a
        // migration, a DBA's ad hoc query, does not port.
        await using (var context = Database.CreateContext())
        {
            var shelf = NewShelf(ShelfId.CreateUnique());
            shelf.AddBook("Dune", new TagId(7), new TagId(8));
            shelf.AddBook("Emma", new TagId(9));
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync(Cancellation);
        }

        Convert.ToInt32(await Database.ScalarAsync(TagsContainSevenSql, Cancellation), CultureInfo.InvariantCulture).Should().Be(1);
    }
}
