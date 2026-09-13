using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Tests.Invariants;

/// <summary>The id of a bar tab. Struct id, so it is never null and never needs validating.</summary>
[EntityId<Guid>("TAB")]
public readonly partial record struct TabId;

/// <summary>The id of one drink on a tab.</summary>
[EntityId<int>("TABLINE")]
public readonly partial record struct TabLineId;

/// <summary>
/// An aggregate that states no invariants at all: it never implements the generated
/// <c>CheckInvariants()</c> seam. The tests use it to pin that such an aggregate behaves exactly as
/// it did before the seam existed, and that its <c>EnsureInvariants</c> body is empty in the IL.
/// </summary>
[AggregateRoot<TabId>]
public partial class QuietTab
{
    /// <summary>Opens a tab that promises nothing.</summary>
    public QuietTab(TabId id) : base(id)
    {
    }

    /// <summary>Anything at all; nothing checks it.</summary>
    public int Anything { get; private set; }

    /// <summary>Changes the property, so the tests can prove a mutation is still just a mutation.</summary>
    public void Set(int value) => Anything = value;
}

/// <summary>
/// An aggregate with a true invariant: a tab that is closed must have been paid for in full. The
/// rule is about the whole cluster, root and lines together, which is why no per-property validator
/// could express it.
/// </summary>
[AggregateRoot<TabId>]
public partial class Tab
{
    /// <summary>Opens an empty tab with a credit limit.</summary>
    public Tab(TabId id, decimal limit) : base(id)
    {
        Limit = limit;
    }

    /// <summary>The most this tab may ever total.</summary>
    public decimal Limit { get; private set; }

    /// <summary>Whether the guest has settled up.</summary>
    public bool IsPaid { get; private set; }

    /// <summary>The drinks on the tab.</summary>
    public partial IReadOnlyList<TabLine> Lines { get; }

    /// <summary>What the tab comes to.</summary>
    public decimal Total => Lines.Sum(line => line.Price);

    /// <summary>Adds a drink. Notice that nothing here checks the limit; the seam does.</summary>
    public TabLine Order(TabLineId id, string drink, decimal price)
    {
        var line = new TabLine(id, drink, price);
        _lines.Add(line);
        return line;
    }

    /// <summary>Marks the tab settled.</summary>
    public void Pay() => IsPaid = true;

    // The seam. Written exactly as an author writes it: a partial method with a body, in their own
    // part of the class, with no accessibility modifier.
    partial void CheckInvariants()
    {
        if (Total > Limit)
        {
            throw InvariantViolation($"A tab may not exceed its limit of {Limit}, and this one totals {Total}.");
        }

        foreach (var line in Lines)
        {
            line.EnsureInvariants();
        }
    }
}

/// <summary>
/// A child entity with a seam of its own. Nothing calls it automatically: <see cref="Tab"/> calls it
/// from its own check, because only the root knows which of its children a rule is about.
/// </summary>
[Entity<TabLineId>]
public partial class TabLine
{
    /// <summary>Puts a drink on a tab.</summary>
    public TabLine(TabLineId id, string drink, decimal price) : base(id)
    {
        Drink = drink;
        Price = price;
    }

    /// <summary>What was ordered.</summary>
    public string Drink { get; private set; } = string.Empty;

    /// <summary>What it costs.</summary>
    public decimal Price { get; private set; }

    /// <summary>Corrects the price, which is how a test breaks the root's rule from a child.</summary>
    public void Reprice(decimal price) => Price = price;

    partial void CheckInvariants()
    {
        if (Price < 0)
        {
            throw InvariantViolation("A drink cannot cost less than nothing.");
        }
    }
}

/// <summary>An aggregate whose check reports everything it found rather than only the first problem.</summary>
[AggregateRoot<TabId>]
public partial class PickyTab
{
    /// <summary>Creates a tab with a name and a table number, both of which have rules.</summary>
    public PickyTab(TabId id, string name, int table) : base(id)
    {
        Name = name;
        Table = table;
    }

    /// <summary>Who the tab is for.</summary>
    public string Name { get; private set; }

    /// <summary>Which table they are sitting at.</summary>
    public int Table { get; private set; }

    partial void CheckInvariants()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("A tab must be in someone's name.");
        }

        if (Table <= 0)
        {
            problems.Add("A tab must be at a real table.");
        }

        if (problems.Count > 0)
        {
            throw InvariantViolation(problems);
        }
    }
}
