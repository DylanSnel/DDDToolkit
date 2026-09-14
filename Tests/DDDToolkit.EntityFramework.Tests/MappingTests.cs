using System.Reflection;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Proves EF Core 10 maps what the generators emit: struct ids (key, plain, nullable), class ids,
/// generated read-only collections (owned, primitive, set), complex types and single value objects.
/// Every read-back uses a fresh context so nothing comes from the change tracker.
/// </summary>
public sealed class MappingTests : IDisposable
{
    private readonly SqliteDatabase _db = new();

    public MappingTests() => _db.EnsureCreated(() => _db.CreateLibraryContext());

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Struct_id_key_plain_struct_id_nullable_struct_id_and_class_id_round_trip()
    {
        var id = ShelfId.CreateUnique();
        var owner = UserId.CreateUnique();
        var cat = CatId.CreateUnique();
        var favourite = CatId.CreateUnique();

        using (var context = _db.CreateLibraryContext())
        {
            context.Shelves.Add(new Shelf(id, "Fiction", owner, cat, favourite));
            context.SaveChanges();
        }

        using (var context = _db.CreateLibraryContext())
        {
            var shelf = context.Shelves.Single(s => s.Id == id);

            shelf.Id.Should().Be(id);
            shelf.Owner.Should().Be(owner);
            shelf.Owner.Should().BeOfType<UserId>();
            shelf.Cat.Should().Be(cat);
            shelf.FavouriteCat.Should().Be(favourite);

            // Struct ids are usable in query predicates, both as key and as plain property.
            context.Shelves.Count(s => s.Cat == cat).Should().Be(1);
            context.Shelves.Count(s => s.Owner == owner).Should().Be(1);
        }
    }

    [Fact]
    public void Nullable_struct_id_stores_and_reads_null()
    {
        var id = ShelfId.CreateUnique();

        using (var context = _db.CreateLibraryContext())
        {
            context.Shelves.Add(new Shelf(id, "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), favouriteCat: null));
            context.SaveChanges();
        }

        using (var context = _db.CreateLibraryContext())
        {
            var shelf = context.Shelves.Single(s => s.Id == id);
            shelf.FavouriteCat.Should().BeNull();

            shelf.SetFavouriteCat(CatId.CreateUnique());
            context.SaveChanges();
        }

        using (var context = _db.CreateLibraryContext())
        {
            context.Shelves.Single(s => s.Id == id).FavouriteCat.Should().NotBeNull();
            context.Shelves.Count(s => s.FavouriteCat == null).Should().Be(0);
        }
    }

    [Fact]
    public void Owned_collection_round_trips_is_read_only_and_removes_rows()
    {
        var id = ShelfId.CreateUnique();
        BookId duneId;

        using (var context = _db.CreateLibraryContext())
        {
            var shelf = new Shelf(id, "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null);
            duneId = shelf.AddBook("Dune", new TagId(1), new TagId(2)).Id;
            shelf.AddBook("Emma");
            context.Shelves.Add(shelf);
            context.SaveChanges();
        }

        using (var context = _db.CreateLibraryContext())
        {
            var shelf = context.Shelves.Single(s => s.Id == id);

            shelf.Books.Should().HaveCount(2);
            shelf.Books.Select(b => b.Title).Should().BeEquivalentTo("Dune", "Emma");
            shelf.Books.Single(b => b.Id == duneId).Tags.Should().Equal(new TagId(1), new TagId(2));

            // The property is a read-only view, not the backing list: consumers cannot cast and mutate.
            shelf.Books.Should().NotBeAssignableTo<List<Book>>();
            ((ICollection<Book>)shelf.Books).IsReadOnly.Should().BeTrue();
            var mutate = () => ((ICollection<Book>)shelf.Books).Clear();
            mutate.Should().Throw<NotSupportedException>();
            _db.CountRows("Book").Should().Be(2);

            shelf.RemoveBook(duneId).Should().BeTrue();
            context.SaveChanges();
        }

        using (var context = _db.CreateLibraryContext())
        {
            var shelf = context.Shelves.Single(s => s.Id == id);
            shelf.Books.Should().ContainSingle().Which.Title.Should().Be("Emma");
            _db.CountRows("Book").Should().Be(1);
        }
    }

    [Fact]
    public void Primitive_collection_of_struct_ids_round_trips_through_the_readonly_backing_field()
    {
        var id = ShelfId.CreateUnique();

        using (var context = _db.CreateLibraryContext())
        {
            var shelf = new Shelf(id, "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null);
            shelf.AddBook("Dune", new TagId(7), new TagId(8), new TagId(9));
            context.Shelves.Add(shelf);
            context.SaveChanges();
        }

        using (var context = _db.CreateLibraryContext())
        {
            var book = context.Shelves.Single(s => s.Id == id).Books.Single();
            book.Tags.Should().Equal(new TagId(7), new TagId(8), new TagId(9));
            book.Tags.Should().NotBeAssignableTo<List<TagId>>();

            book.Tag(new TagId(10));
            context.SaveChanges();
        }

        using (var context = _db.CreateLibraryContext())
        {
            var book = context.Shelves.Single(s => s.Id == id).Books.Single();
            book.Tags.Should().Equal(new TagId(7), new TagId(8), new TagId(9), new TagId(10));
        }
    }

    [Fact]
    public void Primitive_collection_element_is_stored_as_the_converted_value()
    {
        var id = ShelfId.CreateUnique();

        using (var context = _db.CreateLibraryContext())
        {
            var shelf = new Shelf(id, "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null);
            shelf.AddBook("Dune", new TagId(1), new TagId(2));
            context.Shelves.Add(shelf);
            context.SaveChanges();
        }

        using var command = _db.Connection.CreateCommand();
        command.CommandText = "SELECT \"Tags\" FROM \"Book\"";
        var json = (string)command.ExecuteScalar()!;

        // EF Core stores primitive collections as JSON; the elements are the ids' int values, not objects.
        json.Should().Be("[1,2]");
    }

    [Fact]
    public void ReadOnlySet_of_owned_entities_backed_by_HashSet_round_trips()
    {
        var id = ShelfId.CreateUnique();

        using (var context = _db.CreateLibraryContext())
        {
            var shelf = new Shelf(id, "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null);
            shelf.AddNote("first");
            shelf.AddNote("second");
            context.Shelves.Add(shelf);
            context.SaveChanges();
        }

        using (var context = _db.CreateLibraryContext())
        {
            var shelf = context.Shelves.Single(s => s.Id == id);
            shelf.Notes.Should().HaveCount(2);
            shelf.Notes.Select(n => n.Text).Should().BeEquivalentTo("first", "second");
            shelf.Notes.Should().NotBeAssignableTo<HashSet<Note>>();
        }
    }

    [Fact]
    public void ReadOnlySet_of_primitives_cannot_be_mapped_and_fails_with_guidance()
    {
        // EF Core 10 only accepts arrays and IList<T> as primitive collections ("Collections of primitive
        // types must be arrays or ordered lists"); a HashSet-backed IReadOnlySet<string> is therefore not
        // mappable. The convention turns the silent skip into an error that names the fix.
        using var context = new UnmappableSetContext(_db.Options<UnmappableSetContext>());

        var act = () => context.Model;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TagCloud.Keywords*HashSet*IList<T>*IReadOnlyList<String>*");
    }

    [Fact]
    public void Complex_type_and_single_value_objects_round_trip_including_null()
    {
        var withEmail = MemberId.CreateUnique();
        var withoutEmail = MemberId.CreateUnique();
        var name = new PersonName("Ada", "Lovelace");
        // The generated constructor of a single value object is protected; the always-valid twin is public.
        var birthday = new ValidDateOfBirth(new DateOnly(1815, 12, 10));

        using (var context = _db.CreateLibraryContext())
        {
            context.People.Add(new Person(withEmail, name, EmailAddress.Create("ada@example.com"), birthday));
            context.People.Add(new Person(withoutEmail, new PersonName("Charles", "Babbage"), null, new ValidDateOfBirth(new DateOnly(1791, 12, 26))));
            context.SaveChanges();
        }

        using (var context = _db.CreateLibraryContext())
        {
            var ada = context.People.Single(p => p.Id == withEmail);
            ada.Name.Should().Be(name);
            ada.Name.FirstName.Should().Be("Ada");
            ada.Name.LastName.Should().Be("Lovelace");
            ada.Name.MiddleNames.Should().BeNull();
            ada.Email.Should().Be(EmailAddress.Create("ada@example.com"));
            ada.DateOfBirth.Value.Should().Be(birthday.Value);

            var charles = context.People.Single(p => p.Id == withoutEmail);
            charles.Email.Should().BeNull();

            context.People.Count(p => p.Email == null).Should().Be(1);
            context.People.Count(p => p.Name.LastName == "Lovelace").Should().Be(1);

            charles.ChangeEmail(EmailAddress.Create("charles@example.com"));
            ada.ChangeEmail(null);
            context.SaveChanges();
        }

        using (var context = _db.CreateLibraryContext())
        {
            context.People.Single(p => p.Id == withEmail).Email.Should().BeNull();
            context.People.Single(p => p.Id == withoutEmail).Email!.Value.Should().Be("charles@example.com");
        }
    }

    [Fact]
    public void Complex_type_columns_are_inlined_in_the_owner_table()
    {
        using var context = _db.CreateLibraryContext();
        var person = context.Model.FindEntityType(typeof(Person))!;

        person.FindComplexProperty(nameof(Person.Name)).Should().NotBeNull();
        person.FindProperty(nameof(Person.Email))!.GetMaxLength().Should().Be(EmailAddress.MaxLength);
        context.Database.GenerateCreateScript().Should().Contain("\"Name_FirstName\"").And.Contain("\"Name_LastName\"");
    }

    [Fact]
    public void Version_is_a_concurrency_token_on_aggregate_roots_only()
    {
        using var context = _db.CreateLibraryContext();

        context.Model.FindEntityType(typeof(Shelf))!.FindProperty(nameof(IAggregateRoot.Version))!.IsConcurrencyToken.Should().BeTrue();
        context.Model.FindEntityType(typeof(Person))!.FindProperty(nameof(IAggregateRoot.Version))!.IsConcurrencyToken.Should().BeTrue();
        context.Model.FindEntityType(typeof(Book))!.FindProperty(nameof(IAggregateRoot.Version)).Should().BeNull();
    }

    [Fact]
    public void Internal_members_such_as_DomainEvents_are_not_mapped()
    {
        using var context = _db.CreateLibraryContext();
        var shelf = context.Model.FindEntityType(typeof(Shelf))!;

        shelf.FindProperty(nameof(IHasDomainEvents.DomainEvents)).Should().BeNull();
        shelf.FindNavigation(nameof(IHasDomainEvents.DomainEvents)).Should().BeNull();
        shelf.GetProperties().Select(p => p.Name).Should().BeEquivalentTo("Id", "Cat", "FavouriteCat", "Name", "Owner", "Version");
    }

    [Fact]
    public void The_cached_read_only_view_field_is_invisible_to_Entity_Framework()
    {
        using var context = _db.CreateLibraryContext();
        var shelf = context.Model.FindEntityType(typeof(Shelf))!;

        // The generator holds each read-only view in a second field so a read does not build a new
        // wrapper every time. EF finds fields through the property they back, and nothing is named
        // after this one, so it should be as invisible as any other private state. Asserting it is
        // what makes that a guarantee rather than an observation: a field EF decided to map would
        // become a column, and a column becomes a migration.
        var cacheFields = typeof(Shelf)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => f.Name.EndsWith("View", StringComparison.Ordinal))
            .Select(f => f.Name)
            .ToArray();

        cacheFields.Should().BeEquivalentTo(new[] { "__booksView", "__notesView" }, "the views are cached, not rebuilt per read");

        foreach (var name in cacheFields)
        {
            shelf.FindProperty(name).Should().BeNull();
            shelf.FindNavigation(name).Should().BeNull();
        }

        shelf.GetNavigations().Select(n => n.Name).Should().Contain(nameof(Shelf.Books));
        shelf.FindNavigation(nameof(Shelf.Books))!.GetFieldName().Should().Be("_books", "the list is the state, the view is not");
    }

    [Fact]
    public void A_cached_view_still_sees_what_the_aggregate_does_to_its_list()
    {
        var id = ShelfId.CreateUnique();
        var shelf = new Shelf(id, "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null);

        // Read first, so the view exists before the collection changes. A cache that copied instead of
        // wrapping would answer with the empty list it saw.
        var before = shelf.Books;
        before.Should().BeEmpty();

        shelf.AddBook("Dune");

        before.Should().ContainSingle("the view wraps the list rather than copying it");
        shelf.Books.Should().BeSameAs(before, "and the same view is handed out again");
    }

    [Fact]
    public void Aggregate_is_read_back_without_pending_events()
    {
        var id = ShelfId.CreateUnique();

        using (var context = _db.CreateLibraryContext())
        {
            var shelf = new Shelf(id, "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null);
            ((IHasDomainEvents)shelf).DomainEvents.Should().ContainSingle();
            context.Shelves.Add(shelf);
            context.SaveChanges();
        }

        using (var context = _db.CreateLibraryContext())
        {
            var shelf = context.Shelves.Single(s => s.Id == id);
            ((IHasDomainEvents)shelf).DomainEvents.Should().BeEmpty("materialization goes through the protected constructor, not the domain one");
        }
    }
}
