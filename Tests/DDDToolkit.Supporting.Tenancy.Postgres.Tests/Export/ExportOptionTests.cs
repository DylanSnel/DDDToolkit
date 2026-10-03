using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// What an export may turn on, for Tenancy's tables: the privileges written from the policies, which are then
/// exactly what the use cases need and nothing more; forced policies; and, where the host has a role for its own
/// bookkeeping, that role's way to the old rows of the access history.
/// </summary>
public sealed class ExportOptionTests(TenancyPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenancys_tables_are_forced_when_the_export_asks()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation);
        const string Forced =
            "SELECT pg_catalog.quote_ident(n.nspname) || '.' || pg_catalog.quote_ident(c.relname) FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE n.nspname IN ('tenancy', 'widgets', 'ddd') AND c.relkind = 'r' AND c.relforcerowsecurity ORDER BY 1";
        string[] secured =
        [
            "ddd.\"EventLog\"", "tenancy.\"InvitationDigests\"", "tenancy.\"Invitations\"",
            "tenancy.\"OrganizationUnitPaths\"", "tenancy.\"OrganizationUnits\"", "tenancy.\"Organizations\"", "tenancy.\"Roles\"", "tenancy.\"SeatPlacements\"",
            "tenancy.\"SeatRights\"", "tenancy.\"SeatRoleGrants\"", "tenancy.\"Seats\"", "tenancy.\"TenancyAccessRevisions\"", "tenancy.\"TenantNote\"", "tenancy.\"Tenants\"",
            "widgets.\"WidgetPart\"", "widgets.\"Widgets\"",
        ];

        // As an export writes them by default: row level security on, and the tables' owner exempt.
        foreach (var script in TenancyPostgres.AccessScripts())
        {
            script.Should().NotContain("FORCE ROW LEVEL SECURITY");
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ListAsync<string>(Forced, Cancellation)).Should().BeEmpty();
        }

        // Asked to, every table Tenancy's contribution secures is forced with the rest: its own, the table of the
        // application's entity on a tenant, the history, and the module's tables kept to a tenant.
        foreach (var script in TenancyPostgres.AccessScripts(export: written => new RowAccessExport { Roles = written.Roles, Contributions = written.Contributions, ForceRowLevelSecurity = true }))
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await using var after = await AsCaller.OwnerAsync(database, Cancellation);
        (await after.ListAsync<string>(Forced, Cancellation)).Should().Equal(secured);
    }

    [Fact]
    public async Task The_privileges_written_from_the_policies_let_every_use_case_run()
    {
        // A database whose tables give callers nothing but what the access files grant: the template's own grants
        // are taken back by the files, table by table, before they give each role what its policies allow.
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation);
        foreach (var script in TenancyPostgres.AccessScripts(export: written => new RowAccessExport { Roles = written.Roles, Contributions = written.Contributions, WriteGrants = true }))
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        // Seeded through the use cases, as system work and by the widgets' module; then every command of the use
        // cases, each as a seat that may run it. A privilege short of a policy would fail one of them.
        await using var services = new TenancyServices(database);
        await TenancySeed.SeedAsync(services, await TenancySeed.DatabaseNowAsync(database.ConnectionString, Cancellation), Cancellation);
        await EveryUseCase.RunAsync(services, Cancellation);
        (await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Directory().WhoAmIAsync(Cancellation))).Seat.Id.Should().Be(Ada.Seat);

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        async Task<bool> MayAsync(string role, string table, string privilege)
            => await owner.ScalarAsync<bool>($"SELECT pg_catalog.has_table_privilege('{role}', '{table}', '{privilege}')", Cancellation);
        async Task<bool> MayChangeAsync(string role, string table, string column)
            => await owner.ScalarAsync<bool>($"SELECT pg_catalog.has_column_privilege('{role}', '{table}', '{column}', 'UPDATE')", Cancellation);

        // And nothing more. The rights are the database's to write: every caller reads them, and none writes one.
        foreach (var role in new[] { "authenticated", "ddd_system_in" })
        {
            (await MayAsync(role, "tenancy.\"SeatRights\"", "SELECT")).Should().BeTrue();
            foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE" })
            {
                (await MayAsync(role, "tenancy.\"SeatRights\"", privilege)).Should().BeFalse($"{role} writes no right");
            }

            // The history takes rows and gives them back, and changes for nobody.
            (await MayAsync(role, "ddd.\"EventLog\"", "INSERT")).Should().BeTrue();
            (await MayAsync(role, "ddd.\"EventLog\"", "SELECT")).Should().BeTrue();
            (await MayAsync(role, "ddd.\"EventLog\"", "UPDATE")).Should().BeFalse();
            (await MayAsync(role, "ddd.\"EventLog\"", "DELETE")).Should().BeFalse();

            // The tenant of a row is fixed once the row is there: its column is left out of what may change.
            (await MayChangeAsync(role, "tenancy.\"Seats\"", "DisplayName")).Should().BeTrue();
            (await MayChangeAsync(role, "tenancy.\"Seats\"", "TenantId")).Should().BeFalse($"{role} moves no seat to another tenant");
            (await MayChangeAsync(role, "tenancy.\"Roles\"", "TenantId")).Should().BeFalse();
            (await MayChangeAsync(role, "widgets.\"Widgets\"", "TenantId")).Should().BeFalse();
            (await MayChangeAsync(role, "widgets.\"Widgets\"", "Name")).Should().BeTrue();

            // The digest of an invitation's token is added with its invitation, and read back by nobody.
            (await MayAsync(role, "tenancy.\"InvitationDigests\"", "INSERT")).Should().BeTrue();
            foreach (var privilege in new[] { "SELECT", "UPDATE", "DELETE" })
            {
                (await MayAsync(role, "tenancy.\"InvitationDigests\"", privilege)).Should().BeFalse($"{role} reads no digest of a token, and changes none");
            }

            // What an invitation offers is fixed once it is there: only how it ended may change.
            (await MayChangeAsync(role, "tenancy.\"Invitations\"", "State")).Should().BeTrue();
            foreach (var column in new[] { "TenantId", "UnitId", "RoleId", "GrantUntil", "ExpiresAt", "IssuedBy", "IssuedAsSystem" })
            {
                (await MayChangeAsync(role, "tenancy.\"Invitations\"", column)).Should().BeFalse($"{role} changes no {column} of an invitation");
            }
        }

        (await MayAsync("authenticated", "tenancy.\"Tenants\"", "INSERT")).Should().BeFalse("only system work provisions a tenant");
        (await MayAsync("ddd_system_in", "tenancy.\"Tenants\"", "INSERT")).Should().BeTrue();
        foreach (var table in new[]
                 {
                     "tenancy.\"Tenants\"", "tenancy.\"Seats\"", "tenancy.\"SeatRights\"", "tenancy.\"Invitations\"", "tenancy.\"InvitationDigests\"",
                     "widgets.\"Widgets\"", "ddd.\"EventLog\"", "ddd.\"OutboxMessages\"",
                 })
        {
            (await MayAsync("anon", table, "SELECT")).Should().BeFalse($"an anonymous caller is given nothing on {table}");
        }
    }

    [Fact]
    public void The_bookkeeping_role_removes_old_history_where_the_table_lets_rows_go()
    {
        const string Keeper = "tenancy_keeper";
        static string Script(DbContext context, RowAccessExport export)
            => string.Concat(PostgresRowAccess.Scripts([context], [], [], export).Select(each => each.Script));
        static RowAccessExport Export(bool grants, string? system)
            => new()
            {
                Roles = RowAccessRoleNames.Default with { System = system },
                Contributions = [new TenancyRowAccessContribution(TenancyPostgres.Catalogue)],
                WriteGrants = grants,
            };

        using var kept = new KeptHistoryContext(new DbContextOptionsBuilder<KeptHistoryContext>().UseNpgsql("Host=model-only").Options);
        using var forGood = TenancyPostgres.TenancyModel();

        // The export writes privileges, the host has a role for its bookkeeping, and the table lets a row go after
        // a year: that role finds and removes rows, across tenants, and the guard lets only the old ones go.
        var script = Script(kept, Export(grants: true, Keeper));
        script.Should().Contain(
            $"CREATE POLICY \"Bookkeeping removes old rows (delete) for {Keeper}\" ON ddd.\"EventLog\" FOR DELETE TO {Keeper}\n" +
            "    USING (true);\n");
        script.Should().Contain($"CREATE POLICY \"Bookkeeping removes old rows (select) for {Keeper}\" ON ddd.\"EventLog\" FOR SELECT TO {Keeper}\n");
        script.Should().Contain($"GRANT SELECT, DELETE ON TABLE ddd.\"EventLog\" TO {Keeper};\n");
        script.Should().NotContain($"(insert) for {Keeper}").And.NotContain($"(update) for {Keeper}");

        // Without any one of the three, the role is given nothing on the history.
        Script(kept, Export(grants: false, Keeper)).Should().NotContain("Bookkeeping removes old rows");
        Script(kept, Export(grants: true, system: null)).Should().NotContain("Bookkeeping removes old rows");
        Script(forGood, Export(grants: true, Keeper)).Should().NotContain("Bookkeeping removes old rows", "a history kept for good lets no row go");
    }

    [Fact]
    public void A_history_mapped_without_tenancys_tables_is_refused_by_the_export()
    {
        // The history's policies ask Tenancy's seats who is writing, so they are written where the seats are. In a
        // module's context the table would get no policy at all, and every tenant's rows would be read alike.
        using var stray = new StrayHistoryContext(new DbContextOptionsBuilder<StrayHistoryContext>().UseNpgsql("Host=model-only").Options);

        FluentActions.Invoking(() => TenancyPostgres.AccessScripts(more: [stray]))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("StrayHistoryContext maps Tenancy's access history, with AddTenancyEventLogTable, and none of Tenancy's tables.*Map it in the context that calls AddTenancy; a module keeps its own events with AddEventLog.");

        // The toolkit's own log is a module's to map, and nothing of Tenancy's.
        using var plain = new PlainLogContext(new DbContextOptionsBuilder<PlainLogContext>().UseNpgsql("Host=model-only").Options);
        FluentActions.Invoking(() => TenancyPostgres.AccessScripts(more: [plain])).Should().NotThrow();
    }

    /// <summary>A module's context that maps Tenancy's access history, which belongs with Tenancy's tables.</summary>
    private sealed class StrayHistoryContext(DbContextOptions<StrayHistoryContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("strays");
            modelBuilder.AddTenancyEventLogTable<TenantId>(Database, tableName: "StrayLog", schema: "strays");
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
            configurationBuilder.StoreDateTimeOffsetsAsUtc();
        }
    }

    /// <summary>A module's context with the toolkit's own event log, which carries no tenant.</summary>
    private sealed class PlainLogContext(DbContextOptions<PlainLogContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("strays");
            modelBuilder.AddEventLog(Database, tableName: "PlainLog", schema: "strays");
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.StoreDateTimeOffsetsAsUtc();
        }
    }

    /// <summary>The application's tenancy context with a history that lets a row go after a year.</summary>
    private sealed class KeptHistoryContext(DbContextOptions<KeptHistoryContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(TestTenancyContext.Schema);
            modelBuilder.AddTenancy<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>(database: Database);
            modelBuilder.AddDomainEventOutbox(Database);
            modelBuilder.AddTenancyEventLogTable<TenantId>(Database, keepFor: TimeSpan.FromDays(365));
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
            configurationBuilder.StoreDateTimeOffsetsAsUtc();
        }
    }
}
