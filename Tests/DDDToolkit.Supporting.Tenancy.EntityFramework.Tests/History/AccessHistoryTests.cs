using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// Tenancy's access history: the toolkit's event log with each event's tenant on its row. Every event of Tenancy's
/// that changes access gets a row in the save that raised it, which says which tenant it is about and who acted, as
/// Tenancy knows the caller: a seat, an operator, or the application's own work. A rename changes nobody's access,
/// and is not kept.
/// </summary>
public abstract class AccessHistoryTests(TestDatabases databases) : IAsyncLifetime
{
    private static readonly Guid Odette = Guid.NewGuid();

    /// <summary>The four events that only say something is called something else: stored, and no part of the history.</summary>
    private static readonly string[] Renames =
        ["tenancy.organization-renamed", "tenancy.organization-unit-renamed", "tenancy.seat-renamed", "tenancy.role-renamed"];

    private TestServices _services = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _services = await databases.ServicesAsync();

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Tenancys_events_are_kept_with_their_tenant()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var orchard = await _services.ProvisionAsync("orchard");

        // A seat changes its tenant, and an operator has another tenant suspended.
        await _services.BySeat(harbor.Tenant, harbor.AdminSeat, services => services.Organization().AddUnitAsync(harbor.RootUnit, "North", "region", Cancellation));
        await using (var scope = _services.Scope())
        using (TenancyWork.BeginOperatorIn<TenantId, SeatId>(orchard.Tenant, Odette))
        {
            await scope.ServiceProvider.Tenants().SuspendAsync("Asked for by the owner", Cancellation);
        }

        var kept = await KeptAsync();

        // Every event is there once, with the id of its outbox row, and under the tenant it names.
        await using (var scope = _services.Scope())
        {
            var sent = await scope.ServiceProvider.Tenancy().Set<OutboxMessage>().Select(message => message.Id).ToListAsync(Cancellation);
            kept.Select(row => row.Id).Should().BeEquivalentTo(sent, "a row of the history is written next to the event's outbox row, in the same save");
        }

        kept.Should().OnlyContain(row => row.Tenant == harbor.Tenant || row.Tenant == orchard.Tenant);
        kept.Where(row => row.EventName == "tenancy.tenant-provisioned").Select(row => row.Tenant).Should().BeEquivalentTo([harbor.Tenant, orchard.Tenant]);
        kept.Where(row => row.Tenant == harbor.Tenant).Select(row => row.EventName)
            .Should().Contain(["tenancy.tenant-provisioned", "tenancy.role-created", "tenancy.seat-added", "tenancy.seat-placed", "tenancy.organization-role-granted", "tenancy.organization-unit-added"]);

        // Who acted: provisioning is the application's own work, the unit was added by the administrator's seat, and
        // the tenant was suspended for the operator.
        kept.Where(row => row.EventName == "tenancy.tenant-provisioned").Should().OnlyContain(row => row.ActedByKind == "system" && row.ActedById == "tenancy");
        kept.Where(row => row.EventName == "tenancy.organization-unit-added").Select(row => row.ActedByKind).Should().BeEquivalentTo(["system", "system", "seat"], "two roots, and North");
        kept.Single(row => row.EventName == "tenancy.organization-unit-added" && row.ActedByKind == "seat")
            .Should().Match<Kept>(row => row.Tenant == harbor.Tenant && row.ActedById == harbor.AdminSeat.Value.ToString("D"));
        kept.Single(row => row.EventName == "tenancy.tenant-suspended")
            .Should().Match<Kept>(row => row.Tenant == orchard.Tenant && row.ActedByKind == "operator" && row.ActedById == Odette.ToString("D"));

        // The payload is the event as the outbox stores it: ids and dates, and no name of a seat.
        kept.Single(row => row.EventName == "tenancy.seat-added" && row.Tenant == harbor.Tenant).Payload.Should().NotContain("Ada");
    }

    [Fact]
    public async Task Every_access_change_lands_in_the_log_in_its_transaction()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var ada = harbor.AdminSeat;
        var watcher = harbor.RolesByPack[HostCatalogue.WatcherPack];
        Task AsAda(Func<IServiceProvider, Task> act) => _services.BySeat(harbor.Tenant, ada, act);

        var ben = await _services.BySeat(harbor.Tenant, ada, services => services.Seats().AddSeatAsync(Guid.NewGuid(), "Ben", Cancellation));
        await AsAda(services => services.Seats().PlaceAsync(ben, harbor.RootUnit, primary: true, Cancellation));

        // One change, looked at closely: the grant, its outbox row and its row in the history are written by one transaction.
        _services.Commands.Reset();
        await AsAda(services => services.Seats().GrantAsync(ben, harbor.RootUnit, watcher, until: null, reason: null, Cancellation));

        var writes = _services.Commands.Sent.Where(command => command.Writes).ToList();
        writes.Should().Contain(command => command.Text.Contains("\"SeatRoleGrants\""));
        writes.Should().Contain(command => command.Text.Contains("\"OutboxMessages\""));
        writes.Should().Contain(command => command.Text.Contains("\"EventLog\""));
        writes.Select(command => command.Transaction).Distinct().Should().ContainSingle("the grant and its row in the history are one save").Which.Should().NotBeNull();

        // Every other command a seat gives, the four that only rename something among them.
        await AsAda(services => services.Tenants().RenameOrganizationAsync("Harbor Yards", Cancellation));
        var east = await _services.BySeat(harbor.Tenant, ada, services => services.Organization().AddUnitAsync(harbor.RootUnit, "East", "region", Cancellation));
        var pier = await _services.BySeat(harbor.Tenant, ada, services => services.Organization().AddUnitAsync(harbor.RootUnit, "Pier", "site", Cancellation));
        await AsAda(services => services.Organization().RenameUnitAsync(pier, "Long Pier", Cancellation));
        await AsAda(services => services.Organization().MoveUnitAsync(pier, east, Cancellation));
        await AsAda(services => services.Organization().ArchiveUnitAsync(pier, Cancellation));

        var polisher = await _services.BySeat(harbor.Tenant, ada, services =>
            services.Roles().CreateAsync("Polisher", "Polishes widgets", [HostCatalogue.WidgetRead], Cancellation));
        await AsAda(services => services.Roles().RenameAsync(polisher, "Buffer", "Buffs widgets", Cancellation));
        await AsAda(services => services.Roles().SetKeysAsync(polisher, [HostCatalogue.WidgetCreate], Cancellation));
        await AsAda(services => services.Roles().ArchiveAsync(polisher, Cancellation));

        await AsAda(services => services.Seats().RenameAsync(ben, "Benedict", Cancellation));
        await AsAda(services => services.Seats().PlaceAsync(ben, east, primary: false, Cancellation));
        await AsAda(services => services.Seats().MakePrimaryAsync(ben, east, Cancellation));
        await AsAda(services => services.Seats().RevokeAsync(ben, harbor.RootUnit, watcher, Cancellation));
        await AsAda(services => services.Seats().GrantAsync(ben, east, watcher, until: null, reason: null, Cancellation));
        await AsAda(services => services.Seats().WithdrawAsync(ben, east, Cancellation));
        await AsAda(services => services.Seats().SuspendAsync(ben, Cancellation));
        await AsAda(services => services.Seats().ReactivateAsync(ben, Cancellation));
        await AsAda(services => services.Seats().DeactivateAsync(ben, Cancellation));

        // A tenant's own life: a flat one made a tree by its administrator, then suspended, reactivated and closed
        // for an operator.
        var quay = await _services.ProvisionAsync("quay", TenantShape.Flat);
        await _services.BySeat(quay.Tenant, quay.AdminSeat, services =>
            services.Tenants().ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, Cancellation));
        await ForTheOperatorAsync(quay.Tenant, tenants => tenants.SuspendAsync("unpaid", Cancellation));
        await ForTheOperatorAsync(quay.Tenant, tenants => tenants.ReactivateAsync(Cancellation));
        await ForTheOperatorAsync(quay.Tenant, tenants => tenants.CloseAsync("wound up", Cancellation));

        var kept = await KeptAsync();
        await using var scope = _services.Scope();
        var sent = await scope.ServiceProvider.Tenancy().Set<OutboxMessage>().Select(message => new { message.Id, message.EventName }).ToListAsync(Cancellation);

        // The script raised every event Tenancy has. The history has each one that changes access, under the id of its
        // outbox row, and none of the renames.
        sent.Select(message => message.EventName).Distinct().Should().HaveCount(25);
        sent.Select(message => message.EventName).Should().Contain(Renames);
        kept.Select(row => row.Id).Should().BeEquivalentTo(sent.Where(message => !Renames.Contains(message.EventName)).Select(message => message.Id));
        kept.Select(row => row.EventName).Distinct().Should().HaveCount(21).And.NotContain(Renames);

        // Each row says who acted twice, and the same both times: in its own columns, from the caller of the save, and
        // in the event, from the caller of the command.
        foreach (var row in kept)
        {
            using var payload = JsonDocument.Parse(row.Payload);
            var by = payload.RootElement.GetProperty("By");
            by.GetProperty("Kind").GetString().Should().Be(row.ActedByKind, row.EventName);
            var named = row.ActedByKind switch
            {
                "seat" => by.GetProperty("Seat").GetString(),
                "operator" => by.GetProperty("Operator").GetString(),
                _ => by.GetProperty("Scope").GetString(),
            };
            named.Should().Be(row.ActedById, row.EventName);
        }

        kept.Select(row => row.ActedByKind).Distinct().Should().BeEquivalentTo(["seat", "system", "operator"]);
        kept.Where(row => row.EventName is "tenancy.tenant-suspended" or "tenancy.tenant-reactivated" or "tenancy.tenant-closed")
            .Should().HaveCount(3).And.OnlyContain(row => row.Tenant == quay.Tenant && row.ActedByKind == "operator" && row.ActedById == Odette.ToString("D"));
    }

    [Fact]
    public async Task A_change_that_rolls_back_leaves_no_log_row()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var before = await KeptAsync();
        var units = _services.Database.CountRows("OrganizationUnits");
        var stored = _services.Database.CountRows("OutboxMessages");

        await using (var scope = _services.Scope())
        using (TenancyCallers.Begin(HostCaller.InSeat(harbor.Tenant, harbor.AdminSeat)))
        {
            var tenancy = scope.ServiceProvider.Tenancy();
            await using var transaction = await tenancy.Database.BeginTransactionAsync(Cancellation);
            await scope.ServiceProvider.Organization().AddUnitAsync(harbor.RootUnit, "North", "region", Cancellation);

            // Inside the transaction the row is there, next to the unit it is about: the save wrote both.
            var inside = await tenancy.Set<EventLogEntry>().Select(entry => entry.EventName).ToListAsync(Cancellation);
            inside.Should().HaveCount(before.Count + 1);
            inside.Count(name => name == "tenancy.organization-unit-added").Should().Be(2, "the root, and North");

            await transaction.RollbackAsync(Cancellation);
        }

        // Rolled back, the unit is gone, and so are its event and the history's row of it.
        (await KeptAsync()).Select(row => row.Id).Should().BeEquivalentTo(before.Select(row => row.Id));
        _services.Database.CountRows("OrganizationUnits").Should().Be(units);
        _services.Database.CountRows("OutboxMessages").Should().Be(stored);
    }

    [Fact]
    public async Task The_log_holds_ids_and_keys_and_no_names()
    {
        var harbor = await _services.ProvisionAsync("harbor", administratorName: "Ada Lovelace");
        var ada = harbor.AdminSeat;

        var wharf = await _services.BySeat(harbor.Tenant, ada, services => services.Organization().AddUnitAsync(harbor.RootUnit, "Farthing Wharf", "region", Cancellation));
        var lamplighter = await _services.BySeat(harbor.Tenant, ada, services =>
            services.Roles().CreateAsync("Lamplighter", "Tends the lamps", [HostCatalogue.WidgetRead], Cancellation));
        await _services.BySeat(harbor.Tenant, ada, services => services.Roles().SetKeysAsync(lamplighter, [HostCatalogue.WidgetCreate], Cancellation));
        var ben = await _services.BySeat(harbor.Tenant, ada, services => services.Seats().AddSeatAsync(Guid.NewGuid(), "Benedict Quill", Cancellation));
        await _services.BySeat(harbor.Tenant, ada, services => services.Seats().PlaceAsync(ben, wharf, primary: true, Cancellation));
        await _services.BySeat(harbor.Tenant, ada, services => services.Seats().GrantAsync(ben, wharf, lamplighter, until: null, reason: "covering for Grace", Cancellation));

        var kept = await KeptAsync();

        // What a tenant, a unit, a seat and a role are called, and why a role was given, stay with them. The words
        // are long enough not to turn up by chance among the letters of an id.
        foreach (var name in new[] { "Harbor Works", "Lovelace", "Farthing", "Lamplighter", "Tends the lamps", "Benedict", "Quill", "covering" })
        {
            kept.Should().OnlyContain(row => !row.Payload.Contains(name, StringComparison.OrdinalIgnoreCase), $"no row holds '{name}'");
        }

        // A grant is ids, dates and who made it.
        using (var granted = JsonDocument.Parse(kept.Single(row => row.EventName == "tenancy.organization-role-granted" && row.Payload.Contains(ben.Value.ToString("D"))).Payload))
        {
            var grant = granted.RootElement;
            grant.GetProperty("TenantId").GetInt64().Should().Be(harbor.Tenant.Value);
            grant.GetProperty("SeatId").GetGuid().Should().Be(ben.Value);
            grant.GetProperty("UnitId").GetGuid().Should().Be(wharf.Value);
            grant.GetProperty("RoleId").GetGuid().Should().Be(lamplighter.Value);
            grant.GetProperty("EndsAt").ValueKind.Should().Be(JsonValueKind.Null);
            grant.GetProperty("GrantedBy").GetGuid().Should().Be(ada.Value);

            var by = grant.GetProperty("By");
            by.GetProperty("Kind").GetString().Should().Be("seat", "the kind is stored as the word, not as a number");
            by.GetProperty("Seat").GetGuid().Should().Be(ada.Value);
            by.GetProperty("Operator").ValueKind.Should().Be(JsonValueKind.Null);
            by.GetProperty("Scope").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // A change of a role's keys is the keys that came in and the keys that went out.
        using var changed = JsonDocument.Parse(kept.Single(row => row.EventName == "tenancy.role-keys-changed").Payload);
        changed.RootElement.GetProperty("RoleId").GetGuid().Should().Be(lamplighter.Value);
        changed.RootElement.GetProperty("Added").EnumerateArray().Select(key => key.GetString()).Should().Equal(HostCatalogue.WidgetCreate);
        changed.RootElement.GetProperty("Removed").EnumerateArray().Select(key => key.GetString()).Should().Equal(HostCatalogue.WidgetRead);
        changed.RootElement.GetProperty("By").GetProperty("Seat").GetGuid().Should().Be(ada.Value);
    }

    [Fact]
    public async Task A_tenant_provisioned_for_an_operator_is_recorded_as_that_operators()
    {
        HostTenancy.ProvisionedTenant quay;
        await using (var scope = _services.Scope())
        using (TenancyWork.BeginOperator<TenantId, SeatId>(Odette))
        {
            quay = await scope.ServiceProvider.Tenants().ProvisionAsync(
                new HostTenancy.TenantToProvision("quay", "Quay Works", TenantShape.Flat, "Quay", "company", Guid.NewGuid(), "Quin"),
                Cancellation);
        }

        (await KeptAsync()).Should().NotBeEmpty()
            .And.OnlyContain(row => row.Tenant == quay.Tenant && row.ActedByKind == "operator" && row.ActedById == Odette.ToString("D"));
    }

    [Fact]
    public async Task An_event_that_names_no_tenant_is_kept_under_the_tenant_it_was_saved_in()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var fields = _services.Provider.GetServices<IEventLogFields>().ToList();
        fields.Should().ContainSingle("AddTenancy registers the one that fills the tenant, once");

        await using var scope = _services.Scope();
        var tenancy = scope.ServiceProvider.Tenancy();

        // An event of the application's own, raised in Tenancy's context, which names no tenant.
        using (TenancyCallers.Begin(HostCaller.InSeat(harbor.Tenant, harbor.AdminSeat)))
        {
            var row = tenancy.Add(new EventLogEntry { Id = Guid.NewGuid(), EventName = "host.pinged" });
            fields[0].Fill(new Pinged(), row);
            row.Property(TenancyEventLogTable.TenantId).CurrentValue.Should().Be(harbor.Tenant, "the save runs in the caller's tenant");

            // One that names a tenant is kept under that one, whatever the caller's is.
            fields[0].Fill(new TenantReactivated<TenantId, SeatId>(new TenantId(77), By: null), row);
            row.Property(TenancyEventLogTable.TenantId).CurrentValue.Should().Be(new TenantId(77));
            row.State = EntityState.Detached;
        }

        // With neither, the row has no tenant to go under, and the save is refused with what to do about it.
        using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.NotSeated)))
        {
            var row = tenancy.Add(new EventLogEntry { Id = Guid.NewGuid(), EventName = "host.pinged" });
            FluentActions.Invoking(() => fields[0].Fill(new Pinged(), row))
                .Should().Throw<InvalidOperationException>().WithMessage("The event Pinged goes into Tenancy's access history*Give the event its tenant*");
            row.State = EntityState.Detached;
        }

        // A log without the tenant's column is not its business.
        await using var plain = new PlainLogContext(_services.Database.Options<PlainLogContext>().Options);
        var untouched = plain.Add(new EventLogEntry { Id = Guid.NewGuid(), EventName = "host.pinged" });
        FluentActions.Invoking(() => fields[0].Fill(new Pinged(), untouched)).Should().NotThrow();
    }

    [Fact]
    public async Task The_history_is_the_event_log_with_the_tenant_and_an_index_to_page_one_tenant_by()
    {
        await using var scope = _services.Scope();
        var model = scope.ServiceProvider.Tenancy().Model;

        var found = TenancyModel.EventLogOf(model);
        found.Should().NotBeNull();
        var (log, letsRowsGo) = found!.Value;
        log.ClrType.Should().Be<EventLogEntry>();
        letsRowsGo.Should().BeFalse("the TestHost keeps every row for good");

        var tenant = log.FindProperty(TenancyEventLogTable.TenantId)!;
        tenant.ClrType.Should().Be<TenantId>("the column is of the application's own tenant id");
        tenant.IsNullable.Should().BeFalse();
        tenant.IsShadowProperty().Should().BeTrue();
        log.GetIndexes().Should().Contain(index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { TenancyEventLogTable.TenantId, nameof(EventLogEntry.RecordedAt) }));
        log.GetDeclaredQueryFilters().Should().BeEmpty("the toolkit's retention reads the table whole, so a reader names the tenant itself");
        TenancyModel.TableOf(log).Should().BeNull("it is the toolkit's table, with Tenancy's column");

        // A module's model has no history, and a period to keep rows for is what lets them go.
        TenancyModel.EventLogOf(scope.ServiceProvider.Widgets().Model).Should().BeNull();
        await using var kept = new KeptForAYearContext(_services.Database.Options<KeptForAYearContext>().Options);
        TenancyModel.EventLogOf(kept.Model)!.Value.LetsRowsGo.Should().BeTrue();
    }

    [Fact]
    public async Task Who_acted_is_what_tenancy_knows_of_the_caller_and_the_toolkits_answer_without_one()
    {
        var identity = Guid.NewGuid();
        var seat = SeatId.CreateSequential();
        var accessor = _services.Provider.GetRequiredService<IActedByAccessor>();
        accessor.Should().BeOfType<TenancyActedByAccessor>("AddTenancy puts it around the toolkit's own");
        _services.Provider.GetServices<IActedByAccessor>().Should().ContainSingle();

        using (Callers.Begin(Caller.User(identity)))
        {
            accessor.Current.Should().Be(new ActedBy("user", identity.ToString("D")), "without a Tenancy caller the toolkit's own answer stands");

            using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.NotSeated)))
            {
                accessor.Current.Should().Be(new ActedBy("user", identity.ToString("D")), "nobody is no actor of Tenancy's");
            }

            using (TenancyCallers.Begin(HostCaller.InSeat(new TenantId(1), seat)))
            {
                accessor.Current.Should().Be(new ActedBy("seat", seat.Value.ToString("D")));
            }
        }

        using (TenancyWork.BeginOperatorIn<TenantId, SeatId>(new TenantId(1), Odette, scope: "operations"))
        {
            accessor.Current.Should().Be(new ActedBy("operator", Odette.ToString("D")));
        }

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(new TenantId(1), scope: "widgets"))
        {
            accessor.Current.Should().Be(new ActedBy("system", "widgets"));
        }

        // An accessor the host registered before Tenancy is the one it wraps; one registered after it takes its place.
        using var before = new TestServices(configure: services => services.AddSingleton<IActedByAccessor>(new Named("the host's")));
        var wrapping = before.Provider.GetRequiredService<IActedByAccessor>();
        wrapping.Should().BeOfType<TenancyActedByAccessor>();
        wrapping.Current.Should().Be(new ActedBy("the host's", null));
        using (TenancyCallers.Begin(HostCaller.InSeat(new TenantId(1), seat)))
        {
            wrapping.Current.Kind.Should().Be("seat");
        }

        var after = new ServiceCollection();
        TestHostTenancy.Add(after);
        TestHostTenancy.Add(after);
        after.Count(descriptor => descriptor.ServiceType == typeof(IActedByAccessor)).Should().Be(1, "registering Tenancy twice wraps once");
        after.AddSingleton<IActedByAccessor>(new Named("the host's own"));
        using var replaced = after.BuildServiceProvider();
        replaced.GetRequiredService<IActedByAccessor>().Current.Kind.Should().Be("the host's own");
    }

    [Fact]
    public void An_accessor_registered_per_scope_is_refused_where_tenancy_is_registered()
    {
        // Tenancy's accessor is one for the application and makes the one it wraps once: a scoped accessor would be
        // made outside any scope, and go on answering for whatever it was first given.
        var scoped = new ServiceCollection();
        scoped.AddScoped<IActedByAccessor, PerRequest>();

        FluentActions.Invoking(() => TestHostTenancy.Add(scoped))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("IActedByAccessor is registered as a scoped service*Register it as a singleton that reads who is acting each time it is asked*");

        // One made each time it is asked for keeps nothing either way, and is wrapped like a singleton.
        var transient = new ServiceCollection();
        transient.AddTransient<IActedByAccessor, PerRequest>();
        TestHostTenancy.Add(transient);
        using var provider = transient.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IActedByAccessor>().Should().BeOfType<TenancyActedByAccessor>().Which.Current.Kind.Should().Be("request");
    }

    /// <summary>Carries out, in a scope of its own, what the operator asked for in <paramref name="tenant"/>.</summary>
    private async Task ForTheOperatorAsync(TenantId tenant, Func<HostTenancy.TenantCommands, Task> act)
    {
        await using var scope = _services.Scope();
        using (TenancyWork.BeginOperatorIn<TenantId, SeatId>(tenant, Odette))
        {
            await act(scope.ServiceProvider.Tenants());
        }
    }

    private async Task<List<Kept>> KeptAsync()
    {
        await using var scope = _services.Scope();
        return await scope.ServiceProvider.Tenancy().Set<EventLogEntry>()
            .Select(entry => new Kept(entry.Id, entry.EventName, EF.Property<TenantId>(entry, TenancyEventLogTable.TenantId), entry.ActedByKind, entry.ActedById, entry.Payload))
            .ToListAsync(Cancellation);
    }

    /// <summary>A row of the history, as a test reads it.</summary>
    private sealed record Kept(Guid Id, string EventName, TenantId Tenant, string ActedByKind, string? ActedById, string Payload);

    /// <summary>An event of the application's own that names no tenant.</summary>
    private sealed record Pinged : DDDToolkit.BaseTypes.DomainEvent;

    /// <summary>An accessor of the host's own, which answers one kind whoever is calling.</summary>
    private sealed class Named(string kind) : IActedByAccessor
    {
        public ActedBy Current => new(kind, null);
    }

    /// <summary>An accessor of the host's own that a host might register per request.</summary>
    private sealed class PerRequest : IActedByAccessor
    {
        public ActedBy Current => new("request", null);
    }

    /// <summary>A context with the toolkit's plain event log: no tenant on its rows.</summary>
    private sealed class PlainLogContext(DbContextOptions<PlainLogContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddEventLog(Database);
    }

    /// <summary>A context whose history lets a row go after a year.</summary>
    private sealed class KeptForAYearContext(DbContextOptions<KeptForAYearContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddTenancyEventLogTable<TenantId>(Database, keepFor: TimeSpan.FromDays(365));

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => configurationBuilder.AddTenancyConverters();
    }
}

/// <summary>The access history, on SQLite in memory.</summary>
public sealed class AccessHistoryTestsOnSqlite() : AccessHistoryTests(TestDatabases.Sqlite);

/// <summary>The access history, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql.</summary>
public sealed class AccessHistoryTestsOnPostgres(PostgresDatabases postgres) : AccessHistoryTests(postgres);
