using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Invariants;

namespace DDDToolkit.Tests.Invariants;

/// <summary>The id of a bar tab. Struct id, so it is never null and never needs validating.</summary>
[EntityId<Guid>("TAB")]
public readonly partial record struct TabId;

/// <summary>The id of one drink on a tab.</summary>
[EntityId<int>("TABLINE")]
public readonly partial record struct TabLineId;

/// <summary>The id of one garnish on a drink. A grandchild of the tab.</summary>
[EntityId<int>("GARNISH")]
public readonly partial record struct GarnishId;

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
    // part of the class, with no accessibility modifier. Nothing here walks the lines: asking a tab
    // whether it is consistent asks its lines too, and doing it again by hand would report a line
    // twice.
    partial void CheckInvariants()
    {
        if (Total > Limit)
        {
            throw InvariantViolation($"A tab may not exceed its limit of {Limit}, and this one totals {Total}.");
        }
    }
}

/// <summary>
/// A child entity with a seam of its own, stating a rule the tab could not state for it. Asking the
/// tab runs this as well, because the tab is the consistency boundary and a boundary answers for what
/// is inside it.
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

    /// <summary>What was put in it. The tab's grandchildren.</summary>
    public partial IReadOnlyList<Garnish> Garnishes { get; }

    /// <summary>Corrects the price, which is how a test breaks the root's rule from a child.</summary>
    public void Reprice(decimal price) => Price = price;

    /// <summary>Drops something in the drink.</summary>
    public Garnish Decorate(GarnishId id, string what)
    {
        var garnish = new Garnish(id, what);
        _garnishes.Add(garnish);
        return garnish;
    }

    partial void CheckInvariants()
    {
        if (Price < 0)
        {
            throw InvariantViolation("A drink cannot cost less than nothing.");
        }
    }
}

/// <summary>
/// What is in the drink: a child of a child, and the reason the walk needs no idea how deep it goes.
/// A line answers for its garnishes exactly as the tab answers for its lines.
/// </summary>
[Entity<GarnishId>]
public partial class Garnish
{
    /// <summary>Puts something in a drink.</summary>
    public Garnish(GarnishId id, string what) : base(id) => What = what;

    /// <summary>What it is.</summary>
    public string What { get; private set; } = string.Empty;

    /// <summary>A rule of its own, stated as a rule rather than a seam so both kinds are covered.</summary>
    public sealed class MustBeSomething : IInvariant<Garnish>
    {
        /// <inheritdoc />
        public string Code => "garnish.named";

        /// <inheritdoc />
        public InvariantFailure? Check(Garnish entity)
            => string.IsNullOrWhiteSpace(entity.What) ? "A garnish has to be something." : null;
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
