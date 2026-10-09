using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

// A yard of crates, for the policies of a hierarchy Entity Framework maps to one table: a crate, and an
// express crate, a kind of crate, in the same table and told apart by its discriminator, with the stickers
// of either in a table of their own.

public class Crate
{
    public Guid Id { get; set; }

    public Guid? Owner { get; set; }

    public List<CrateSticker> Stickers { get; set; } = [];
}

public class ExpressCrate : Crate
{
    public bool Insured { get; set; }
}

public class CrateSticker
{
    public Guid Id { get; set; }

    public string Text { get; set; } = "";
}

public sealed class YardContext(DbContextOptions<YardContext> options) : DbContext(options)
{
    public DbSet<Crate> Crates => Set<Crate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("yard");
        modelBuilder.Entity<Crate>().OwnsMany(crate => crate.Stickers);
        modelBuilder.Entity<ExpressCrate>();
    }

    /// <summary>A context on <paramref name="connectionString"/>, or on nothing for a script, which never connects.</summary>
    public static YardContext Create(string connectionString = "Host=nowhere.invalid;Database=unused")
        => new(new DbContextOptionsBuilder<YardContext>().UseNpgsql(connectionString).Options);
}

/// <summary>The yard's rules: one about every crate, and one about express crates only, which reads a column every crate has.</summary>
public static class YardRules
{
    public static readonly RowAccessRule Owners = RowAccessRule.For<Crate>(
        "Owners have their crates", RowOperations.All, "({caller:signedin} AND ({col:Owner} IS NOT DISTINCT FROM {caller:uid}))");

    public static readonly RowAccessRule OwnerlessExpress = RowAccessRule.For<ExpressCrate>(
        "Ownerless express crates are open", RowOperations.Read | RowOperations.Change, "({col:Owner} IS NULL)");
}
