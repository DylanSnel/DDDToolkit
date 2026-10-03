using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

// A pallet depot, to put what a database refuses against a real one: a pallet has a number that is unique in
// its depot, a label that is unique everywhere, and an owner, who alone changes it. The stamps on a pallet are
// its own entities, in a table of their own.

[EntityId<Guid>]
public readonly partial record struct PalletId;

[EntityId<Guid>]
public readonly partial record struct PalletStampId;

[EntityId<int>]
public readonly partial record struct DepotId;

[AggregateRoot<PalletId>]
public partial class Pallet
{
    public Pallet(PalletId id, DepotId depot, int number, string label, Guid? owner) : base(id)
    {
        Depot = depot;
        Number = number;
        Label = label;
        Owner = owner;
    }

    public DepotId Depot { get; private set; }

    public int Number { get; private set; }

    public string Label { get; private set; }

    public Guid? Owner { get; private set; }

    public void Retitle(string label) => Label = label;

    public void Renumber(int number) => Number = number;

    public void HandTo(Guid? owner) => Owner = owner;

    /// <summary>What was stamped on the pallet: entities of the aggregate, with no version of their own.</summary>
    public partial IReadOnlyList<PalletStamp> Stamps { get; }

    public void Stamp(string text) => _stamps.Add(new PalletStamp(PalletStampId.CreateSequential(), text));
}

[Entity<PalletStampId>]
public partial class PalletStamp
{
    public PalletStamp(PalletStampId id, string text) : base(id) => Text = text;

    public string Text { get; private set; }

    public void Reword(string text) => Text = text;
}

/// <summary>An owner does anything with their own pallets, and with nobody else's.</summary>
[RowAccess<Pallet>(RowOperations.All)]
public static partial class OwnersHaveTheirPallets
{
    public static bool Allows(Pallet pallet, Caller caller) => caller.IsSignedIn && pallet.Owner == caller.UserId;
}

/// <summary>Whoever signed in reads every pallet, so a pallet somebody may not change is still one they see.</summary>
[RowAccess<Pallet>(RowOperations.Read)]
public static partial class SignedInCallersReadEveryPallet
{
    public static bool Allows(Pallet pallet, Caller caller) => caller.IsSignedIn;
}

/// <summary>The depot's rules, each as the export takes it.</summary>
public static class PalletRules
{
    public static readonly RowAccessRule Owners = RowAccessRule.For<Pallet>("Owners have their pallets", RowOperations.All, OwnersHaveTheirPallets.RowAccessSql);

    public static readonly RowAccessRule SignedIn = RowAccessRule.For<Pallet>("Signed in callers read every pallet", RowOperations.Read, SignedInCallersReadEveryPallet.RowAccessSql);
}

/// <summary>
/// The depot's tables. A pallet's number is unique in its depot, and that index says what a save that breaks it
/// is refused with; its label is unique too, and that index says nothing.
/// </summary>
public class PalletContext(DbContextOptions options) : DbContext(options)
{
    public const string Schema = "depot";

    /// <summary>The refusal the index on a depot's pallet numbers answers.</summary>
    public const string NumberTaken = "pallets.number-taken";

    /// <summary>
    /// The text of <see cref="NumberTaken"/>: it names the two properties of the index, one of them an id with a
    /// converter, and one property of the row the index is not on.
    /// </summary>
    public const string NumberTakenText = "Depot {Depot} already has a pallet numbered {Number}, so '{Label}' cannot take it.";

    public DbSet<Pallet> Pallets => Set<Pallet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Pallet>(pallet =>
        {
            pallet.Property(row => row.Label).HasMaxLength(64);
            pallet.HasIndex(row => row.Label).IsUnique();
            Numbers(pallet.HasIndex(row => new { row.Depot, row.Number }).IsUnique());

            // Entities of an aggregate: Entity Framework maps them once it is told whose they are.
            pallet.OwnsMany(row => row.Stamps);
        });
    }

    /// <summary>What the index on the numbers is given besides being unique: the refusal a save that breaks it gets.</summary>
    protected virtual void Numbers(IndexBuilder<Pallet> index) => index.RefusesAs(NumberTaken, NumberTakenText);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }
}

/// <summary>The same depot with an index on the numbers that says nothing: what the tables were before the index was marked.</summary>
public sealed class UnmarkedPalletContext(DbContextOptions<UnmarkedPalletContext> options) : PalletContext(options)
{
    protected override void Numbers(IndexBuilder<Pallet> index)
    {
    }
}
