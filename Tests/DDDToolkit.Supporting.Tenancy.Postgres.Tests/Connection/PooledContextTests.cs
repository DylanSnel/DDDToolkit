using DDDToolkit.Abstractions.Access;
using DDDToolkit.EntityFramework;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Contexts taken from a pool, as a host takes them that runs the parts of one request side by side, each on a
/// context of its own. The options of such a context are built once, from the application's services and not a
/// request's, and one instance serves one caller after another. What the database keeps and answers is asked there as
/// on a request's own context: the store and the questions find that the database keeps the rights, ask its functions
/// as the caller of each command, and keep nothing of the caller before. A module's query with a question in it, over
/// Tenancy's read functions, is one statement on the context taken for it, answered for whoever asks.
/// <para>
/// Such a context saves as any other does. The request's own is one of the pool's, bound to the request's scope: a
/// command through it is checked for whoever rented it, its events go to the outbox with what it changed, the
/// database writes the rights, and what one renter tracked or was refused leaves nothing for the next. One
/// connection is enough for all of it.
/// </para>
/// </summary>
public abstract class PooledContextTests(TenancyPostgres postgres, TenancyNaming names)
{
    /// <summary>Who asks, in which tenant and through which seat, about which unit, and the seats they are answered.</summary>
    private static readonly (Person Person, TenantId Tenant, SeatId Seat, OrganizationUnitId Unit, SeatId[] Holders)[] Asking =
    [
        (Oli, Harbor, Oli.Seat, NorthPier, [Oli.Seat]),
        (Ada, Harbor, Ada.Seat, NorthPier, [Ada.Seat, Seth.Seat, Oli.Seat]),
        (Oli, Orchard, OliInOrchard, OrchardRoot, [OliInOrchard]),
        (Odette, Orchard, Odette.Seat, OrchardRoot, [Odette.Seat, OliInOrchard]),
    ];

    /// <summary>The identity of Pat, whose seat a command of a test adds.</summary>
    private static readonly Guid Pat = Guid.Parse("d0000000-0000-4000-8000-000000000097");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_context_rented_again_answers_the_next_caller_and_keeps_nothing_of_the_one_before()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder), pooled: true);

        await RentedAgainAsync<WidgetContext>(services, recorder);
        await RentedAgainAsync<TestTenancyContext>(services, recorder);
    }

    [Fact]
    public async Task Contexts_of_one_pool_answer_callers_side_by_side_each_for_itself()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database, pooled: true);
        var rented = new System.Collections.Concurrent.ConcurrentBag<DbContext>();

        // Twelve flows of work at once, each a person in a tenant of their own choosing, each asking four times over a
        // context it takes from the pool and gives back: whichever instance a flow gets, and whoever had it before, it
        // is answered for its own caller and tenant.
        var flows = Enumerable.Range(0, 12).Select(index => Task.Run(async () =>
        {
            var asking = Asking[index % Asking.Length];
            var answered = new List<List<SeatId>>();
            for (var round = 0; round < 4; round++)
            {
                answered.Add(await services.BySeat(asking.Person.Identity, asking.Tenant, asking.Seat, scoped => HoldersAsync<WidgetContext>(scoped, asking.Unit, rented.Add)));
            }

            return (asking, answered);
        }));

        foreach (var (asking, answered) in await Task.WhenAll(flows))
        {
            answered.Should().AllSatisfy(holders => holders.Should().BeEquivalentTo(asking.Holders, "{0} asks in tenant {1}", asking.Person.Name, asking.Tenant));
        }

        rented.Distinct().Count().Should().BeLessThan(rented.Count, "the pool handed contexts out again");
    }

    [Fact]
    public async Task A_modules_query_over_the_read_functions_answers_each_caller_on_a_context_of_its_own()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder), pooled: true);
        var rented = new System.Collections.Concurrent.ConcurrentBag<DbContext>();

        // Who asks, and the widgets each may read: at the units where they hold the key, in the tenant they chose.
        (Person Person, TenantId Tenant, SeatId Seat, string[] Widgets)[] reading =
        [
            (Seth, Harbor, Seth.Seat, ["Pump", "Valve"]),
            (Oli, Harbor, Oli.Seat, ["Valve"]),
            (Ada, Harbor, Ada.Seat, ["Gauge", "Pump", "Valve"]),
            (Oli, Orchard, OliInOrchard, ["Crate"]),
            (Eve, Harbor, Eve.Seat, []),
        ];

        // A read as a module makes it where the parts of a request run side by side: a context from the factory for
        // this one query, Tenancy's question composed into it over that same context, and the context given back.
        async Task<List<string>> ReadableAsync(IServiceProvider scoped)
        {
            await using var widgets = await scoped.GetRequiredService<IDbContextFactory<WidgetContext>>().CreateDbContextAsync(Cancellation);
            rented.Add(widgets);

            var held = scoped.Answers().Over(widgets).UnitsWhereIHold(HostCatalogue.WidgetRead);
            return await widgets.Widgets.Where(widget => held.Contains(widget.UnitId)).OrderBy(widget => widget.Name).Select(widget => widget.Name).ToListAsync(Cancellation);
        }

        // Fifteen requests at once, each reading four times: whichever context a read is handed, and whoever had it
        // before, the answer is its own caller's.
        var requests = Enumerable.Range(0, 15).Select(index => Task.Run(async () =>
        {
            var asking = reading[index % reading.Length];
            var answered = new List<List<string>>();
            for (var round = 0; round < 4; round++)
            {
                answered.Add(await services.BySeat(asking.Person.Identity, asking.Tenant, asking.Seat, ReadableAsync));
            }

            return (asking, answered);
        }));

        foreach (var (asking, answered) in await Task.WhenAll(requests))
        {
            answered.Should().AllSatisfy(widgets => widgets.Should().Equal(asking.Widgets, "{0} reads in tenant {1}", asking.Person.Name, asking.Tenant));
        }

        rented.Should().HaveCount(60);
        rented.Distinct().Count().Should().BeLessThan(rented.Count, "the pool handed contexts out again");
        recorder.Sent.Should().HaveCount(60, "each read is one statement, the question inside it")
            .And.OnlyContain(command => command.Text.Contains("tenancy." + TenancyFunctionNames.CallerRights + "()", StringComparison.Ordinal) && command.Caller!.Kind == CallerKind.User);
    }

    [Fact]
    public async Task The_store_asks_the_database_as_the_caller_of_each_command_through_a_pooled_context()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder), pooled: true);
        var rented = new List<DbContext>();

        Task<IReadOnlyList<(SeatId Seat, RoleId Role)>> AdministratorsAsync(IServiceProvider scoped, TenantId tenant)
        {
            rented.Add(scoped.Tenancy());
            return scoped.GetRequiredService<HostTenancy.IStore>().AdministratorsAsync(tenant, DateTimeOffset.UtcNow, Cancellation);
        }

        // The request's own context comes from the pool too, and the store with it. Ada administers Harbor and is told
        // so; Oli, on the context she gave back, manages nothing and is told nothing; Odette, on the same one again, is
        // told who administers Orchard.
        (await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => AdministratorsAsync(scoped, Harbor))).Should().Equal((Ada.Seat, HarborRoles.Administrator));
        (await services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => AdministratorsAsync(scoped, Harbor))).Should().BeEmpty();
        (await services.BySeat(Odette.Identity, Orchard, Odette.Seat, scoped => AdministratorsAsync(scoped, Orchard))).Should().Equal((Odette.Seat, OrchardRoles.Administrator));
        recorder.Sent.Should().HaveCount(3).And.OnlyContain(
            command => command.Text.Contains(TenancyFunctionNames.TenantAdministrators, StringComparison.Ordinal) && command.Caller!.Kind == CallerKind.User,
            "the store found that the database keeps the rights, from the application's services, and asked its function as the signed-in user");

        // System work in the tenant reads the rights itself, on the same context.
        (await services.BySystemIn(Harbor, scoped => AdministratorsAsync(scoped, Harbor))).Should().Equal((Ada.Seat, HarborRoles.Administrator));
        recorder.Sent.Should().HaveCount(4).And.Subject.Last().Text.Should().NotContain(TenancyFunctionNames.TenantAdministrators);

        // The rights a move changes, likewise: Seth, who supervises North, is answered his own and those of the keys
        // that manage access, and Ada after him hers.
        async Task<int> OwnReachesAsync(IServiceProvider scoped, SeatId seat)
        {
            rented.Add(scoped.Tenancy());
            var reaches = await scoped.GetRequiredService<HostTenancy.IStore>().RightsAMoveChangesAsync(
                Harbor, seat, North, NorthPier, scoped.GetRequiredService<Catalogue.TenancyCatalogue>().AccessManagingKeys, DateTimeOffset.UtcNow, Cancellation);
            return reaches.Count(reach => reach.OfCaller);
        }

        var catalogue = services.Provider.GetRequiredService<Catalogue.TenancyCatalogue>();
        (await services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => OwnReachesAsync(scoped, Seth.Seat))).Should().Be(6 + 6, "the six keys of a supervisor, held at North, reach North and North Pier");
        (await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => OwnReachesAsync(scoped, Ada.Seat))).Should().Be(2 * catalogue.LiveKeys.Count, "each of her keys reaches both parents from the root");

        rented.Distinct().Should().ContainSingle("each request was handed the context the one before gave back");

        // And the checks a host runs at start-up find a context that comes from a pool.
        TenancyPostgresChecks.EnsureExplicitCallers(services.Provider);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);
    }

    [Fact]
    public async Task A_command_through_a_pooled_context_is_checked_and_writes_its_events()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder), pooled: true);
        var rented = new List<DbContext>();

        // The request's own context, the unit of work of its command, is one of the pool's.
        Task<T> As<T>(Person person, Func<IServiceProvider, Task<T>> act) => services.BySeat(person.Identity, Harbor, person.Seat, scoped =>
        {
            var context = scoped.Tenancy();
            context.IsPooled().Should().BeTrue();
            rented.Add(context);
            return act(scoped);
        });

        // Ada, who administers Harbor, adds a seat, places it and gives it a role: three commands, three requests.
        var seat = await As(Ada, scoped => scoped.Seats().AddSeatAsync(Pat, Cancellation, configure: seat => seat.Rename("Pat")));
        await As(Ada, async scoped =>
        {
            await scoped.Seats().PlaceAsync(seat, North, primary: true, Cancellation);
            return true;
        });
        await As(Ada, async scoped =>
        {
            await scoped.Seats().GrantAsync(seat, North, HarborRoles.Watcher, until: null, reason: null, Cancellation);
            return true;
        });

        // Oli, on the context she gave back, manages no seats: the use case asks the database as him, and refuses.
        var refused = await RefusedAsync(TenancyRefusals.NotPermitted, () => As(Oli, scoped => scoped.Seats().AddSeatAsync(Guid.NewGuid(), Cancellation, configure: seat => seat.Rename("Uninvited"))));
        refused.Arguments["Key"].Should().Be(Catalogue.TenancyKeys.SeatsManage);

        rented.Should().HaveCount(4);
        rented.Distinct().Should().ContainSingle("each request was handed the context the one before gave back");

        // Each save stored its events with what it changed, in the same transaction.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ListAsync<string>(
                "SELECT \"EventName\" FROM ddd.\"OutboxMessages\" WHERE \"AggregateId\" ILIKE $1 ORDER BY \"CreatedAt\", \"EventName\"",
                Cancellation,
                "%" + seat.Value + "%"))
            .Should().Equal("tenancy.seat-added", "tenancy.seat-placed", "tenancy.organization-role-granted");

        // The rights the role gives are there, and no command of the application wrote them: the database did.
        (await owner.ListAsync<string>("SELECT \"Key\" FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, seat.Value, HarborRoles.Watcher.Value))
            .Should().Equal(HostCatalogue.WidgetRead);
        recorder.Sent.Should().Contain(command => command.Text.StartsWith("INSERT INTO", StringComparison.Ordinal), "the commands wrote");
        recorder.Sent.Where(command => !command.Text.StartsWith("SELECT", StringComparison.Ordinal))
            .Should().NotContain(command => command.Text.Contains(names.Shown("SeatRights"), StringComparison.Ordinal), "the save leaves the rights to the database");

        // And the refused command left nothing.
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Seats\" WHERE \"DisplayName\" = $1", Cancellation, "Uninvited")).Should().Be(0);
    }

    [Fact]
    public async Task A_row_of_another_tenant_is_refused_on_a_context_its_tenant_just_gave_back()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder), pooled: true);
        var rented = new List<DbContext>();

        // Odette, who administers Orchard, saves a widget of Orchard's on her request's context, and gives it back.
        await services.BySeat(Odette.Identity, Orchard, Odette.Seat, async scoped =>
        {
            rented.Add(scoped.Widgets());
            scoped.Widgets().Widgets.Add(new Widget(WidgetId.CreateSequential(), Orchard, OrchardRoot, "Barrel"));
            await scoped.Widgets().SaveChangesAsync(Cancellation);
        });

        // Ada, in Harbor, is handed that instance. A widget of Orchard's is not hers to save, whoever saved one on
        // this context a moment ago: the save check reads the caller of each save, and refuses before a statement is sent.
        recorder.Clear();
        await RefusedAsync(TenancyRefusals.OtherTenant, () => services.BySeat(Ada.Identity, Harbor, Ada.Seat, async scoped =>
        {
            var widgets = scoped.Widgets();
            rented.Add(widgets);
            widgets.ChangeTracker.Entries().Should().BeEmpty("what the renter before tracked left with her");

            widgets.Widgets.Add(new Widget(WidgetId.CreateSequential(), Orchard, OrchardRoot, "Stowaway"));
            await widgets.SaveChangesAsync(Cancellation);
        }));
        recorder.Sent.Should().BeEmpty("the check comes before the first statement");

        // A widget of her own tenant she saves, on the same instance once more: the refusal left nothing on it.
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, async scoped =>
        {
            var widgets = scoped.Widgets();
            rented.Add(widgets);
            widgets.ChangeTracker.Entries().Should().BeEmpty("the refused widget left with the request that added it");

            widgets.Widgets.Add(new Widget(WidgetId.CreateSequential(), Harbor, North, "Winch"));
            await widgets.SaveChangesAsync(Cancellation);
        });

        rented.Should().HaveCount(3);
        rented.Distinct().Should().ContainSingle("each request was handed the context the one before gave back");

        // Each reads her own tenant's widgets, and no stowaway among them.
        Task<List<string>> WidgetsAsync(IServiceProvider scoped) => scoped.Widgets().Widgets.OrderBy(widget => widget.Name).Select(widget => widget.Name).ToListAsync(Cancellation);
        (await services.BySeat(Odette.Identity, Orchard, Odette.Seat, WidgetsAsync)).Should().Equal("Barrel", "Crate");
        (await services.BySeat(Ada.Identity, Harbor, Ada.Seat, WidgetsAsync)).Should().Equal("Gauge", "Pump", "Valve", "Winch");
    }

    [Fact]
    public async Task Rows_one_renter_tracked_are_gone_for_the_next()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database, pooled: true);
        var rented = new List<DbContext>();

        // Ada loads Oli's seat on her request's context, changes it, and ends the request without saving.
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, async scoped =>
        {
            var context = scoped.Tenancy();
            rented.Add(context);

            var seat = await context.Set<HostSeat>().AsTracking().SingleAsync(candidate => candidate.Id == Oli.Seat, Cancellation);
            seat.Rename("Renamed and never saved");
            seat.Suspend();
            context.ChangeTracker.Entries<HostSeat>().Should().ContainSingle();
            seat.DomainEvents.Should().NotBeEmpty("the change waits on the seat, with its event");
        });

        // Odette, in Orchard, is handed that instance with nothing tracked on it, and her commands save her changes alone.
        await services.BySeat(Odette.Identity, Orchard, Odette.Seat, async scoped =>
        {
            var context = scoped.Tenancy();
            rented.Add(context);
            context.ChangeTracker.Entries().Should().BeEmpty("what the renter before tracked left with her");

            await scoped.RenameSeatAsync(OliInOrchard, "Oliver", Cancellation);
            await scoped.Seats().SuspendAsync(OliInOrchard, Cancellation);
        });

        rented.Distinct().Should().ContainSingle("the second request was handed the context the first gave back");

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ListAsync<string>("SELECT \"DisplayName\" FROM tenancy.\"Seats\" WHERE \"Id\" = ANY ($1) ORDER BY 1", Cancellation, new[] { Oli.Seat.Value, OliInOrchard.Value }))
            .Should().Equal("Oli", "Oliver");
        (await owner.ListAsync<string>("SELECT \"AggregateId\" FROM ddd.\"OutboxMessages\" WHERE \"EventName\" = $1", Cancellation, "tenancy.seat-suspended"))
            .Should().ContainSingle(id => id.Contains(OliInOrchard.Value.ToString(), StringComparison.OrdinalIgnoreCase), "Odette's change was saved with its event")
            .And.NotContain(id => id.Contains(Oli.Seat.Value.ToString(), StringComparison.OrdinalIgnoreCase), "the event of the change that was never saved left with it");
    }

    [Fact]
    public async Task A_pooled_context_is_wired_like_any_other()
    {
        // Nothing is asked of a database here: how a context is wired is in its options.
        await using var services = new TenancyServices(new TestDatabase("Host=wiring-only", "Host=wiring-only", names), pooled: true);
        await using var scope = services.Scope();

        await WiredAsync<TestTenancyContext>(scope.ServiceProvider);
        await WiredAsync<WidgetContext>(scope.ServiceProvider);

        static async Task WiredAsync<TContext>(IServiceProvider scoped)
            where TContext : DbContext
        {
            var ofTheScope = scoped.GetRequiredService<TContext>();
            await using var ofTheFactory = await scoped.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContextAsync(Cancellation);

            ofTheFactory.Should().NotBeSameAs(ofTheScope, "the scope holds its own while a read takes another");
            foreach (var context in new DbContext[] { ofTheScope, ofTheFactory })
            {
                context.IsPooled().Should().BeTrue();
                FluentActions.Invoking(() => TenancyChecks.EnsureWired(context)).Should().NotThrow("UseDDDToolkit puts the save check after the toolkit's interceptors in the pool's options too");
            }

            // One set of options for every context of the pool: the same interceptors, in the same order.
            ofTheFactory.GetService<IDbContextOptions>().Should().BeSameAs(ofTheScope.GetService<IDbContextOptions>());
            scoped.GetRequiredService<TContext>().Should().BeSameAs(ofTheScope, "a scope has one context of its own");
        }
    }

    [Fact]
    public async Task The_use_cases_complete_on_a_pool_of_one_connection()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // One connection for everything the application does, and five seconds to wait for it: a use case that asked
        // for a second connection while it held the first would wait those out, and fail.
        var one = new NpgsqlConnectionStringBuilder(database.ConnectionString) { MaxPoolSize = 1, Timeout = 5 }.ConnectionString;
        await using var services = new TenancyServices(database with { ConnectionString = one }, pooled: true);

        // Both contexts connect through that one pool, the request's own and one a read takes from the factory.
        await services.InScopeAsync(async scoped =>
        {
            await using var read = await scoped.GetRequiredService<IDbContextFactory<WidgetContext>>().CreateDbContextAsync(Cancellation);
            foreach (var context in new DbContext[] { scoped.Tenancy(), scoped.Widgets(), read })
            {
                // Pooled, or the limit would bound nothing: without a pool every open is a connection of its own.
                var connects = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());
                (connects.Pooling, connects.MaxPoolSize, connects.Timeout).Should().Be((true, 1, 5), "{0} connects through the pool of one", context.GetType().Name);
            }
        });

        // Every command of the use cases, once, each in a scope of its own, as a request is: seats, placements and
        // grants; units added, moved and archived, which changes what every right under them reaches; roles made
        // and their keys changed, which changes the rights of everyone who holds them; tenants provisioned,
        // suspended and closed.
        await EveryUseCase.RunAsync(services, Cancellation);

        // Three questions: who the caller is, with every key it holds and where; who holds a key at a unit, inside a
        // query on a context taken from the factory; and where the caller holds one, inside a module's own query.
        var catalogue = services.Provider.GetRequiredService<Catalogue.TenancyCatalogue>();
        var overview = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Directory().WhoAmIAsync(Cancellation));
        overview.Seat.Id.Should().Be(Ada.Seat);
        overview.Keys.Should().HaveCount(catalogue.LiveKeys.Count).And.OnlyContain(reach => reach.WholeTenant);

        (await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => HoldersAsync<WidgetContext>(scoped, HarborRoot, _ => { }))).Should().Equal(Ada.Seat);

        var readable = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, async scoped =>
        {
            await using var widgets = await scoped.GetRequiredService<IDbContextFactory<WidgetContext>>().CreateDbContextAsync(Cancellation);
            var held = scoped.Answers().Over(widgets).UnitsWhereIHold(HostCatalogue.WidgetRead);
            return await widgets.Widgets.Where(widget => held.Contains(widget.UnitId)).OrderBy(widget => widget.Name).Select(widget => widget.Name).ToListAsync(Cancellation);
        });
        readable.Should().Equal("Gauge", "Pump", "Valve");

        // What a host checks before its first request completes on that one connection as well.
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);
    }

    /// <summary>
    /// Asks who holds the key to read widgets, one caller after another over the one context of
    /// <typeparamref name="TContext"/> the pool hands out, gives back and hands out again: a seat that manages nothing,
    /// an administrator, the same person in another tenant, that tenant's administrator, system work, and the first
    /// seat once more.
    /// </summary>
    private static async Task RentedAgainAsync<TContext>(TenancyServices services, CommandRecorder recorder)
        where TContext : DbContext
    {
        var rented = new List<DbContext>();
        recorder.Clear();

        foreach (var asking in Asking)
        {
            (await services.BySeat(asking.Person.Identity, asking.Tenant, asking.Seat, scoped => HoldersAsync<TContext>(scoped, asking.Unit, rented.Add)))
                .Should().BeEquivalentTo(asking.Holders, "{0} asks in tenant {1}, over {2}", asking.Person.Name, asking.Tenant, typeof(TContext).Name);
        }

        // For a seat, the database's function answers, in one statement each, sent as that person.
        recorder.Sent.Should().HaveCount(Asking.Length);
        recorder.Sent.Select(command => command.Caller!.UserId).Should().Equal(Asking.Select(asking => (Guid?)asking.Person.Identity));
        recorder.Sent.Should().OnlyContain(command => command.Text.Contains(TenancyFunctionNames.SeatsHoldingAt, StringComparison.Ordinal),
            "the context found that the database keeps the rights, from the application's services its options were built with");

        // System work in the tenant reads every right of it, and works the answer out; the seat after it is answered
        // as a seat again.
        (await services.BySystemIn(Harbor, scoped => HoldersAsync<TContext>(scoped, NorthPier, rented.Add))).Should().BeEquivalentTo([Ada.Seat, Seth.Seat, Oli.Seat]);
        recorder.Sent.Last().Text.Should().NotContain(TenancyFunctionNames.SeatsHoldingAt);
        (await services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => HoldersAsync<TContext>(scoped, NorthPier, rented.Add))).Should().Equal(Oli.Seat);

        rented.Should().HaveCount(Asking.Length + 2);
        rented.Distinct().Should().ContainSingle("each caller was handed the context the one before gave back");
    }

    /// <summary>Runs <paramref name="command"/>, expecting it to be refused with <paramref name="code"/>.</summary>
    private static async Task<RefusalException> RefusedAsync(string code, Func<Task> command)
    {
        var refusal = (await command.Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(code);
        return refusal;
    }

    /// <summary>
    /// The seats that hold the key to read widgets at <paramref name="unit"/>, asked inside a query of a context taken
    /// from the pool for this one question, and given back after it.
    /// </summary>
    private static async Task<List<SeatId>> HoldersAsync<TContext>(IServiceProvider scoped, OrganizationUnitId unit, Action<DbContext> rented)
        where TContext : DbContext
    {
        await using var context = await scoped.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContextAsync(Cancellation);
        rented(context);

        var holders = scoped.Answers().Over(context).SeatsHoldingAt(HostCatalogue.WidgetRead, unit);
        return await context.Set<SeatRow<TenantId, SeatId>>().Where(seat => holders.Contains(seat.Id)).Select(seat => seat.Id).ToListAsync(Cancellation);
    }
}

/// <summary>Contexts taken from a pool, under the names Entity Framework gives the tables and columns.</summary>
public sealed class PooledContextTestsOnDefaultNames(TenancyPostgres postgres) : PooledContextTests(postgres, TenancyNaming.Default);

/// <summary>Contexts taken from a pool, under snake_case names with enums stored as snake_case text.</summary>
public sealed class PooledContextTestsOnSnakeCase(TenancyPostgres postgres) : PooledContextTests(postgres, TenancyNaming.SnakeCase);
