using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;

namespace DDDToolkit.EntityFramework.Tests.Domain;

/// <summary>
/// Aggregate root of the test domain: struct id key, a plain struct id property, a nullable struct
/// id, a class id, an owned collection of <see cref="Book"/> and a set of primitive keywords.
/// </summary>
[AggregateRoot<ShelfId>]
public partial class Shelf
{
    public Shelf(ShelfId id, string name, UserId owner, CatId cat, CatId? favouriteCat) : base(id)
    {
        Name = name;
        Owner = owner;
        Cat = cat;
        FavouriteCat = favouriteCat;
        RaiseDomainEvent(new ShelfCreated(id, name));
    }

    public string Name { get; private set; }

    /// <summary>A class id (record deriving from EntityId&lt;Guid&gt;).</summary>
    public UserId Owner { get; private set; }

    /// <summary>A struct id as a plain property.</summary>
    public CatId Cat { get; private set; }

    /// <summary>A nullable struct id.</summary>
    public CatId? FavouriteCat { get; private set; }

    /// <summary>Owned child entities, backed by the generated <c>_books</c> list.</summary>
    public partial IReadOnlyList<Book> Books { get; }

    /// <summary>Owned child entities in a set, backed by the generated <c>_notes</c> hash set.</summary>
    public partial IReadOnlySet<Note> Notes { get; }

    public void Rename(string name)
    {
        Name = name;
        RaiseDomainEvent(new ShelfRenamed(Id, name));
    }

    public void SetFavouriteCat(CatId? cat) => FavouriteCat = cat;

    /// <summary>Raises the current shape of a versioned event, so an old row and a new one can be compared.</summary>
    public void Catalogue(string code, string system) => RaiseDomainEvent(new ShelfCataloguedV2(Id, code, system));

    public Book AddBook(string title, params TagId[] tags)
    {
        var book = new Book(BookId.CreateUnique(), title, tags);
        _books.Add(book);
        RaiseDomainEvent(new BookAdded(Id, book.Id, title));
        return book;
    }

    public bool RemoveBook(BookId bookId)
    {
        var book = _books.FirstOrDefault(b => b.Id == bookId);
        return book is not null && _books.Remove(book);
    }

    public Note AddNote(string text)
    {
        var note = new Note(NoteId.CreateUnique(), text);
        _notes.Add(note);
        return note;
    }
}
