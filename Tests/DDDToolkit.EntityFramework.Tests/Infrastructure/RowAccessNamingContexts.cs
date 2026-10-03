using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

// Contexts for what a script refuses or works around when it names functions and reads keys: two modules that
// give their contexts no schema, so their tables and their functions all land in public; a module whose schema
// has a capital letter, which Postgres keeps only when it is quoted; and an aggregate whose entities hold another
// key of it than its primary key.

public class Memo
{
    public Guid Id { get; set; }
}

public class Bookmark
{
    public Guid Id { get; set; }
}

/// <summary>A module whose context names no schema: its table is in <c>public</c>, and so is every function it writes.</summary>
public sealed class MemosContext(DbContextOptions<MemosContext> options) : DbContext(options)
{
    public DbSet<Memo> Memos => Set<Memo>();

    /// <summary>A context on nothing, for a script, which never connects.</summary>
    public static MemosContext Create() => new(new DbContextOptionsBuilder<MemosContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
}

/// <summary>Another module whose context names no schema.</summary>
public sealed class BookmarksContext(DbContextOptions<BookmarksContext> options) : DbContext(options)
{
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    /// <summary>A context on nothing, for a script, which never connects.</summary>
    public static BookmarksContext Create() => new(new DbContextOptionsBuilder<BookmarksContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
}

/// <summary>A module whose schema is <c>"Memos"</c>, which Entity Framework creates quoted.</summary>
public sealed class CapitalizedMemosContext(DbContextOptions<CapitalizedMemosContext> options) : DbContext(options)
{
    public DbSet<Memo> Memos => Set<Memo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema("Memos");

    /// <summary>A context on nothing, for a script, which never connects.</summary>
    public static CapitalizedMemosContext Create() => new(new DbContextOptionsBuilder<CapitalizedMemosContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
}

public class Parcel
{
    public Guid Id { get; set; }

    public int Number { get; set; }

    public List<ParcelWatcher> Watchers { get; set; } = [];
}

public class ParcelWatcher
{
    public Guid? User { get; set; }
}

/// <summary>
/// Parcels whose watchers hold the parcel's number, an alternate key, rather than its id: the watchers' table
/// alone cannot say which parcels' ids they watch.
/// </summary>
public sealed class ParcelsContext(DbContextOptions<ParcelsContext> options) : DbContext(options)
{
    public DbSet<Parcel> Parcels => Set<Parcel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("parcels");
        modelBuilder.Entity<Parcel>().HasAlternateKey(parcel => parcel.Number);
        modelBuilder.Entity<Parcel>().OwnsMany(parcel => parcel.Watchers, watcher => watcher.WithOwner().HasPrincipalKey(parcel => parcel.Number).HasForeignKey("ParcelNumber"));
    }

    /// <summary>A context on nothing, for a script, which never connects.</summary>
    public static ParcelsContext Create() => new(new DbContextOptionsBuilder<ParcelsContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
}
