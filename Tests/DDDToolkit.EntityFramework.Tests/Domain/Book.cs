using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.EntityFramework.Tests.Domain;

/// <summary>Child entity of <see cref="Shelf"/> (owned type) with a primitive collection of struct ids.</summary>
[Entity<BookId>]
public partial class Book
{
    public Book(BookId id, string title, IEnumerable<TagId> tags) : base(id)
    {
        Title = title;
        _tags.AddRange(tags);
    }

    public string Title { get; private set; }

    /// <summary>Value-converted struct ids stored as a primitive collection, backed by the generated <c>_tags</c> list.</summary>
    public partial IReadOnlyList<TagId> Tags { get; }

    public void Retitle(string title) => Title = title;

    public void Tag(TagId tag) => _tags.Add(tag);
}
