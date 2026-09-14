using System.ComponentModel.DataAnnotations.Schema;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Tests.Converters;
using DDDToolkit.Invariants;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests.Invariants;

/// <summary>The id of a bar tab.</summary>
[EntityId<Guid>("TAB")]
public readonly partial record struct TabId;

/// <summary>The id of one drink on a tab.</summary>
[EntityId<Guid>("TABLINE")]
public readonly partial record struct TabLineId;

/// <summary>
/// An aggregate with a true invariant: the drinks on a tab may not total more than its limit. The
/// rule spans the root and its owned children, so no single property setter could enforce it.
/// </summary>
[AggregateRoot<TabId>]
public partial class Tab
{
    /// <summary>Opens an empty tab with a credit limit.</summary>
    public Tab(TabId id, int limit) : base(id) => Limit = limit;

    /// <summary>The most this tab may ever total.</summary>
    public int Limit { get; private set; }

    /// <summary>The drinks on the tab, owned by it.</summary>
    public partial IReadOnlyList<TabLine> Lines { get; }

    /// <summary>How many times the seam has run against this instance. Not a column.</summary>
    [NotMapped]
    public int Checks { get; private set; }

    /// <summary>What the tab comes to.</summary>
    public int Total => Lines.Sum(line => line.Price);

    /// <summary>Adds a drink. Nothing here checks the limit; the seam does, at the commit.</summary>
    public TabLine Order(string drink, int price)
    {
        var line = new TabLine(TabLineId.CreateUnique(), drink, price);
        _lines.Add(line);
        return line;
    }

    /// <summary>Takes a drink off the tab, which puts the line on its way out of the database.</summary>
    public bool Remove(TabLineId lineId)
    {
        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        return line is not null && _lines.Remove(line);
    }

    /// <summary>Raises the limit, so a test can make a broken tab legal again.</summary>
    public void RaiseLimit(int limit) => Limit = limit;

    partial void CheckInvariants()
    {
        Checks++;

        if (Total > Limit)
        {
            throw InvariantViolation($"A tab may not exceed its limit of {Limit}, and this one totals {Total}.");
        }
    }
}

/// <summary>A drink on a <see cref="Tab"/>. Owned, so it is saved and loaded with its root.</summary>
[Entity<TabLineId>]
public partial class TabLine
{
    /// <summary>Puts a drink on a tab.</summary>
    public TabLine(TabLineId id, string drink, int price) : base(id)
    {
        Drink = drink;
        Price = price;
    }

    /// <summary>What was ordered.</summary>
    public string Drink { get; private set; } = string.Empty;

    /// <summary>What it costs.</summary>
    public int Price { get; private set; }

    /// <summary>How many times this line's own seam has run against this instance. Not a column.</summary>
    [NotMapped]
    public int Checks { get; private set; }

    /// <summary>Corrects the price: a change to a child that can break the root's rule.</summary>
    public void Reprice(int price) => Price = price;

    /// <summary>
    /// A rule of the line alone: nothing on a bar tab is free. The tab could not state this one for
    /// it, which is why a child entity has to be asked for its own invariants and not only through
    /// its root.
    /// </summary>
    partial void CheckInvariants()
    {
        Checks++;

        if (Price <= 0)
        {
            throw InvariantViolation($"A line must cost something, and '{Drink}' costs {Price}.");
        }
    }
}

/// <summary>An aggregate that states no invariants at all, to prove it is saved exactly as before.</summary>
[AggregateRoot<TabId>]
public partial class QuietTab
{
    /// <summary>Opens a tab that promises nothing.</summary>
    public QuietTab(TabId id, string note) : base(id) => Note = note;

    /// <summary>Anything at all; nothing checks it.</summary>
    public string Note { get; private set; }

    /// <summary>Changes the note.</summary>
    public void Renote(string note) => Note = note;
}

/// <summary>The id of a cellar.</summary>
[EntityId<Guid>("CELLAR")]
public readonly partial record struct CellarId;

/// <summary>The id of the bottles of one wine in a cellar.</summary>
[EntityId<Guid>("BOTTLE")]
public readonly partial record struct BottleId;

/// <summary>
/// The bar's cellar, written by hand instead of with <c>[AggregateRoot]</c>, for two things a
/// generated aggregate cannot give these tests.
/// <para>
/// Its children are a plain relationship rather than an owned collection, because Entity Framework
/// always loads owned children together with their owner: only a related child can be left unloaded,
/// which is the case the interceptor must be caught never touching.
/// </para>
/// <para>
/// It also answers all four members of the contract by hand, so the non-throwing stage has something
/// to report and so both subjects are visible in one place: the self-only pair states the cellar's
/// own rule, and the walking pair folds in what its bottles report, which is what a generated root
/// with a child collection does. Writing the walk out here is what lets a test prove the save does
/// not go through it, because a save that did would count a broken bottle twice, once from the bottle
/// and once from the cellar that walked into it. The <c>CheckInvariants()</c> seam can only throw,
/// and that half is covered by <see cref="Tab"/> and <see cref="TabLine"/>.
/// </para>
/// </summary>
public class Cellar : AggregateRoot<CellarId>
{
    /// <summary>The code of the rule that a cellar is named.</summary>
    public const string MustHaveAName = "Cellar.MustHaveAName";

    private readonly List<Bottle> _bottles = [];

    /// <summary>Opens an empty cellar.</summary>
    public Cellar(CellarId id, string name) : base(id) => Name = name;

    /// <summary>What the cellar is called. An empty one breaks the root's own rule.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// How often the navigation below has been read through its property. Not a column. It is here
    /// so that a test can show a save left an unloaded navigation alone rather than claim it: any
    /// walk over <see cref="Bottles"/> moves this number.
    /// </summary>
    [NotMapped]
    public int Reads { get; private set; }

    /// <summary>The bottles in the cellar, related rather than owned, so they can stay unloaded.</summary>
    public IReadOnlyList<Bottle> Bottles
    {
        get
        {
            Reads++;
            return _bottles;
        }
    }

    /// <summary>Puts bottles in the cellar.</summary>
    public Bottle Stock(string label, int quantity)
    {
        var bottle = new Bottle(BottleId.CreateUnique(), label, quantity);
        _bottles.Add(bottle);
        return bottle;
    }

    /// <summary>Renames the cellar. Nothing here refuses an empty name; the commit does.</summary>
    public void Rename(string name) => Name = name;

    /// <inheritdoc />
    public override IReadOnlyList<InvariantViolation> GetOwnInvariantViolations()
        => string.IsNullOrWhiteSpace(Name)
            ? [new InvariantViolation(MustHaveAName, "A cellar must have a name.", GetType(), Id)]
            : [];

    /// <inheritdoc />
    public override void EnsureOwnInvariants() => ThrowIfAny(GetOwnInvariantViolations());

    /// <inheritdoc />
    public override IReadOnlyList<InvariantViolation> GetInvariantViolations()
        => [.. GetOwnInvariantViolations(), .. Bottles.SelectMany(bottle => bottle.GetInvariantViolations())];

    /// <inheritdoc />
    public override void EnsureInvariants() => ThrowIfAny(GetInvariantViolations());

    private void ThrowIfAny(IReadOnlyList<InvariantViolation> violations)
    {
        if (violations.Count > 0)
        {
            throw InvariantViolation(violations.Select(violation => violation.Message));
        }
    }
}

/// <summary>
/// Bottles of one wine in a <see cref="Cellar"/>. Related to its root through a foreign key rather
/// than owned by it, which is the other shape the interceptor has to walk up from.
/// <para>
/// It states only the self-only pair, because it holds no children: the base class answers the
/// walking pair for a leaf by delegating to this one, and a leaf that repeated itself would be the
/// same bug in miniature.
/// </para>
/// </summary>
public class Bottle : Entity<BottleId>
{
    /// <summary>The code of the rule that a cellar never holds a negative number of bottles.</summary>
    public const string MustNotGoNegative = "Bottle.MustNotGoNegative";

    /// <summary>Puts a wine in the cellar.</summary>
    public Bottle(BottleId id, string label, int quantity) : base(id)
    {
        Label = label;
        Quantity = quantity;
    }

    /// <summary>Which wine it is.</summary>
    public string Label { get; private set; }

    /// <summary>How many there are. Below zero is a state the domain says cannot exist.</summary>
    public int Quantity { get; private set; }

    /// <summary>Takes bottles out, without counting what is left. The commit counts.</summary>
    public void Take(int count) => Quantity -= count;

    /// <inheritdoc />
    public override IReadOnlyList<InvariantViolation> GetOwnInvariantViolations()
        => Quantity < 0
            ? [new InvariantViolation(MustNotGoNegative, $"A cellar cannot hold {Quantity} bottles of '{Label}'.", GetType(), Id)]
            : [];

    /// <inheritdoc />
    public override void EnsureOwnInvariants()
    {
        var violations = GetOwnInvariantViolations();

        if (violations.Count > 0)
        {
            throw InvariantViolation(violations.Select(violation => violation.Message));
        }
    }
}

/// <summary>
/// A context holding only the invariant test domain, so these tests cannot disturb (or be disturbed
/// by) the rules of the shared library domain.
/// </summary>
public class TabContext(DbContextOptions<TabContext> options) : DbContext(options)
{
    /// <summary>The tabs with a rule.</summary>
    public DbSet<Tab> Tabs => Set<Tab>();

    /// <summary>The tabs without one.</summary>
    public DbSet<QuietTab> QuietTabs => Set<QuietTab>();

    /// <summary>The cellars, whose children are related rather than owned.</summary>
    public DbSet<Cellar> Cellars => Set<Cellar>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }
}
