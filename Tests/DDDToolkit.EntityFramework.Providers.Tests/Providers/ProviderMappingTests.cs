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
/// actually became, which is where the two providers differ and where a SQLite-shaped decision shows
/// up as a cost somebody else pays.
/// </para>
/// </summary>
public abstract class ProviderMappingTests(ProviderFixture fixture) : ProviderTestBase(fixture)
{
    /// <summary>What <c>information_schema</c> calls the column a <see cref="Guid"/> identifier lands in.</summary>
    protected abstract string GuidColumnType { get; }

    /// <summary>What <c>information_schema</c> calls the column behind the primitive collection of ids.</summary>
    protected abstract string TagsColumnType { get; }

    /// <summary>What <c>information_schema</c> calls the outbox's <c>CreatedAt</c> column.</summary>
    protected abstract string OutboxTimestampColumnType { get; }

    /// <summary>
    /// What that column would have been without the UTC <c>DateTime</c> conversion, which is what the
    /// provider maps a bare <see cref="DateTimeOffset"/> to.
    /// </summary>
    protected abstract string NativeOffsetColumnType { get; }

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
    public async Task Outbox_timestamps_are_stored_as_the_converted_UTC_DateTime_not_the_providers_offset_type()
    {
        SkipIfUnavailable();

        // This is the SQLite workaround, visible. SQLite cannot order by DateTimeOffset, so the outbox
        // converts to a UTC DateTime for everybody; on a provider with a native offset type that is a
        // column shape its users did not ask for. Recorded here so the cost is a test, not a memory.
        foreach (var column in new[] { nameof(OutboxMessage.OccurredAt), nameof(OutboxMessage.CreatedAt), nameof(OutboxMessage.ProcessedAt) })
        {
            (await Database.ColumnTypeAsync(DomainEventStorage.DefaultOutboxTableName, column, Cancellation))
                .Should().Be(OutboxTimestampColumnType, "the converter turns every outbox timestamp into a UTC DateTime");
        }

        (await Database.ColumnTypeAsync(DomainEventStorage.DefaultInboxTableName, nameof(InboxMessage.ProcessedAt), Cancellation))
            .Should().Be(OutboxTimestampColumnType);
    }

    [Fact]
    public void What_the_UTC_conversion_costs_this_provider_is_the_difference_between_these_two_types()
    {
        SkipIfUnavailable();

        using var context = Database.CreateContext();

        // What this provider would have stored a DateTimeOffset in, asked of the provider itself, next to
        // what the outbox actually asks for. Where they are the same the SQLite workaround is free here;
        // where they differ it is a column shape this provider's users did not choose.
        var native = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(DateTimeOffset))!.StoreType;
        var stored = context.Model.FindEntityType(typeof(OutboxMessage))!.FindProperty(nameof(OutboxMessage.CreatedAt))!.GetColumnType();

        native.Should().Be(NativeOffsetColumnType);
        stored.Should().Be(OutboxTimestampColumnType);
    }
}
