using DTS.DDD.Abstractions.Attributes;

namespace DTS.DDD.EntityFramework.Tests.Domain;

/// <summary>Child entity kept in an <c>IReadOnlySet</c> (HashSet-backed) on <see cref="Shelf"/>.</summary>
[Entity<NoteId>]
public partial class Note
{
    public Note(NoteId id, string text) : base(id)
    {
        Text = text;
    }

    public string Text { get; private set; }
}
