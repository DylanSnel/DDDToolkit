using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Interfaces;

namespace DDDToolkit.Tests.Domain;

/// <summary>
/// An aggregate root exercising all four generated collection shapes. Each get-only partial property
/// gets a private backing field (<c>_lines</c>, <c>_tags</c>, <c>_notes</c>, <c>_labels</c>) that only the
/// aggregate can touch; callers see a read-only view that tracks the field.
/// </summary>
[AggregateRoot<BasketId>]
public partial class Basket
{
    /// <summary>Opens a basket and raises <see cref="BasketOpened"/>.</summary>
    public Basket(BasketId id, string owner) : base(id)
    {
        Owner = owner;
        RaiseDomainEvent(new BasketOpened(id));
    }

    /// <summary>Who the basket belongs to.</summary>
    public string Owner { get; private set; } = string.Empty;

    /// <summary>Backed by a <c>List&lt;BasketLine&gt;</c>; the view is a <c>ReadOnlyCollection&lt;BasketLine&gt;</c>.</summary>
    public partial IReadOnlyList<BasketLine> Lines { get; }

    /// <summary>Backed by a <c>HashSet&lt;TagId&gt;</c>; duplicates collapse.</summary>
    public partial IReadOnlySet<TagId> Tags { get; }

    /// <summary>An <c>IEnumerable&lt;T&gt;</c> property is backed by a list just like <c>IReadOnlyList&lt;T&gt;</c>.</summary>
    public partial IEnumerable<string> Notes { get; }

    /// <summary>An <c>IReadOnlyCollection&lt;T&gt;</c> property is backed by a list too.</summary>
    public partial IReadOnlyCollection<string> Labels { get; }

    /// <summary>Adds a line through the backing field and raises <see cref="LineAdded"/>.</summary>
    public void AddLine(BasketLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _lines.Add(line);
        RaiseDomainEvent(new LineAdded(Id, line.Id));
    }

    /// <summary>Removes a line through the backing field. Raises <see cref="LineRemoved"/> only when it removed something.</summary>
    public bool RemoveLine(BasketLineId lineId)
    {
        var index = _lines.FindIndex(line => line.Id == lineId);
        if (index < 0)
        {
            return false;
        }

        _lines.RemoveAt(index);
        RaiseDomainEvent(new LineRemoved(Id, lineId));
        return true;
    }

    /// <summary>Adds a tag. Adding the same tag twice is a no-op because the backing field is a set.</summary>
    public void Tag(TagId tag) => _tags.Add(tag);

    /// <summary>Appends a note.</summary>
    public void Note(string note) => _notes.Add(note);

    /// <summary>Appends a label.</summary>
    public void Label(string label) => _labels.Add(label);
}

/// <summary>
/// A child entity of <see cref="Basket"/>. It has its own collection property and — importantly — no
/// domain-event surface at all: only the root raises events.
/// </summary>
[Entity<BasketLineId>]
public partial class BasketLine
{
    /// <summary>Creates a line.</summary>
    public BasketLine(BasketLineId id, Sku sku, int quantity) : base(id)
    {
        Sku = sku;
        Quantity = quantity;
    }

    /// <summary>The product this line orders.</summary>
    public Sku Sku { get; private set; }

    /// <summary>How many of it.</summary>
    public int Quantity { get; private set; }

    /// <summary>Free-text adjustments, to prove collection generation is not root-specific.</summary>
    public partial IReadOnlyList<string> Adjustments { get; }

    /// <summary>Records an adjustment through the backing field.</summary>
    public void Adjust(string reason) => _adjustments.Add(reason);
}

/// <summary>
/// A child entity that deliberately shares <see cref="BasketId"/> with <see cref="Basket"/>.
/// The equality tests use it to pin what <c>Entity&lt;TId&gt;.Equals</c> actually compares.
/// </summary>
[Entity<BasketId>]
public partial class BasketSnapshot
{
    /// <summary>Creates a snapshot carrying the basket's own id.</summary>
    public BasketSnapshot(BasketId id) : base(id)
    {
    }
}

/// <summary>
/// A second aggregate root over <see cref="BasketId"/>. It exposes the protected <c>RaiseDomainEvent</c>
/// through a public seam so the tests can drive it directly, and doubles as the "different root type,
/// same id type" case for the equality tests.
/// </summary>
[AggregateRoot<BasketId>]
public partial class EventProbe
{
    /// <summary>Creates a probe. Unlike <see cref="Basket"/> it raises nothing on construction.</summary>
    public EventProbe(BasketId id) : base(id)
    {
    }

    /// <summary>Test seam: forwards straight to the protected <c>RaiseDomainEvent</c>.</summary>
    public void Raise(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
}

/// <summary>An aggregate root keyed by a reference-type id, so the equality tests cover both id kinds.</summary>
[AggregateRoot<LedgerId>]
public partial class Ledger
{
    /// <summary>Creates a ledger.</summary>
    public Ledger(LedgerId id) : base(id)
    {
    }
}
