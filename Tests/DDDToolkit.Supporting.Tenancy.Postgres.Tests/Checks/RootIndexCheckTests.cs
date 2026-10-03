using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// On Postgres a host starts only on a database that has the unique index on a tenant's root. The policies keep a
/// seat from making a second root; Tenancy's own system work and the tables' owner they do not hold, and a key
/// held at any unit without a parent is held for the whole tenant. So the start-up check that proves the policies
/// are in place looks for the index, by what it does and whatever it is called, and says how to get it: by the
/// migration, where the model maps it, and by passing the context's <c>Database</c> to <c>AddTenancy</c>, where it
/// does not.
/// </summary>
public abstract class RootIndexCheckTests(TenancyPostgres postgres, TenancyNaming names)
{
    /// <summary>The index as <c>AddTenancy</c> names it, under the default names.</summary>
    private const string RootIndex = "IX_OrganizationUnits_TenantId_WhereRoot";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>What the check says of a database without the index, up to what it says of the model.</summary>
    private string Missing =>
        "The database has no unique index that keeps a tenant's organization to a single root: none in use on " +
        $"tenancy.{names.Shown("OrganizationUnits")} over {names.Shown("TenantId")} alone, for the units where {names.Shown("ParentId")} IS NULL. " +
        "On Postgres that index is required: the policies keep a seat from making a second root, and hold neither Tenancy's own system work nor the tables' owner, " +
        "while a key held at any unit without a parent is held for the whole tenant. ";

    [Fact]
    public async Task A_database_without_the_unique_index_on_a_tenants_root_is_refused_at_start_up()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);

        // The model maps the index and the database lost it, or never got it: the migration is what is missing.
        var behind = Missing + $"The model maps it, as {names.Of(RootIndex)}, so the database is behind the model: add a migration, where none makes the index yet, and apply it.";
        await ExecuteAsync(database, $"DROP INDEX tenancy.\"{names.Of(RootIndex)}\"");
        await RefusedAsync(services.Provider, behind);

        // An index that does not keep a tenant to a single root is not taken for the one that does.
        foreach (var (statement, why) in new[]
                 {
                     ("CREATE INDEX not_unique ON tenancy.\"OrganizationUnits\" (\"TenantId\") WHERE \"ParentId\" IS NULL", "it refuses nothing"),
                     ("CREATE UNIQUE INDEX wider_than_the_tenant ON tenancy.\"OrganizationUnits\" (\"TenantId\", \"Name\") WHERE \"ParentId\" IS NULL", "two roots of different names both fit it"),
                     ("CREATE UNIQUE INDEX only_active_roots ON tenancy.\"OrganizationUnits\" (\"TenantId\") WHERE \"ParentId\" IS NULL AND \"Status\" = 'Active'", "an archived root is a unit without a parent all the same"),
                     ("CREATE UNIQUE INDEX on_another_column ON tenancy.\"OrganizationUnits\" (\"Id\") WHERE \"ParentId\" IS NULL", "it is not on the tenant"),
                 })
        {
            await ExecuteAsync(database, statement);
            await RefusedAsync(services.Provider, behind, why);
        }

        // The index under a name of the host's own, made by a migration it wrote itself, is the index.
        await ExecuteAsync(database, "CREATE UNIQUE INDEX units_keep_a_single_root ON tenancy.\"OrganizationUnits\" (\"TenantId\") WHERE \"ParentId\" IS NULL");
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);

        // One Postgres does not use refuses nothing, as CREATE INDEX CONCURRENTLY leaves an index it could not
        // finish, over two roots that were there already say. The check names it, and built again, it counts.
        await TenancyPostgres.ExecuteAsync(
            database.SuperuserConnectionString,
            "UPDATE pg_catalog.pg_index SET indisvalid = false WHERE indexrelid = 'tenancy.units_keep_a_single_root'::pg_catalog.regclass",
            Cancellation);
        await RefusedAsync(
            services.Provider,
            Missing + "The index tenancy.units_keep_a_single_root is there, and Postgres does not use it: it is not valid, as CREATE INDEX CONCURRENTLY leaves an index it could not finish. " +
            "See that no tenant has two roots, and build it again: REINDEX INDEX tenancy.units_keep_a_single_root;");
        await ExecuteAsync(database, "REINDEX INDEX tenancy.units_keep_a_single_root;");
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);
    }

    [Fact]
    public async Task A_host_that_maps_tenancy_without_the_database_is_told_to_pass_it()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // A host whose context calls AddTenancy without the context's Database: its model has Tenancy's tables and
        // neither filtered index. The check reads the database, which has the index from whoever made it.
        await using var host = HostWithoutTheIndexes(database);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(host, Cancellation);

        // A database made from that model has none, and no migration of this host would ever make one.
        await ExecuteAsync(database, $"DROP INDEX tenancy.\"{names.Of(RootIndex)}\"");
        await RefusedAsync(
            host,
            Missing + "The model maps no such index either: pass the context's Database to AddTenancy, modelBuilder.AddTenancy(database: Database), which maps it, then add a migration and apply it.");
    }

    /// <summary>Runs <paramref name="statement"/>, written in the default names, as the tables' owner, in the names of <paramref name="database"/>.</summary>
    private static Task ExecuteAsync(TestDatabase database, string statement)
        => TenancyPostgres.ExecuteAsync(database.ConnectionString, database.Names.Sql(statement), Cancellation);

    private static async Task RefusedAsync(IServiceProvider host, string message, string because = "")
        => await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(host, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>(because).WithMessage(message);

    /// <summary>
    /// The services of a host that registers Tenancy, row level security and Tenancy on Postgres as any other does,
    /// over a context that maps Tenancy's tables without the two filtered indexes.
    /// </summary>
    private static ServiceProvider HostWithoutTheIndexes(TestDatabase database)
    {
        var services = new ServiceCollection();
        TestHostTenancy.Add(services);
        services.AddPostgresRowLevelSecurity();
        services.AddTenancyPostgres();
        services.AddDbContext<TenancyWithoutItsIndexes>((provider, options)
            => database.Names.Configure(options.UseNpgsql(database.ConnectionString)).UsePostgresRowLevelSecurity(provider));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>
    /// The application's tenancy context as a host maps it that leaves the context's <c>Database</c> out of
    /// <c>AddTenancy</c>: Tenancy's tables, named as the host names them, and neither filtered index.
    /// </summary>
    private sealed class TenancyWithoutItsIndexes(DbContextOptions<TenancyWithoutItsIndexes> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(TestTenancyContext.Schema);
            modelBuilder.AddTenancy<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>(TenancyNaming.For(this).Tables);
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
            configurationBuilder.StoreDateTimeOffsetsAsUtc();
        }
    }
}

/// <summary>The index on a tenant's root is required, under the names Entity Framework gives the tables and columns.</summary>
public sealed class RootIndexCheckTestsOnDefaultNames(TenancyPostgres postgres) : RootIndexCheckTests(postgres, TenancyNaming.Default);

/// <summary>The index on a tenant's root is required, under snake_case names with enums stored as snake_case text.</summary>
public sealed class RootIndexCheckTestsOnSnakeCase(TenancyPostgres postgres) : RootIndexCheckTests(postgres, TenancyNaming.SnakeCase);
