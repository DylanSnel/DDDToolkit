using System.Globalization;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Provisioning uses no power over other tenants: once it has the new tenant's id it reads and writes as system work in
/// that tenant, under the policies, so it writes that tenant's rows and nothing else; and a slug another tenant took,
/// which it cannot see from there, is refused by the unique index, with nothing of the new tenant left behind. An
/// administrators' pack that lists its keys gives the first administrator those keys and no others, and the database
/// says the same of the pack as the catalogue does, which is what lets a settings manager add its role when a tenant
/// changes shape. Under what name a role is made from a pack is not the database's to say: a tenant provisioned in a
/// language gets its roles named in it, and a settings manager adds a pack's role under its translated name.
/// </summary>
public abstract class ProvisioningTests(TenancyPostgres postgres, TenancyNaming names)
{
    private static readonly TenantId Estuary = new(40);

    /// <summary>The pack of a hierarchical tenant's administrators in <see cref="ListingAdministrators"/>.</summary>
    private const string TreeAdministrators = "tree-admin";

    /// <summary>
    /// What that pack lists: Tenancy's own keys, which are every key that manages access in the TestHost's catalogue,
    /// and no key to work with widgets.
    /// </summary>
    private static readonly string[] Listed = [.. TenancyKeys.Permissions.Select(permission => permission.Key).Order(StringComparer.Ordinal)];

    private static readonly string[] Widgets = [HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead];

    /// <summary>
    /// The TestHost's catalogue with an administrators' pack for each shape: a flat tenant's lists nothing and holds
    /// every live key, as the TestHost's does, and a hierarchical tenant's lists <see cref="Listed"/>.
    /// </summary>
    private static ApplicationCatalogue ListingAdministrators { get; } = HostCatalogue.Application with
    {
        Packs =
        [
            .. HostCatalogue.Application.Packs.Select(pack => pack.Administers ? pack with { Shape = TenantShape.Flat } : pack),
            new RolePack(TreeAdministrators, "Tree administrator", "Runs access across the tree", Listed, Shape: TenantShape.Hierarchical, Administers: true, Order: 15),
        ],
    };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Provisioning_writes_only_the_new_tenant_as_system_in()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var before = await RowsByTenantAsync(database);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder));

        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            await services.InScopeAsync(scoped => scoped.Tenants().ProvisionAsync(
                new HostTenancy.TenantToProvision("estuary", "Estuary Works", TenantShape.Hierarchical, "Estuary", "company", Guid.NewGuid(), "Dan", TenantId: Estuary),
                Cancellation));
        }

        // Every read and write it made, it made as system work in the new tenant, in Tenancy's own scope.
        recorder.Sent.Should().NotBeEmpty();
        foreach (var command in recorder.Sent)
        {
            command.Caller!.Kind.Should().Be(CallerKind.SystemIn, "provisioning narrows itself to the tenant it makes");
            command.Caller.Scope.Should().Be(TenancyWork.SystemScope);
            command.TenancyCaller.Should().Be(HostCaller.SystemIn(Estuary), command.Text);
        }

        recorder.Sent.Should().Contain(command => command.Text.Contains("INSERT INTO tenancy." + names.Shown("Tenants"), StringComparison.Ordinal));

        // It wrote the new tenant's rows, and no other tenant's.
        var after = await RowsByTenantAsync(database);
        after.Where(row => row.Key.Tenant != Estuary.Value).Should().BeEquivalentTo(before, "no other tenant's rows changed in number");
        after.Keys.Where(key => key.Tenant == Estuary.Value).Select(key => key.Table)
            .Should().BeEquivalentTo(["Tenants", "Organizations", "OrganizationUnits", "OrganizationUnitPaths", "Seats", "SeatPlacements", "SeatRights", "Roles", "TenancyAccessRevisions"]);
    }

    [Fact]
    public async Task Provisioning_a_taken_slug_is_refused_by_the_index()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var before = await RowsByTenantAsync(database);
        await using var services = new TenancyServices(database);

        // From inside the new tenant, the check that the slug is free sees no other tenant's slug: the index sees them all.
        RefusalException refusal;
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            refusal = (await FluentActions.Awaiting(() => services.InScopeAsync(scoped => scoped.Tenants().ProvisionAsync(
                    new HostTenancy.TenantToProvision("orchard", "Second Orchard", TenantShape.Flat, "Orchard", "company", Guid.NewGuid(), "Dan", TenantId: Estuary),
                    Cancellation)))
                .Should().ThrowAsync<RefusalException>()).Which;
        }

        refusal.Code.Should().Be(TenancyRefusals.SlugTaken);
        var failure = refusal.InnerException;
        while (failure is not null and not PostgresException)
        {
            failure = failure.InnerException;
        }

        var postgresFailure = failure.Should().BeOfType<PostgresException>("the refusal stands for what Postgres refused").Subject;
        postgresFailure.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        postgresFailure.ConstraintName.Should().Be(names.Of("IX_Tenants_Slug"));

        (await RowsByTenantAsync(database)).Should().BeEquivalentTo(before, "nothing of the new tenant is left behind");
    }

    [Fact]
    public async Task Provisioning_an_administrators_pack_that_lists_its_keys_gives_those_keys_and_no_others()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = await ListingServicesAsync(database);

        var dan = Guid.NewGuid();
        var provisioned = await ProvisionEstuaryAsync(services, TenantShape.Hierarchical, dan);

        // The role is a copy of its pack, and the database wrote the first administrator's rights from the role: the
        // keys the pack lists, at the root, and no key to work with widgets, though each of those is live.
        provisioned.AdministratorRole.Should().Be(provisioned.RolesByPack[TreeAdministrators]);
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ListAsync<string>("SELECT pg_catalog.unnest(\"Keys\") FROM tenancy.\"Roles\" WHERE \"Id\" = $1", Cancellation, provisioned.AdministratorRole.Value))
                .Should().BeEquivalentTo(Listed);
            (await owner.ListAsync<string>("SELECT \"Key\" FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, provisioned.AdminSeat.Value))
                .Should().BeEquivalentTo(Listed, "each key once, at the root");
            foreach (var key in Widgets)
            {
                (await owner.ScalarAsync<bool>("SELECT tenancy.key_is_live($1)", Cancellation, key)).Should().BeTrue();
            }
        }

        // It is an administrator all the same, under the policies as in the use cases: it gives a role that manages
        // access, whose keys that do it holds, and a role to work with widgets, none of whose keys it holds.
        var fay = await services.BySeat(dan, Estuary, provisioned.AdminSeat, scoped => scoped.Seats().AddSeatAsync(Guid.NewGuid(), "Fay", Cancellation));
        await services.BySeat(dan, Estuary, provisioned.AdminSeat, scoped => scoped.Seats().PlaceAsync(fay, provisioned.RootUnit, primary: true, Cancellation));
        foreach (var pack in new[] { HostCatalogue.SupervisorPack, HostCatalogue.OperatorPack })
        {
            await services.BySeat(dan, Estuary, provisioned.AdminSeat,
                scoped => scoped.Seats().GrantAsync(fay, provisioned.RootUnit, provisioned.RolesByPack[pack], until: null, reason: null, Cancellation));
        }

        // What the database answers the administrator is what the pack lists.
        await using (var asDan = await AsCaller.PersonAsync(database, dan, Estuary, Cancellation))
        {
            foreach (var key in Listed)
            {
                (await asDan.ScalarAsync<bool>("SELECT tenancy.holds_tenant_wide($1)", Cancellation, key)).Should().BeTrue("the pack lists {0}", key);
            }

            foreach (var key in Widgets)
            {
                (await asDan.ScalarAsync<bool>("SELECT tenancy.holds_key($1)", Cancellation, key)).Should().BeFalse("the pack does not list {0}", key);
            }
        }

        string[] given = [TenancyKeys.GrantsManage, TenancyKeys.SeatsManage, TenancyKeys.UnitsManage, .. Widgets];
        await using var after = await AsCaller.OwnerAsync(database, Cancellation);
        (await after.ListAsync<string>("SELECT DISTINCT \"Key\" FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, fay.Value))
            .Should().BeEquivalentTo(given, "the two roles' keys reached the seat they were given to");
    }

    [Fact]
    public async Task A_catalogue_that_declares_no_packs_writes_the_default_administrators_pack_and_gives_its_role_to_the_first_seat()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var noPacks = HostCatalogue.Application with { Packs = [] };
        var catalogue = TenancyCatalogue.Build(noPacks, []);

        // The access file written from that catalogue has the default pack, and the start-up check finds the database
        // written from the catalogue the application runs with.
        string.Concat(TenancyPostgres.AccessScripts(catalogue, names: names)).Should().Contain("WHEN 'administrator' THEN ARRAY[");
        await using var services = await ServicesWithAsync(database, noPacks);

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ScalarAsync<string[]>("SELECT tenancy.pack_keys($1)", Cancellation, TenancyPacks.DefaultAdministratorsKey))
                .Should().Equal(catalogue.LiveKeys, "the default administrators' pack holds every live key, as one that lists none does");
            (await owner.ScalarAsync<bool>("SELECT tenancy.pack_keys($1) IS NULL", Cancellation, HostCatalogue.AdministratorPack))
                .Should().BeTrue("the TestHost's own packs are not in this catalogue");
        }

        var dan = Guid.NewGuid();
        var provisioned = await ProvisionEstuaryAsync(services, TenantShape.Flat, dan);

        // The tenant's one role is a copy of the default pack, and the database wrote the first seat's rights from it:
        // every live key, at the root.
        provisioned.RolesByPack.Keys.Should().Equal(TenancyPacks.DefaultAdministratorsKey);
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ListAsync<string>("SELECT \"Name\" || ' ' || \"FromPack\" FROM tenancy.\"Roles\" WHERE \"TenantId\" = $1", Cancellation, Estuary.Value))
                .Should().Equal("Administrator administrator");
            (await owner.ListAsync<string>("SELECT \"Key\" FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, provisioned.AdminSeat.Value))
                .Should().BeEquivalentTo(catalogue.LiveKeys, "each key once, at the root");
        }

        // Under the policies the first seat is an administrator: it holds every key for the whole tenant, and gives a
        // role that manages access.
        await using (var asDan = await AsCaller.PersonAsync(database, dan, Estuary, Cancellation))
        {
            foreach (var key in catalogue.LiveKeys)
            {
                (await asDan.ScalarAsync<bool>("SELECT tenancy.holds_tenant_wide($1)", Cancellation, key)).Should().BeTrue("the default pack holds {0}", key);
            }
        }

        var desk = await services.BySeat(dan, Estuary, provisioned.AdminSeat,
            scoped => scoped.Roles().CreateAsync("Settings desk", "Keeps the settings", [TenancyKeys.SettingsManage], Cancellation));
        var fay = await services.BySeat(dan, Estuary, provisioned.AdminSeat, scoped => scoped.Seats().AddSeatAsync(Guid.NewGuid(), "Fay", Cancellation));
        await services.BySeat(dan, Estuary, provisioned.AdminSeat, scoped => scoped.Seats().PlaceAsync(fay, provisioned.RootUnit, primary: true, Cancellation));
        await services.BySeat(dan, Estuary, provisioned.AdminSeat,
            scoped => scoped.Seats().GrantAsync(fay, provisioned.RootUnit, desk, until: null, reason: null, Cancellation));
    }

    [Fact]
    public async Task A_settings_manager_changing_the_shape_adds_the_listing_administrators_role_as_its_pack_lists_it()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = await ListingServicesAsync(database);

        // What a copy of each administrators' pack holds, the database says as the catalogue does.
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ScalarAsync<string[]>("SELECT tenancy.pack_keys($1)", Cancellation, TreeAdministrators)).Should().Equal(Listed);
            (await owner.ScalarAsync<string[]>("SELECT tenancy.pack_keys($1)", Cancellation, HostCatalogue.AdministratorPack))
                .Should().Equal(TenancyPostgres.Catalogue.LiveKeys, "a pack that lists nothing holds every live key");
        }

        // A flat tenant, whose administrator makes a settings desk: a seat that changes the tenant's settings and
        // manages no roles.
        var dan = Guid.NewGuid();
        var fayIdentity = Guid.NewGuid();
        var provisioned = await ProvisionEstuaryAsync(services, TenantShape.Flat, dan);
        provisioned.RolesByPack.Keys.Should().NotContain(TreeAdministrators, "that pack is for a hierarchical tenant");
        var settingsDesk = await services.BySeat(dan, Estuary, provisioned.AdminSeat,
            scoped => scoped.Roles().CreateAsync("Settings desk", "Keeps the settings", [TenancyKeys.SettingsManage], Cancellation));
        var fay = await services.BySeat(dan, Estuary, provisioned.AdminSeat, scoped => scoped.Seats().AddSeatAsync(fayIdentity, "Fay", Cancellation));
        await services.BySeat(dan, Estuary, provisioned.AdminSeat, scoped => scoped.Seats().PlaceAsync(fay, provisioned.RootUnit, primary: true, Cancellation));
        await services.BySeat(dan, Estuary, provisioned.AdminSeat,
            scoped => scoped.Seats().GrantAsync(fay, provisioned.RootUnit, settingsDesk, until: null, reason: null, Cancellation));

        // Without the roles key the policy lets her add a role only as a copy of a pack, holding exactly the keys the
        // database says the pack holds. The use case copies the hierarchical administrators' pack with the keys it
        // lists, so the two agree.
        await services.BySeat(fayIdentity, Estuary, fay, scoped => scoped.Tenants().ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, Cancellation));

        // A role holding every live key under that pack's name is no copy of it, and is refused her.
        await using (var asFay = await AsCaller.PersonAsync(database, fayIdentity, Estuary, Cancellation))
        {
            var everyLiveKey = () => asFay.AttemptAsync(
                """
                INSERT INTO tenancy."Roles" ("Id", "NormalizedName", "Version", "TenantId", "Name", "Description", "FromPack", "Status", "Keys")
                VALUES (gen_random_uuid(), 'SECOND TREE ADMINISTRATOR', 0, $1, 'Second tree administrator', '', $2, 'Active', $3)
                """,
                Cancellation,
                Estuary.Value,
                TreeAdministrators,
                TenancyPostgres.Catalogue.LiveKeys.ToArray());
            (await everyLiveKey.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        // The tenant has the role now, with the keys its pack lists. No grant changed: the first administrator keeps
        // the flat tenant's role, with every live key, and the new role is nobody's until someone gives it.
        await using var after = await AsCaller.OwnerAsync(database, Cancellation);
        (await after.ListAsync<string>(
                "SELECT pg_catalog.unnest(\"Keys\") FROM tenancy.\"Roles\" WHERE \"TenantId\" = $1 AND \"FromPack\" = $2", Cancellation, Estuary.Value, TreeAdministrators))
            .Should().BeEquivalentTo(Listed);
        (await after.ScalarAsync<long>(
                "SELECT count(*) FROM tenancy.\"SeatRoleGrants\" g JOIN tenancy.\"Roles\" r ON r.\"Id\" = g.\"RoleId\" WHERE r.\"TenantId\" = $1 AND r.\"FromPack\" = $2",
                Cancellation,
                Estuary.Value,
                TreeAdministrators))
            .Should().Be(0);
        (await after.ListAsync<string>("SELECT \"Key\" FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, provisioned.AdminSeat.Value))
            .Should().BeEquivalentTo(TenancyPostgres.Catalogue.LiveKeys);
    }

    [Fact]
    public async Task A_tenant_provisioned_in_a_language_gets_its_roles_named_in_it_and_so_does_a_change_of_shape_by_a_settings_manager()
    {
        var dutch = CultureInfo.GetCultureInfo("nl");
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database, configure: registered => registered.AddSingleton<IRolePackTexts>(new DutchPacks()));

        // Provisioned as system work in the new tenant, under the policies, with the application's own fields set.
        var dan = Guid.NewGuid();
        var fayIdentity = Guid.NewGuid();
        HostTenancy.ProvisionedTenant provisioned;
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            provisioned = await services.InScopeAsync(scoped => scoped.Tenants().ProvisionAsync(
                new HostTenancy.TenantToProvision(
                    "estuary", "Estuary Works", TenantShape.Flat, "Estuary", "company", dan, "Dan",
                    TenantId: Estuary,
                    Language: dutch,
                    ConfigureTenant: tenant => tenant.MarkAsDemo(),
                    ConfigureFirstSeat: seat => seat.ChangeJobTitle("Harbor master")),
                Cancellation));
        }

        await services.BySeat(dan, Estuary, provisioned.AdminSeat, async scoped =>
        {
            (await scoped.Directory().ListRolesAsync(Cancellation)).Select(role => (role.Name, role.FromPack)).Should().Equal(
                ("Bediener", HostCatalogue.OperatorPack), ("Hoofdgebruiker", HostCatalogue.AdministratorPack), ("Toeschouwer", HostCatalogue.WatcherPack));
            (await scoped.Tenancy().Set<HostTenant>().AsNoTracking().SingleAsync(Cancellation)).IsDemo.Should().BeTrue();
            (await scoped.Tenancy().Set<HostSeat>().AsNoTracking().SingleAsync(seat => seat.Id == provisioned.AdminSeat, Cancellation))
                .JobTitle.Should().Be("Harbor master");
        });

        // A seat that manages the settings and no roles: the policy lets it add a role only as a copy of a pack, by the
        // pack's keys. The name is the tenant's, so the copy goes in under the pack's Dutch name.
        var settingsDesk = await services.BySeat(dan, Estuary, provisioned.AdminSeat,
            scoped => scoped.Roles().CreateAsync("Settings desk", "Keeps the settings", [TenancyKeys.SettingsManage], Cancellation));
        var fay = await services.BySeat(dan, Estuary, provisioned.AdminSeat, scoped => scoped.Seats().AddSeatAsync(fayIdentity, "Fay", Cancellation));
        await services.BySeat(dan, Estuary, provisioned.AdminSeat, scoped => scoped.Seats().PlaceAsync(fay, provisioned.RootUnit, primary: true, Cancellation));
        await services.BySeat(dan, Estuary, provisioned.AdminSeat,
            scoped => scoped.Seats().GrantAsync(fay, provisioned.RootUnit, settingsDesk, until: null, reason: null, Cancellation));

        await services.BySeat(fayIdentity, Estuary, fay, scoped => scoped.Tenants().ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, dutch, Cancellation));

        var supervisor = (await services.BySeat(fayIdentity, Estuary, fay, scoped => scoped.Directory().ListRolesAsync(Cancellation)))
            .Should().ContainSingle(role => role.FromPack == HostCatalogue.SupervisorPack).Which;
        supervisor.Name.Should().Be("Afdelingshoofd");
        supervisor.Keys.Should().Equal(TenancyPostgres.Catalogue.Packs.Single(pack => pack.Key == HostCatalogue.SupervisorPack).Keys);
    }

    /// <summary>The TestHost's packs in Dutch, as an application would read them from its own resources.</summary>
    private sealed class DutchPacks : IRolePackTexts
    {
        public (string Name, string Description)? For(RolePack pack, CultureInfo culture)
            => culture.TwoLetterISOLanguageName != "nl"
                ? null
                : pack.Key switch
                {
                    HostCatalogue.AdministratorPack => ("Hoofdgebruiker", "Regelt de tenant"),
                    HostCatalogue.SupervisorPack => ("Afdelingshoofd", "Leidt een deel van de organisatie"),
                    HostCatalogue.OperatorPack => ("Bediener", "Werkt met widgets"),
                    HostCatalogue.WatcherPack => ("Toeschouwer", "Bekijkt widgets"),
                    _ => null,
                };
    }

    /// <summary>
    /// An application that runs with <see cref="ListingAdministrators"/> over <paramref name="database"/>, after the
    /// access file written from that catalogue was applied: it is the migration, as for any change of a pack.
    /// </summary>
    private Task<TenancyServices> ListingServicesAsync(TestDatabase database) => ServicesWithAsync(database, ListingAdministrators);

    /// <summary>
    /// An application that runs with <paramref name="application"/> over <paramref name="database"/>, after the access
    /// file written from the catalogue built of it was applied, and the start-up check found the policies in place.
    /// </summary>
    private async Task<TenancyServices> ServicesWithAsync(TestDatabase database, ApplicationCatalogue application)
    {
        foreach (var script in TenancyPostgres.AccessScripts(TenancyCatalogue.Build(application, []), names: names))
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        var services = new TenancyServices(database, catalogue: application);
        try
        {
            await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);
            return services;
        }
        catch
        {
            await services.DisposeAsync();
            throw;
        }
    }

    /// <summary>Provisions Estuary with <paramref name="administrator"/> as the identity of its first administrator, as system work outside any tenant.</summary>
    private static async Task<HostTenancy.ProvisionedTenant> ProvisionEstuaryAsync(TenancyServices services, TenantShape shape, Guid administrator)
    {
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            return await services.InScopeAsync(scoped => scoped.Tenants().ProvisionAsync(
                new HostTenancy.TenantToProvision("estuary", "Estuary Works", shape, "Estuary", "company", administrator, "Dan", TenantId: Estuary),
                Cancellation));
        }
    }

    /// <summary>How many rows each tenant has in each of Tenancy's tables, as the owner counts them.</summary>
    private static async Task<Dictionary<(string Table, long Tenant), long>> RowsByTenantAsync(TestDatabase database)
    {
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        var rows = new Dictionary<(string Table, long Tenant), long>();
        foreach (var (table, tenant) in new[]
                 {
                     ("Tenants", "\"Id\""), ("Organizations", "\"Id\""), ("OrganizationUnits", "\"TenantId\""), ("OrganizationUnitPaths", "\"TenantId\""),
                     ("Seats", "\"TenantId\""), ("SeatPlacements", "\"TenantId\""), ("SeatRights", "\"TenantId\""), ("Roles", "\"TenantId\""),
                     ("TenancyAccessRevisions", "\"TenantId\""),
                 })
        {
            foreach (var counted in await owner.ListAsync<string>($"SELECT {tenant} || ' ' || count(*) FROM tenancy.\"{table}\" GROUP BY {tenant}", Cancellation))
            {
                var parts = counted.Split(' ');
                rows[(table, long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture))] = long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return rows;
    }
}

/// <summary>Provisioning as system work in the tenant it makes, under the names Entity Framework gives the tables and columns.</summary>
public sealed class ProvisioningTestsOnDefaultNames(TenancyPostgres postgres) : ProvisioningTests(postgres, TenancyNaming.Default);

/// <summary>Provisioning as system work in the tenant it makes, under snake_case names with enums stored as snake_case text.</summary>
public sealed class ProvisioningTestsOnSnakeCase(TenancyPostgres postgres) : ProvisioningTests(postgres, TenancyNaming.SnakeCase);
