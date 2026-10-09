using DDDToolkit.EntityFramework.Conventions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>A numbered docket on a shelf: nothing of the toolkit's but the mapping, so the provider's own failure is all there is to read.</summary>
public sealed class Docket
{
    public Guid Id { get; set; }

    public string Shelf { get; set; } = string.Empty;

    public int Number { get; set; }

    /// <summary>Unique too, through an alternate key: a constraint, where the number is an index.</summary>
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// The dockets' table, beside the suite's own in the test's database. A docket's number is unique on its
/// shelf, and that index says what a save that breaks it is refused with; its code is unique through an
/// alternate key, which says nothing, because only an index can.
/// </summary>
public sealed class DocketContext(DbContextOptions<DocketContext> options) : DbContext(options)
{
    /// <summary>The refusal the index on a shelf's docket numbers answers.</summary>
    public const string NumberTaken = "dockets.number-taken";

    public DbSet<Docket> Dockets => Set<Docket>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Docket>(docket =>
        {
            docket.ToTable("Dockets");
            docket.Property(row => row.Id).ValueGeneratedNever();
            docket.Property(row => row.Shelf).HasMaxLength(40);
            docket.Property(row => row.Code).HasMaxLength(40);
            docket.HasAlternateKey(row => row.Code);
            docket.HasIndex(row => new { row.Shelf, row.Number })
                .IsUnique()
                .RefusesAs(NumberTaken, "Shelf {Shelf} already has a docket numbered {Number}.");
        });

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.AddDDDToolkitConventions();
}
