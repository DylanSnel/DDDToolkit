using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Interfaces;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// The store the use cases load and save through: it keeps to the caller's tenant, provisions a working tenant in
/// one save, and stores Tenancy's domain events under their own names and off the sinks. A key removed from the
/// catalogue is reported, and its holders get no rights for it.
/// </summary>
public abstract class StoreTests(TestDatabases databases) : IAsyncLifetime
{
    private const string PolishKey = "widget.polish";

    private TestServices _services = null!;

    public async ValueTask InitializeAsync() => _services = await databases.ServicesAsync();

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_seat_of_another_tenant_is_not_found()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var orchard = await _services.ProvisionAsync("orchard");

        await _services.BySeat(harbor.Tenant, harbor.AdminSeat, async services =>
        {
            var store = services.GetRequiredService<HostTenancy.IStore>();

            (await store.FindSeatAsync(orchard.AdminSeat, TestContext.Current.CancellationToken)).Should().BeNull();
            (await store.FindRoleAsync(orchard.AdministratorRole, TestContext.Current.CancellationToken)).Should().BeNull();
            (await store.FindTenantAsync(orchard.Tenant, TestContext.Current.CancellationToken)).Should().BeNull();
            (await store.FindOrganizationAsync(orchard.Tenant, TestContext.Current.CancellationToken)).Should().BeNull();
            (await store.FindSeatAsync(harbor.AdminSeat, TestContext.Current.CancellationToken)).Should().NotBeNull("the caller's own tenant is found");
        });

        await Refused.WithCodeAsync(TenancyRefusals.SeatNotFound, () => _services.BySeat(harbor.Tenant, harbor.AdminSeat, services =>
            services.Seats().RenameAsync(orchard.AdminSeat, "Someone else", TestContext.Current.CancellationToken)));
        await Refused.WithCodeAsync(TenancyRefusals.RoleNotFound, () => _services.BySeat(harbor.Tenant, harbor.AdminSeat, services =>
            services.Seats().GrantAsync(harbor.AdminSeat, harbor.RootUnit, orchard.AdministratorRole, until: null, reason: null, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task The_store_lists_seats_by_name_all_or_by_id()
    {
        var identity = Guid.NewGuid();
        var harbor = await _services.ProvisionAsync("harbor", administrator: identity);
        var orchard = await _services.ProvisionAsync("orchard", administratorName: "Odette");
        var grace = await _services.AddSeatAsync(harbor.Tenant, Guid.NewGuid(), "Grace");
        var lin = await _services.AddSeatAsync(harbor.Tenant, Guid.NewGuid(), "Lin");
        await _services.BySystemIn(harbor.Tenant, services => services.Seats().SuspendAsync(lin, TestContext.Current.CancellationToken));

        // Grace has no placement and no key: whoever works in a tenant reads its seats' names.
        await _services.BySeat(harbor.Tenant, grace, async services =>
        {
            var store = services.GetRequiredService<HostTenancy.IStore>();

            _services.Commands.Reset();
            var all = await store.ListSeatsAsync(harbor.Tenant, only: null, TestContext.Current.CancellationToken);
            all.Should().BeEquivalentTo(
            [
                new HostTenancy.SeatSummary(harbor.AdminSeat, "Ada", SeatStatus.Active),
                new HostTenancy.SeatSummary(grace, "Grace", SeatStatus.Active),
                new HostTenancy.SeatSummary(lin, "Lin", SeatStatus.Suspended),
            ]);
            _services.Commands.Count.Should().Be(1);
            Selected(_services.Commands.Commands.Single()).Should().NotContain("Identity", "the identity never leaves the database");

            _services.Commands.Reset();
            var some = await store.ListSeatsAsync(harbor.Tenant, [lin, orchard.AdminSeat, SeatId.CreateSequential(), harbor.AdminSeat], TestContext.Current.CancellationToken);
            some.Select(seat => seat.DisplayName).Should().BeEquivalentTo(["Ada", "Lin"], "a seat of another tenant and no seat at all are not found");
            _services.Commands.Count.Should().Be(1, "the ids travel with the one statement");
            Selected(_services.Commands.Commands.Single()).Should().NotContain("Identity");

            (await store.ListSeatsAsync(harbor.Tenant, [], TestContext.Current.CancellationToken)).Should().BeEmpty();

            // Another tenant than the caller's is behind the tenant filter, whichever tenant is named.
            (await store.ListSeatsAsync(orchard.Tenant, only: null, TestContext.Current.CancellationToken)).Should().BeEmpty();
            (await store.ListSeatsAsync(orchard.Tenant, [orchard.AdminSeat], TestContext.Current.CancellationToken)).Should().BeEmpty();

            // And nothing it read is tracked: the directory's reads leave the unit of work as it was.
            services.Tenancy().ChangeTracker.Entries<HostSeat>().Should().BeEmpty();
        });

        // What the statement selects, from its first word to the table it reads: the columns that leave the database.
        static string Selected(string sql) => sql[..sql.IndexOf("FROM", StringComparison.Ordinal)];
    }

    [Fact]
    public async Task Provisioning_end_to_end_through_the_store()
    {
        var identity = Guid.NewGuid();
        var harbor = await _services.ProvisionAsync("harbor", administrator: identity);

        await _services.BySystemIn(harbor.Tenant, async services =>
        {
            var store = services.GetRequiredService<HostTenancy.IStore>();

            var tenant = (await store.FindTenantAsync(harbor.Tenant, TestContext.Current.CancellationToken))!;
            tenant.Status.Should().Be(TenantStatus.Active);
            tenant.Shape.Should().Be(TenantShape.Hierarchical);

            (await store.FindOrganizationAsync(harbor.Tenant, TestContext.Current.CancellationToken))!.Root.Id.Should().Be(harbor.RootUnit);
            (await store.ListRolesAsync(harbor.Tenant, TestContext.Current.CancellationToken)).Select(role => (role.Id, role.FromPack))
                .Should().BeEquivalentTo(harbor.RolesByPack.Select(pair => (pair.Value, (string?)pair.Key)));

            var administrator = (await store.FindSeatAsync(harbor.AdminSeat, TestContext.Current.CancellationToken))!;
            administrator.Placements.Should().ContainSingle().Which.Grants.Should().ContainSingle().Which.RoleId.Should().Be(harbor.AdministratorRole);

            (await store.SlugTakenAsync(" Harbor ", TestContext.Current.CancellationToken)).Should().BeTrue("a slug is compared as it is stored");
            (await store.SlugTakenAsync("orchard", TestContext.Current.CancellationToken)).Should().BeFalse();
            (await store.IdentityHasSeatAsync(harbor.Tenant, identity, TestContext.Current.CancellationToken)).Should().BeTrue();
            (await store.RoleNameTakenAsync(harbor.Tenant, "ADMINISTRATOR", except: null, TestContext.Current.CancellationToken)).Should().BeTrue();
            (await store.RoleNameTakenAsync(harbor.Tenant, "Administrator", except: harbor.AdministratorRole, TestContext.Current.CancellationToken)).Should().BeFalse();
        });

        // The rows the questions read are written by the same save: the first administrator holds every live key at the root.
        var catalogue = _services.Provider.GetRequiredService<TenancyCatalogue>();
        (await _services.StoredRightsAsync(harbor.AdminSeat)).Select(right => (right.UnitId, right.Key))
            .Should().BeEquivalentTo(catalogue.LiveKeys.Select(key => (harbor.RootUnit, key)));
        (await _services.StoredPathsAsync(harbor.Tenant)).Should().ContainSingle();
        _services.Database.CountRows("TenancyAccessRevisions").Should().Be(1);

        var overview = await _services.BySeat(harbor.Tenant, harbor.AdminSeat, services => services.Directory().WhoAmIAsync(TestContext.Current.CancellationToken));
        overview.Tenant.Slug.Should().Be("harbor");
        overview.Keys.Should().OnlyContain(reach => reach.WholeTenant).And.HaveCount(catalogue.LiveKeys.Count);
    }

    [Fact]
    public async Task A_seat_loads_its_placements_and_their_grants_in_one_query_without_a_warning()
    {
        // The warning Entity Framework logs for a query that loads two collections, the placements and their
        // grants, without saying how, fails the query here instead.
        using var services = await databases.ServicesAsync(collection => collection.ConfigureDbContext<TestTenancyContext>(options =>
            options.ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))));
        var harbor = await services.ProvisionAsync("harbor");
        var north = await services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var grace = await services.SeatAtAsync(harbor, "Grace", north, HostCatalogue.OperatorPack, HostCatalogue.WatcherPack);
        await services.BySystemIn(harbor.Tenant, scoped => scoped.Seats().PlaceAsync(grace, harbor.RootUnit, primary: false, TestContext.Current.CancellationToken));
        await services.GrantAsync(harbor, grace, harbor.RootUnit, HostCatalogue.WatcherPack);

        await services.BySystemIn(harbor.Tenant, async scoped =>
        {
            services.Commands.Reset();
            var seat = (await scoped.GetRequiredService<HostTenancy.IStore>().FindSeatAsync(grace, TestContext.Current.CancellationToken))!;
            services.Commands.Commands.Should().ContainSingle("the seat, its placements and their grants come back together");

            seat.Placements.Select(placement => (placement.UnitId, placement.IsPrimary, Roles: placement.Grants.Select(grant => grant.RoleId).Order().ToArray()))
                .Should().BeEquivalentTo(
                [
                    (north, true, new[] { harbor.RolesByPack[HostCatalogue.OperatorPack], harbor.RolesByPack[HostCatalogue.WatcherPack] }.Order().ToArray()),
                    (harbor.RootUnit, false, new[] { harbor.RolesByPack[HostCatalogue.WatcherPack] }),
                ]);
        });
    }

    [Fact]
    public async Task The_package_events_are_stored_under_tenancy_names()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        await _services.SeatAtAsync(harbor, "Grace", north, HostCatalogue.OperatorPack);

        await using var scope = _services.Scope();
        var names = await scope.ServiceProvider.Tenancy().Set<OutboxMessage>().Select(message => message.EventName).ToListAsync(TestContext.Current.CancellationToken);

        names.Should().Contain(
        [
            "tenancy.tenant-provisioned", "tenancy.tenant-activated", "tenancy.organization-unit-added", "tenancy.role-created",
            "tenancy.seat-added", "tenancy.seat-placed", "tenancy.organization-role-granted",
        ]);
        names.Should().OnlyContain(name => name.StartsWith("tenancy.", StringComparison.Ordinal), "a generic type's own name would carry a backtick");
    }

    [Fact]
    public async Task Tenancy_domain_events_are_kept_off_the_sinks()
    {
        // Nothing mapped: every event is stored, delivered to nothing, and done with.
        var sink = new RecordingSink();
        using (var services = await databases.ServicesAsync(collection => collection
                   .AddOutboxProcessor<TestTenancyContext>()
                   .AddDDDToolkitEntityFramework(options => options.UseOutbox<TestTenancyContext>(outbox => outbox.SendTo(sink)))))
        {
            await services.ProvisionAsync("harbor");
            var stored = services.Database.CountRows("OutboxMessages");

            (await ProcessAsync(services)).Should().Be(stored, "every message is done with");
            sink.Messages.Should().BeEmpty("a domain event of the package is not a contract");
        }

        // Mapped before the package's registration: that event leaves as the application's contract, the rest stay in.
        var mapped = new RecordingSink();
        using (var services = await databases.ServicesAsync(collection => collection
                   .AddOutboxProcessor<TestTenancyContext>()
                   .AddDDDToolkitEntityFramework(options => options.UseOutbox<TestTenancyContext>(outbox => outbox
                       .SendTo(mapped)
                       .PublishAs<SeatAdded<TenantId, SeatId>, SeatJoined>(added => new SeatJoined(added.TenantId.Value, added.SeatId.Value))))))
        {
            var harbor = await services.ProvisionAsync("harbor");
            await ProcessAsync(services);

            mapped.Messages.Should().ContainSingle().Which.Body.Should().Be(new SeatJoined(harbor.Tenant.Value, harbor.AdminSeat.Value));
        }
    }

    [Fact]
    public async Task A_stored_event_is_read_back_with_who_made_the_change()
    {
        // Mapped to contracts of the application's own: each is made from the event as the outbox reads it back from
        // its stored row, so what a contract says about who acted is what the row kept.
        var sink = new RecordingSink();
        using var services = await databases.ServicesAsync(collection => collection
            .AddOutboxProcessor<TestTenancyContext>()
            .AddDDDToolkitEntityFramework(options => options.UseOutbox<TestTenancyContext>(outbox => outbox
                .SendTo(sink)
                .PublishAs<OrganizationRoleGranted<TenantId, SeatId, OrganizationUnitId, RoleId>, RoleGiven>(granted => new RoleGiven(
                    granted.SeatId.Value, TenancyActorKinds.Of(granted.By!.Value.Kind), granted.By.Value.Seat?.Value, granted.By.Value.Operator, granted.By.Value.Scope))
                .PublishAs<RoleKeysChanged<TenantId, RoleId, SeatId>, RoleKeysSet>(changed => new RoleKeysSet(
                    changed.RoleId.Value, string.Join(" ", changed.Added), string.Join(" ", changed.Removed), changed.By!.Value.Seat?.Value)))));

        var operatorIdentity = Guid.NewGuid();
        HostTenancy.ProvisionedTenant harbor;
        await using (var scope = services.Scope())
        using (TenancyWork.BeginOperator<TenantId, SeatId>(operatorIdentity))
        {
            harbor = await scope.ServiceProvider.Tenants().ProvisionAsync(
                new HostTenancy.TenantToProvision("harbor", "Harbor Works", TenantShape.Hierarchical, "Harbor", "company", Guid.NewGuid(), "Ada"),
                TestContext.Current.CancellationToken);
        }

        var watcher = harbor.RolesByPack[HostCatalogue.WatcherPack];
        var grace = await services.SeatAtAsync(harbor, "Grace", harbor.RootUnit);
        await services.RunAsync(HostCaller.SystemIn(harbor.Tenant, harbor.AdminSeat), scoped =>
            scoped.Seats().GrantAsync(grace, harbor.RootUnit, harbor.RolesByPack[HostCatalogue.OperatorPack], until: null, reason: null, TestContext.Current.CancellationToken));
        await services.BySeat(harbor.Tenant, harbor.AdminSeat, scoped =>
            scoped.Seats().GrantAsync(grace, harbor.RootUnit, watcher, until: null, reason: null, TestContext.Current.CancellationToken));
        await services.BySeat(harbor.Tenant, harbor.AdminSeat, scoped =>
            scoped.Roles().SetKeysAsync(watcher, [HostCatalogue.WidgetCreate, HostCatalogue.WidgetChange], TestContext.Current.CancellationToken));

        await ProcessAsync(services);

        // The four kinds of actor a grant can have here, each with what it is named by and nothing else.
        sink.Messages.Select(message => message.Body).OfType<RoleGiven>().Should().Equal(
            new RoleGiven(harbor.AdminSeat.Value, "operator", null, operatorIdentity, "tenancy"),
            new RoleGiven(grace.Value, "system", harbor.AdminSeat.Value, null, "tenancy"),
            new RoleGiven(grace.Value, "seat", harbor.AdminSeat.Value, null, null));
        sink.Messages.Select(message => message.Body).OfType<RoleKeysSet>().Should().Equal(
            new RoleKeysSet(watcher.Value, "widget.change widget.create", string.Empty, harbor.AdminSeat.Value));
    }

    [Fact]
    public async Task Role_names_are_unique_ignoring_case_beyond_ascii()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        await _services.BySystemIn(harbor.Tenant, services =>
            services.Roles().CreateAsync("Élan", "Keeps going", [HostCatalogue.WidgetRead], TestContext.Current.CancellationToken));

        // SQLite's own lower case stops at ASCII, and a database's may follow its collation: the name is compared as
        // it was normalized before it was stored.
        await _services.BySystemIn(harbor.Tenant, async services =>
            (await services.GetRequiredService<HostTenancy.IStore>().RoleNameTakenAsync(harbor.Tenant, "élan", except: null, TestContext.Current.CancellationToken))
                .Should().BeTrue());
        await Refused.WithCodeAsync(TenancyRefusals.RoleNameTaken, () => _services.BySystemIn(harbor.Tenant, services =>
            services.Roles().CreateAsync("ÉLAN", "Shouts", [HostCatalogue.WidgetRead], TestContext.Current.CancellationToken)));

        // A create that got past the check, as one racing another would, meets the unique index, which gives the
        // refusal the check gives.
        await _services.BySystemIn(harbor.Tenant, async services =>
        {
            var context = services.Tenancy();
            context.Add(TenancyInstances.NewRole<HostRole, RoleId, TenantId, SeatId>(
                RoleId.CreateSequential(), harbor.Tenant, new RoleDraft("élan", string.Empty, [HostCatalogue.WidgetRead]), services.GetRequiredService<TenancyCatalogue>()));

            var refusal = await Refused.WithCodeAsync(TenancyRefusals.RoleNameTaken, () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
            refusal.Arguments.Should().Contain("Name", "élan");
            refusal.InnerException.Should().BeOfType<DbUpdateException>();
        });

        _services.Database.CountRows("Roles").Should().Be(harbor.RolesByPack.Count + 1);
    }

    [Fact]
    public async Task A_key_removed_from_the_catalogue_is_reported()
    {
        // The application once declared a key, gave it to a role, and granted the role.
        using var before = await databases.ServicesAsync(collection => collection.AddTenancyPermissions([new Permission(PolishKey, "Widgets", "Polish widgets")]));
        var harbor = await before.ProvisionAsync("harbor");
        var polisher = await before.BySystemIn(harbor.Tenant, services =>
            services.Roles().CreateAsync("Polisher", "Polishes widgets", [PolishKey, HostCatalogue.WidgetRead], TestContext.Current.CancellationToken));
        var grace = await before.SeatAtAsync(harbor, "Grace", harbor.RootUnit);
        await before.BySystemIn(harbor.Tenant, services => services.Seats().GrantAsync(grace, harbor.RootUnit, polisher, until: null, reason: null, TestContext.Current.CancellationToken));
        (await before.StoredRightsAsync(grace)).Select(right => right.Key).Should().Contain(PolishKey);

        // A later version of it removed the key from the code instead of retiring it.
        using var after = new TestServices(database: before.Database);
        await using (var scope = after.Scope())
        {
            var unknown = await TenancyChecks.UnknownStoredKeysAsync<HostRole, RoleId, TenantId>(
                scope.ServiceProvider.Tenancy(), scope.ServiceProvider.GetRequiredService<TenancyCatalogue>(), TestContext.Current.CancellationToken);
            unknown.Should().Equal(PolishKey);
        }

        // The role keeps the key, and it gives no rights: a new holder gets no row for it, and an old one loses
        // its row the next time its rights are written.
        var lin = await after.SeatAtAsync(harbor, "Lin", harbor.RootUnit);
        await after.BySystemIn(harbor.Tenant, services => services.Seats().GrantAsync(lin, harbor.RootUnit, polisher, until: null, reason: null, TestContext.Current.CancellationToken));
        (await after.StoredRightsAsync(lin)).Select(right => right.Key).Should().Equal(HostCatalogue.WidgetRead);

        await after.BySystemIn(harbor.Tenant, services => services.Seats().SuspendAsync(grace, TestContext.Current.CancellationToken));
        await after.BySystemIn(harbor.Tenant, services => services.Seats().ReactivateAsync(grace, TestContext.Current.CancellationToken));
        (await after.StoredRightsAsync(grace)).Select(right => right.Key).Should().Equal(HostCatalogue.WidgetRead);

        await after.BySystemIn(harbor.Tenant, async services =>
            (await services.Tenancy().Set<HostRole>().SingleAsync(role => role.Id == polisher, TestContext.Current.CancellationToken)).Keys.Should().Contain(PolishKey));
    }

    [Fact]
    public async Task The_store_answers_the_administrators_and_a_moves_reach_as_before_without_the_option()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var administrators = harbor.AdministratorRole;
        var now = DateTimeOffset.UtcNow;
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var south = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "South");
        var second = await _services.SeatAtAsync(harbor, "Cy", harbor.RootUnit, HostCatalogue.AdministratorPack);
        var temporary = await _services.SeatAtAsync(harbor, "Bert", harbor.RootUnit);
        await _services.GrantAsync(harbor, temporary, harbor.RootUnit, HostCatalogue.AdministratorPack, until: now.AddDays(7));
        var grace = await _services.SeatAtAsync(harbor, "Grace", north, HostCatalogue.SupervisorPack, HostCatalogue.WatcherPack);
        var hal = await _services.SeatAtAsync(harbor, "Hal", south, HostCatalogue.OperatorPack);
        var orchard = await _services.ProvisionAsync("orchard");
        var catalogue = TenancyCatalogue.Build(HostCatalogue.Application, []);

        // Asked by a seat, with the rights the save's to write and every seat's to read: the store reads them itself.
        var (pairs, reaches) = await _services.BySeat(harbor.Tenant, grace, async services =>
        {
            var store = services.GetRequiredService<HostTenancy.IStore>();
            _services.Commands.Reset();
            var answered = (
                await store.AdministratorsAsync(harbor.Tenant, now.AddMinutes(1), TestContext.Current.CancellationToken),
                await store.RightsAMoveChangesAsync(harbor.Tenant, grace, north, south, catalogue.AccessManagingKeys, now.AddMinutes(1), TestContext.Current.CancellationToken));
            _services.Commands.Count.Should().Be(2, "one statement each");
            _services.Commands.Commands.Should().OnlyContain(command => command.Contains("\"SeatRights\"") && !command.Contains("tenant_administrators") && !command.Contains("rights_a_move_changes"));
            return answered;
        });

        // The administrators: a right at the root for the role key, with no end. Bert's ends, and Orchard's is not Harbor's.
        pairs.Should().BeEquivalentTo([(harbor.AdminSeat, administrators), (second, administrators)]);
        pairs.Should().NotContain(pair => pair.Seat == orchard.AdminSeat);

        // A move from under North to under South: Grace's own rights at North, whatever their key, reaching North; every
        // seat's rights of a key that manages access at the root, reaching both; and nothing of Hal's at South, whose
        // operator's keys manage no access.
        reaches.Where(reach => reach.OfCaller).Select(reach => (reach.UnitId, reach.Key, reach.Parent)).Should().BeEquivalentTo(
        [
            (north, TenancyKeys.GrantsManage, north), (north, TenancyKeys.SeatsManage, north), (north, TenancyKeys.UnitsManage, north),
            (north, HostCatalogue.WidgetChange, north), (north, HostCatalogue.WidgetCreate, north), (north, HostCatalogue.WidgetRead, north),
            (north, HostCatalogue.WidgetRead, north),
        ], "the supervisor's keys and the watcher's, each a right of its own");
        reaches.Where(reach => !reach.OfCaller).Should().OnlyContain(reach => reach.UnitId == harbor.RootUnit && catalogue.ManagesAccess(reach.Key))
            .And.HaveCount(3 * catalogue.AccessManagingKeys.Count * 2, "three administrators, each key that manages access, once for each parent");
        reaches.Where(reach => reach.EndsAt != null).Should().HaveCount(catalogue.AccessManagingKeys.Count * 2, "Bert's, which have not ended")
            .And.OnlyContain(reach => reach.EndsAt > now.AddDays(6));
        reaches.Should().NotContain(reach => reach.UnitId == south);
        hal.Should().NotBe(grace);
    }

    private static async Task<int> ProcessAsync(TestServices services)
    {
        await using var scope = services.Scope();
        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor<TestTenancyContext>>().ProcessPendingAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>The application's own contract for a seat added to a tenant.</summary>
    public sealed record SeatJoined(long Tenant, Guid Seat);

    /// <summary>The application's own contract for a role given to a seat, with who gave it, flattened.</summary>
    public sealed record RoleGiven(Guid Seat, string ByKind, Guid? BySeat, Guid? ByOperator, string? ByScope);

    /// <summary>The application's own contract for a role whose keys changed.</summary>
    public sealed record RoleKeysSet(Guid Role, string Added, string Removed, Guid? BySeat);

    /// <summary>A transport that keeps what it was handed.</summary>
    private sealed class RecordingSink : IIntegrationEventSink
    {
        private readonly List<IntegrationEventMessage> _messages = [];

        public IReadOnlyList<IntegrationEventMessage> Messages => _messages;

        public Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
        {
            _messages.Add(message);
            return Task.CompletedTask;
        }
    }
}

/// <summary>Tenancy's store, on SQLite in memory.</summary>
public sealed class StoreTestsOnSqlite() : StoreTests(TestDatabases.Sqlite);

/// <summary>Tenancy's store, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql, not the policies.</summary>
public sealed class StoreTestsOnPostgres(PostgresDatabases postgres) : StoreTests(postgres);
