using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.TestHost.Converters;
using DDDToolkit.Supporting.Membership.TestHost.Gardens;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.TestHost.Persistence;

/// <summary>
/// The gardens' context: two kinds of resource whose roles are kept, each with its members mapped by
/// <c>HasMembers</c> and its role class by <c>IsKeptRole</c>. Everything about whose a role of plots is, is
/// the host's own and said here: the column, the two indexes over it, and the rule that a request reads the
/// roles of the garden it is in. The roles of sheds are the whole application's, so nothing of the kind is
/// said for them. The gardens have this context to themselves, so the documents, the folders and the depot
/// know nothing of it.
/// </summary>
/// <param name="options">The context's options.</param>
public sealed class GardenContext(DbContextOptions<GardenContext> options) : DbContext(options)
{
    /// <summary>The schema the tables are in.</summary>
    public const string Schema = "gardens";

    /// <summary>The plots.</summary>
    public DbSet<Plot> Plots => Set<Plot>();

    /// <summary>The roles of plots, of the garden the request is in.</summary>
    public DbSet<PlotRole> PlotRoles => Set<PlotRole>();

    /// <summary>The sheds.</summary>
    public DbSet<Shed> Sheds => Set<Shed>();

    /// <summary>The roles of sheds: one set, for the whole application.</summary>
    public DbSet<ShedRole> ShedRoles => Set<ShedRole>();

    /// <summary>
    /// The garden this context's request is in, for a host that keeps it on the request's own context and not
    /// around the work: set where a request begins. A context a factory makes for one reading knows nothing
    /// of it, which is why what must follow the host's rule is read on the request's context.
    /// </summary>
    public GardenId? GardenOfThisRequest { get; set; }

    /// <summary>Whether the work that is running is in a garden: read where a query runs, so one context serves any request.</summary>
    private bool InAGarden => (GardenOfThisRequest ?? GardenOfTheRequest.Current) is not null;

    /// <summary>The garden the work that is running is in.</summary>
    private GardenId Garden => GardenOfThisRequest ?? GardenOfTheRequest.Current ?? default;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Plot>(plot =>
        {
            plot.Property(row => row.Id).ValueGeneratedNever();
            plot.Property(row => row.Name).HasMaxLength(100);
            plot.HasMembers(row => row.Gardeners, row => row.OwnerId);
        });

        modelBuilder.Entity<PlotRole>(role =>
        {
            // The package's part of a role: its name, what it is for, its keys, its status, and the starter role it came from.
            role.IsKeptRole();

            // The host's part. A name is used once in a garden, and a garden has each starter role once: two
            // indexes over the host's own column, the first refusing in the host's own code.
            role.HasIndex(row => new { row.GardenId, row.Name }).IsUnique().RefusesAs(PlotMembership.RoleNameTaken, "Another role of this garden is called {Name} already.");
            role.HasIndex(row => new { row.GardenId, row.MadeFrom }).IsUnique();

            // And the host's rule: a request in a garden reads that garden's roles, and no other's.
            role.HasQueryFilter(row => !InAGarden || row.GardenId == Garden);
        });

        modelBuilder.Entity<Shed>(shed =>
        {
            shed.Property(row => row.Id).ValueGeneratedNever();
            shed.Property(row => row.Name).HasMaxLength(100);
            shed.HasMembers(row => row.Hands, row => row.OwnerId);
        });

        modelBuilder.Entity<ShedRole>(role =>
        {
            role.IsKeptRole();

            // One set of roles for the whole application: a name once, and each starter role once, with no column in front.
            role.HasIndex(row => row.Name).IsUnique();
            role.HasIndex(row => row.MadeFrom).IsUnique();
        });
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddFilingConverters();
    }
}
