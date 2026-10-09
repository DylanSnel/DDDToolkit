using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.TestHost.Converters;
using DDDToolkit.Supporting.Membership.TestHost.Crates;
using DDDToolkit.Supporting.Membership.TestHost.Depot;
using DDDToolkit.Supporting.Membership.TestHost.Pallets;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.TestHost.Persistence;

/// <summary>
/// The depot's context: the organization's own rows, and the two kinds of resource that stand beside it, each
/// with its members mapped by <c>HasMembers</c>. A crate says where it sits; a pallet sits nowhere. The depot
/// has this context to itself, next to the one the documents and the folders are in, so neither knows of
/// the other.
/// </summary>
/// <param name="options">The context's options.</param>
public sealed class DepotContext(DbContextOptions<DepotContext> options) : DbContext(options)
{
    /// <summary>The schema the tables are in.</summary>
    public const string Schema = "depot";

    /// <summary>The pallets.</summary>
    public DbSet<Pallet> Pallets => Set<Pallet>();

    /// <summary>The crates.</summary>
    public DbSet<Crate> Crates => Set<Crate>();

    /// <summary>The porters the depot took on.</summary>
    public DbSet<Porter> Porters => Set<Porter>();

    /// <summary>The roles the depot keeps.</summary>
    public DbSet<DepotRole> Roles => Set<DepotRole>();

    /// <summary>The keys the depot's roles give.</summary>
    public DbSet<DepotRoleKey> RoleKeys => Set<DepotRoleKey>();

    /// <summary>The bays.</summary>
    public DbSet<Bay> Bays => Set<Bay>();

    /// <summary>Every bay with each bay above it, and with itself.</summary>
    public DbSet<BayPath> BayPaths => Set<BayPath>();

    /// <summary>The keys porters hold at bays.</summary>
    public DbSet<BayHold> BayHolds => Set<BayHold>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Pallet>(pallet =>
        {
            pallet.Property(row => row.Id).ValueGeneratedNever();
            pallet.Property(row => row.Label).HasMaxLength(100);
            pallet.HasMembers(row => row.Porters, row => row.OwnerId);
        });

        modelBuilder.Entity<Crate>(crate =>
        {
            crate.Property(row => row.Id).ValueGeneratedNever();
            crate.Property(row => row.Label).HasMaxLength(100);

            // Where a crate sits is said with its members: what a key held at a bay, or above it, is compared with.
            crate.HasMembers(row => row.Porters, row => row.OwnerId, at: row => row.BayId);
        });

        modelBuilder.Entity<Porter>(porter =>
        {
            porter.Property(row => row.Id).ValueGeneratedNever();
            porter.HasIndex(row => row.UserId);
        });

        modelBuilder.Entity<DepotRole>(role =>
        {
            role.Property(row => row.Id).ValueGeneratedNever();
            role.Property(row => row.Name).HasMaxLength(64);
        });

        modelBuilder.Entity<DepotRoleKey>(given =>
        {
            given.HasKey(row => new { row.RoleId, row.Key });
            given.Property(row => row.Key).HasMaxLength(100);
        });

        modelBuilder.Entity<Bay>(bay => bay.Property(row => row.Id).ValueGeneratedNever());
        modelBuilder.Entity<BayPath>(path => path.HasKey(row => new { row.AboveId, row.BayId }));
        modelBuilder.Entity<BayHold>(hold =>
        {
            hold.HasKey(row => new { row.PorterId, row.BayId, row.Key });
            hold.Property(row => row.Key).HasMaxLength(100);
        });
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddFilingConverters();
    }
}
