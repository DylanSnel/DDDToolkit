using System.ComponentModel.DataAnnotations.Schema;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Tests.Converters;
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

    /// <summary>Corrects the price: a change to a child that can break the root's rule.</summary>
    public void Reprice(int price) => Price = price;
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

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }
}
