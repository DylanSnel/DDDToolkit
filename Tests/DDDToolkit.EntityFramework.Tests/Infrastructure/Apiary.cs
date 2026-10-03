using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

// An apiary, to put the privileges a script writes against a real Postgres: a hive has a number that is fixed
// once the hive stands, a keeper who alone changes it, and boxes stacked on it, which are its own entities in a
// table of their own. Settling a hive and renaming one raise an event, so a save writes an outbox row too.

[EntityId<Guid>]
public readonly partial record struct HiveId;

[EntityId<Guid>]
public readonly partial record struct HiveBoxId;

/// <summary>A hive was set up in the apiary.</summary>
[DomainEventName("hive.settled")]
public sealed record HiveSettled(HiveId Hive, int Number) : DomainEvent;

/// <summary>A hive got another label.</summary>
[DomainEventName("hive.renamed")]
public sealed record HiveRenamed(HiveId Hive, string Label) : DomainEvent;

[AggregateRoot<HiveId>]
public partial class Hive
{
    public Hive(HiveId id, int number, string label, Guid? keeper, bool isOpen) : base(id)
    {
        Number = number;
        Label = label;
        Keeper = keeper;
        IsOpen = isOpen;
        RaiseDomainEvent(new HiveSettled(id, number));
    }

    /// <summary>What the hive is known by in the apiary: given when it is settled, and never changed.</summary>
    public int Number { get; private set; }

    public string Label { get; private set; }

    public Guid? Keeper { get; private set; }

    /// <summary>Whether visitors may see the hive.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The boxes stacked on the hive: entities of the aggregate, in a table of their own.</summary>
    public partial IReadOnlyList<HiveBox> Boxes { get; }

    public void Rename(string label)
    {
        Label = label;
        RaiseDomainEvent(new HiveRenamed(Id, label));
    }

    /// <summary>What the mapping refuses: the number is fixed once the hive stands.</summary>
    public void Renumber(int number) => Number = number;

    public void Stack(string kind) => _boxes.Add(new HiveBox(HiveBoxId.CreateSequential(), kind));

    /// <summary>Takes the top box off again.</summary>
    public void Unstack() => _boxes.RemoveAt(_boxes.Count - 1);

    /// <summary>Puts the bottom box to another use.</summary>
    public void Refit(string kind) => _boxes[0].Refit(kind);
}

[Entity<HiveBoxId>]
public partial class HiveBox
{
    public HiveBox(HiveBoxId id, string kind) : base(id) => Kind = kind;

    public string Kind { get; private set; }

    public void Refit(string kind) => Kind = kind;
}

/// <summary>
/// Whether a hive carries a honey box: a question about the hive's boxes, which a policy on the hives' table
/// cannot ask itself, so it is a function the rules call. It runs as its owner, past the boxes' own policies.
/// </summary>
[AccessFunction<Hive>("apiary.has_honey")]
public static partial class HivesWithHoney
{
    public static bool Allows(Hive hive, Caller caller) => hive.Boxes.Any(box => box.Kind == "honey");
}

/// <summary>A visitor reads the hives that carry honey, open to visitors or not.</summary>
[RowAccess<Hive>(RowOperations.Read, To = [RowAccessRoles.Anonymous])]
public static partial class VisitorsReadHivesWithHoney
{
    public static bool Allows(Hive hive, Caller caller) => HivesWithHoney.Allows(hive, caller);
}

/// <summary>A keeper does anything with their own hives, and with nobody else's.</summary>
[RowAccess<Hive>(RowOperations.All, To = [RowAccessRoles.User])]
public static partial class KeepersHaveTheirHives
{
    public static bool Allows(Hive hive, Caller caller) => caller.IsSignedIn && hive.Keeper == caller.UserId;
}

/// <summary>Whoever signed in reads every hive.</summary>
[RowAccess<Hive>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class SignedInCallersReadEveryHive
{
    public static bool Allows(Hive hive, Caller caller) => caller.IsSignedIn;
}

/// <summary>A visitor, who did not sign in, reads the hives that are open to visitors, and nothing else.</summary>
[RowAccess<Hive>(RowOperations.Read, To = [RowAccessRoles.Anonymous])]
public static partial class VisitorsReadOpenHives
{
    public static bool Allows(Hive hive, Caller caller) => hive.IsOpen;
}

/// <summary>The apiary's rules, each as a script takes it.</summary>
public static class ApiaryRules
{
    /// <summary>The token role a ranger's token carries.</summary>
    public const string Ranger = "ranger";

    /// <summary>The database role the apiary's host maps that token role to.</summary>
    public const string RangerRole = "apiary_ranger";

    public static readonly RowAccessRule Keepers = RowAccessRule.For<Hive>("Keepers have their hives", RowOperations.All, KeepersHaveTheirHives.RowAccessSql, RowAccessRoles.User);

    public static readonly RowAccessRule SignedIn = RowAccessRule.For<Hive>("Signed in callers read every hive", RowOperations.Read, SignedInCallersReadEveryHive.RowAccessSql, RowAccessRoles.User);

    public static readonly RowAccessRule Visitors = RowAccessRule.For<Hive>("Visitors read open hives", RowOperations.Read, VisitorsReadOpenHives.RowAccessSql, RowAccessRoles.Anonymous);

    /// <summary>A ranger, a token role the host mapped, reads every hive and changes none.</summary>
    public static readonly RowAccessRule Rangers = RowAccessRule.For<Hive>("Rangers read every hive", RowOperations.Read, "TRUE", RowAccessRoles.Token(Ranger));

    /// <summary>The rules the apiary runs with.</summary>
    public static readonly RowAccessRule[] All = [Keepers, SignedIn, Visitors, Rangers];

    /// <summary>A rule that asks an access function, and the function it asks: for the tests of functions that run as their owner.</summary>
    public static readonly RowAccessRule VisitorsOfHoney = RowAccessRule.For<Hive>("Visitors read hives with honey", RowOperations.Read, VisitorsReadHivesWithHoney.RowAccessSql, RowAccessRoles.Anonymous);

    public static readonly RowAccessFunction HasHoney = RowAccessFunction.For<Hive>("apiary.has_honey", HivesWithHoney.RowAccessSql);
}

/// <summary>
/// The apiary's tables: the hives and their boxes in the schema <c>apiary</c>, and the toolkit's outbox and
/// inbox in <c>ddd</c>. A hive's number is fixed once its row is added.
/// </summary>
public class ApiaryContext(DbContextOptions options) : DbContext(options)
{
    public const string Schema = "apiary";

    public DbSet<Hive> Hives => Set<Hive>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Hive>(hive =>
        {
            hive.Property(row => row.Number).IsFixedAfterInsert();
            hive.Property(row => row.Label).HasMaxLength(64);

            // Entities of an aggregate: Entity Framework maps them once it is told whose they are.
            hive.OwnsMany(row => row.Boxes);
        });

        modelBuilder.AddDomainEventOutbox(Database);
        modelBuilder.AddDomainEventInbox(Database);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }

    /// <summary>The apiary's model on Npgsql, for a script to be written from: it never connects.</summary>
    public static ApiaryContext ForScripts()
        => new(new DbContextOptionsBuilder<ApiaryContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
}
