using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// Tenancy's access check over a database: what each case of <see cref="TenancyRequirement"/> lets through and
/// what it refuses with, asked the way anything that stands in front of a module's handlers asks it, through the
/// module's set of checks. Only a key for the whole tenant reads, in one statement, over the context the check
/// was registered for.
/// </summary>
public abstract class TenancyAccessCheckTests(TestDatabases databases) : IAsyncLifetime
{
    private const string OperatorRole = "operator";

    private TestServices _services = null!;
    private HostTenancy.ProvisionedTenant _harbor = null!;
    private OrganizationUnitId _north;
    private SeatId _seth;
    private SeatId _wes;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A request of the tenancy module that declares <paramref name="Requires"/>.</summary>
    private sealed record Declaring(AccessRequirement Requires) : IHostRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => Requires;
    }

    /// <summary>What a request of the widgets' module implements: a module of its own, with a context of its own.</summary>
    private interface IWidgetRequest : IRequireAccess;

    /// <summary>A request of the widgets' module that declares <paramref name="Requires"/>.</summary>
    private sealed record OfWidgetsDeclaring(AccessRequirement Requires) : IWidgetRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => Requires;
    }

    /// <summary>A case that is nobody's here.</summary>
    private sealed record OnWidget(Guid Widget) : AccessRequirement;

    public async ValueTask InitializeAsync()
    {
        _services = await ServicesAsync();

        // Harbor: Ada administers it. Seth supervises North, where he manages seats; Wes only looks at widgets.
        _harbor = await _services.ProvisionAsync("harbor");
        _north = await _services.AddUnitAsync(_harbor.Tenant, _harbor.RootUnit, "North");
        _seth = await _services.SeatAtAsync(_harbor, "Seth", _north, HostCatalogue.SupervisorPack);
        _wes = await _services.SeatAtAsync(_harbor, "Wes", _harbor.RootUnit, HostCatalogue.WatcherPack);
        _services.Commands.Reset();
    }

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Services over a database of their own, with <see cref="OperatorRole"/> listed as an operator's and the
    /// widgets' module holding its requests to Tenancy's check over its own context.
    /// </summary>
    private async Task<TestServices> ServicesAsync(bool pooled = false)
    {
        var services = new TestServices(
            database: await databases.CreateAsync(Cancellation),
            ownsDatabase: true,
            pooled: pooled,

            // A module that only asks Tenancy declares none of its classes, and writes the four ids out.
            afterTenancy: registered => registered.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, IWidgetRequest, TestWidgetContext>());
        services.Provider.GetRequiredService<TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId>>().OperatorTokenRoles.Add(OperatorRole);
        return services;
    }

    // ------------------------------------------------------------------ the cases

    [Fact]
    public async Task Who_is_calling_is_the_core_s_to_decide_in_a_module_of_tenancy_s_too()
    {
        var signedIn = new Declaring(AccessRequirement.SignedIn());
        var systemWork = new Declaring(AccessRequirement.RequiresSystemWork());

        // A person who signed in and has no seat yet, as when accepting an invitation: Tenancy is not asked.
        await using (var scope = _services.Scope())
        using (Callers.Begin(Caller.User(Guid.NewGuid())))
        using (TenancyCallers.BeginNone())
        {
            await RequireAsync(scope.ServiceProvider, signedIn);
            await RefusedByTheToolkitAsync(ToolkitRefusals.SystemOnly, () => RequireAsync(scope.ServiceProvider, systemWork), "no user is the application itself");
        }

        await using (var scope = _services.Scope())
        using (Callers.Begin(Caller.Anonymous))
        {
            await RefusedByTheToolkitAsync(ToolkitRefusals.NotSignedIn, () => RequireAsync(scope.ServiceProvider, signedIn));
        }

        // System work passes in its tenant, and outside any as well: the requirement says who may send a request,
        // not where the work acts. What acts in a tenant asks Tenancy for it, which refuses system work outside any.
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            await InScope(systemWork);
            await RefusedByTheToolkitAsync(ToolkitRefusals.NotSignedIn, () => InScope(signedIn), "the application's own work is nobody's sign-in");
        }

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(_harbor.Tenant))
        {
            await InScope(systemWork);
        }

        _services.Commands.Commands.Should().BeEmpty("who is calling is kept with the flow of work: nothing is read for it");
    }

    [Fact]
    public async Task A_request_in_a_tenant_takes_a_seat_or_system_work_there_and_refuses_nobody_as_nobody()
    {
        var inTenant = new Declaring(TenancyAccess.InTenant());

        await _services.BySeat(_harbor.Tenant, _wes, scoped => RequireAsync(scoped, inTenant));
        await _services.BySystemIn(_harbor.Tenant, scoped => RequireAsync(scoped, inTenant));

        await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => ByNobody(inTenant));
        await Refused.WithCodeAsync(TenancyRefusals.SeatSuspended, () => ByNobody(inTenant, TenancyRefusals.SeatSuspended), "nobody is refused with the reason it was given");

        // System work outside any tenant is a mistake in the calling code, not a refusal a client could act on.
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            await FluentActions.Awaiting(() => InScope(inTenant)).Should().ThrowAsync<InvalidOperationException>();
        }

        _services.Commands.Commands.Should().BeEmpty("who is calling is kept with the flow of work: nothing is read for it");
    }


    [Fact]
    public async Task A_key_for_the_whole_tenant_is_asked_at_the_root_in_one_statement()
    {
        var seats = new Declaring(TenancyAccess.ForTheWholeTenant(TenancyKeys.SeatsManage));

        // Ada administers the tenant: every key, at the root.
        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => RequireAsync(scoped, seats));
        _services.Commands.Count.Should().Be(1, "the key is asked in one statement");
        _services.Commands.Sent[0].Writes.Should().BeFalse();
        _services.Commands.Sent[0].Context.Should().BeOfType<TestTenancyContext>("over the context the check was registered for");

        // Seth manages seats at North, which is not the whole tenant.
        _services.Commands.Reset();
        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _seth, scoped => RequireAsync(scoped, seats)));
        refusal.Arguments["Key"].Should().Be(TenancyKeys.SeatsManage, "the refusal names the key, as the package's own use cases name it");
        _services.Commands.Count.Should().Be(1);

        // Wes holds nothing of the kind anywhere.
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _wes, scoped => RequireAsync(scoped, seats)));

        // System work in the tenant holds every key there.
        await _services.BySystemIn(_harbor.Tenant, scoped => RequireAsync(scoped, seats));
    }

    [Fact]
    public async Task Nobody_is_refused_as_nobody_before_anything_is_read_for_a_key()
    {
        var seats = new Declaring(TenancyAccess.ForTheWholeTenant(TenancyKeys.SeatsManage));

        await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => ByNobody(seats));

        _services.Commands.Commands.Should().BeEmpty("no context is made, and no statement sent, for a caller that is nobody");
    }

    [Fact]
    public async Task A_key_the_catalogue_does_not_know_is_a_mistake_in_the_request_whoever_calls()
    {
        var unknown = new Declaring(TenancyAccess.ForTheWholeTenant("widget.polish"));

        await FluentActions.Awaiting(() => _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => RequireAsync(scoped, unknown)))
            .Should().ThrowAsync<ArgumentException>("a request that required a key nobody can hold would refuse everyone, so it fails as the bug it is");
    }

    [Fact]
    public async Task A_key_at_a_unit_is_asked_there_and_above_in_one_statement()
    {
        static Declaring At(OrganizationUnitId unit) => new(TenancyAccess.AtUnit(TenancyKeys.SeatsManage, unit));

        // Seth manages seats at North: there the key is his, asked in one statement over the context the check was registered for.
        await _services.BySeat(_harbor.Tenant, _seth, scoped => RequireAsync(scoped, At(_north)));
        _services.Commands.Count.Should().Be(1, "the key is asked in one statement");
        _services.Commands.Sent[0].Writes.Should().BeFalse();
        _services.Commands.Sent[0].Context.Should().BeOfType<TestTenancyContext>();

        // At the root, above where he holds it, it is not: the refusal names the key and the unit, as the package's own use cases do.
        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _seth, scoped => RequireAsync(scoped, At(_harbor.RootUnit))));
        refusal.Arguments["Key"].Should().Be(TenancyKeys.SeatsManage);
        refusal.Arguments["Unit"].Should().Be(_harbor.RootUnit);

        // Ada holds it at the root, and so at North below it. Wes holds nothing of the kind anywhere.
        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => RequireAsync(scoped, At(_north)));
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _wes, scoped => RequireAsync(scoped, At(_north))));

        // A unit the tenant does not have is one where nobody holds anything, its administrator and system work included.
        var nowhere = OrganizationUnitId.CreateSequential();
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => RequireAsync(scoped, At(nowhere))));
        await _services.BySystemIn(_harbor.Tenant, scoped => RequireAsync(scoped, At(_north)));
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySystemIn(_harbor.Tenant, scoped => RequireAsync(scoped, At(nowhere))));

        // Nobody is refused as nobody, and nothing is read for it.
        _services.Commands.Reset();
        await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => ByNobody(At(_north)));
        _services.Commands.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task A_key_at_a_unit_known_by_another_type_than_the_application_s_units_stops_the_request()
    {
        var elsewhere = new Declaring(TenancyAccess.AtUnit(TenancyKeys.SeatsManage, _seth));

        (await FluentActions.Awaiting(() => _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => RequireAsync(scoped, elsewhere)))
                .Should().ThrowAsync<InvalidOperationException>("a case the check cannot decide lets nobody through, the administrator included"))
            .WithMessage("*Tenancy's access check does not decide: the application's units are known by OrganizationUnitId*");
    }

    [Fact]
    public async Task Only_an_operator_passes_what_is_for_operators()
    {
        var operators = new Declaring(TenancyAccess.RequiresOperator());
        var odette = Guid.NewGuid();

        // An operator works in no tenant: the toolkit's caller says who it is, and to Tenancy it is nobody.
        await using (var scope = _services.Scope())
        using (Callers.Begin(Caller.User(odette, OperatorRole)))
        using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.NotSeated)))
        {
            await RequireAsync(scope.ServiceProvider, operators);
        }

        // Everybody else: the tenant's administrator, signed in as a user; a user with another token role; one
        // with the operators' role and no verified identity; nobody at all; and system work in the tenant.
        (Caller? Toolkit, ITenancyCaller Tenancy)[] others =
        [
            (Caller.User(Guid.NewGuid()), HostCaller.InSeat(_harbor.Tenant, _harbor.AdminSeat)),
            (Caller.User(odette, "analyst"), HostCaller.Nobody(TenancyRefusals.NotSeated)),
            (Caller.User(null, OperatorRole), HostCaller.Nobody(TenancyRefusals.NotSeated)),
            (Caller.Anonymous, HostCaller.Nobody(TenancyRefusals.NotSeated)),
            (null, HostCaller.SystemIn(_harbor.Tenant)),
        ];
        foreach (var (toolkit, tenancy) in others)
        {
            await using var scope = _services.Scope();
            using (toolkit is null ? null : Callers.Begin(toolkit))
            using (TenancyCallers.Begin(tenancy))
            {
                var refusal = await Refused.WithCodeAsync(TenancyRefusals.OperatorsOnly, () => RequireAsync(scope.ServiceProvider, operators), $"{toolkit?.ToString() ?? "system work"} is no operator");
                refusal.Kind.Should().Be(DDDToolkit.Exceptions.RefusalKind.NotPermitted);
            }
        }

        _services.Commands.Commands.Should().BeEmpty("an operator is told by its token's role: nothing is read for it");
    }

    // ------------------------------------------------------------------ whose cases, and whose context

    [Fact]
    public async Task The_check_decides_tenancy_s_cases_and_no_others()
    {
        await using var scope = _services.Scope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IHostRequest>>();

        AccessRequirement[] ofTenancy =
        [
            TenancyAccess.InTenant(),
            TenancyAccess.ForTheWholeTenant(TenancyKeys.SeatsManage),
            TenancyAccess.RequiresOperator(),
            TenancyAccess.AtUnit(TenancyKeys.SeatsManage, _north),
        ];
        ofTenancy.Should().OnlyContain(requirement => checks.Decides(requirement));
        checks.Decides(new OnWidget(Guid.NewGuid())).Should().BeFalse("a module's own case takes a check of the module's own");

        using (TenancyCallers.Begin(HostCaller.SystemIn(_harbor.Tenant)))
        {
            (await FluentActions.Awaiting(() => checks.RequireAsync(new Declaring(new OnWidget(Guid.NewGuid())), Cancellation).AsTask())
                    .Should().ThrowAsync<InvalidOperationException>("a requirement nothing checks lets nobody through, system work included"))
                .WithMessage("*OnWidget*none of the access checks registered for*IHostRequest*");
        }
    }

    [Fact]
    public async Task A_case_of_tenancy_s_in_a_module_without_its_check_names_the_registration_that_adds_it()
    {
        // A module that registered no check of Tenancy's for its requests: the message says which call adds it,
        // not only that some check is missing.
        var checks = new AccessChecks<IWidgetRequest>([]);

        (await FluentActions.Awaiting(() => checks.RequireAsync(new OfWidgetsDeclaring(TenancyAccess.InTenant()), Cancellation).AsTask())
                .Should().ThrowAsync<InvalidOperationException>())
            .WithMessage(
                "*declares 'TenancyRequirement.InTenant', which none of the access checks registered for TenancyAccessCheckTests.IWidgetRequest decides. "
                + "A requirement nothing checks lets nobody through: register the check that decides it with "
                + "services.AddTenancyAccess<TenancyAccessCheckTests.IWidgetRequest, TContext>(), with the module's context for TContext.");
    }

    [Fact]
    public async Task A_module_is_checked_over_its_own_context()
    {
        var seats = new OfWidgetsDeclaring(TenancyAccess.ForTheWholeTenant(TenancyKeys.SeatsManage));

        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => scoped.GetRequiredService<AccessChecks<IWidgetRequest>>().RequireAsync(seats, Cancellation).AsTask());

        _services.Commands.Count.Should().Be(1);
        _services.Commands.Sent[0].Context.Should().BeOfType<TestWidgetContext>("the widgets' module asks over the read model its own context maps, and names no context of Tenancy's");

        await Refused.WithCodeAsync(
            TenancyRefusals.NotPermitted,
            () => _services.BySeat(_harbor.Tenant, _seth, scoped => scoped.GetRequiredService<AccessChecks<IWidgetRequest>>().RequireAsync(seats, Cancellation).AsTask()));
    }

    [Fact]
    public async Task Without_a_factory_the_key_is_asked_on_the_scope_s_own_context()
    {
        var seats = new Declaring(TenancyAccess.ForTheWholeTenant(TenancyKeys.SeatsManage));

        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, async scoped =>
        {
            await RequireAsync(scoped, seats);
            _services.Commands.Sent.Should().ContainSingle().Which.Context.Should().BeSameAs(scoped.Tenancy());
        });
    }

    [Fact]
    public async Task With_a_factory_the_key_is_asked_on_a_context_of_its_own()
    {
        using var pooled = await ServicesAsync(pooled: true);
        var harbor = await pooled.ProvisionAsync("harbor");
        var seats = new Declaring(TenancyAccess.ForTheWholeTenant(TenancyKeys.SeatsManage));

        await pooled.BySeat(harbor.Tenant, harbor.AdminSeat, async scoped =>
        {
            // The scope's own context, the unit of work of its commands, rented before the check runs.
            var ofTheScope = scoped.Tenancy();
            pooled.Commands.Reset();

            // Two requests of one scope, checked side by side: each on a context of its own, taken from the pool.
            await Task.WhenAll(RequireAsync(scoped, seats), RequireAsync(scoped, seats));

            pooled.Commands.Sent.Should().HaveCount(2);
            pooled.Commands.Sent.Should().OnlyContain(sent => sent.Context is TestTenancyContext && !ReferenceEquals(sent.Context, ofTheScope), "a check never reads on the request's unit of work");
            ofTheScope.ChangeTracker.Entries().Should().BeEmpty();
        });
    }

    [Fact]
    public void Adding_the_check_for_an_interface_twice_adds_it_once()
    {
        var services = new ServiceCollection();
        services.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, IWidgetRequest, TestWidgetContext>();
        var registered = services.Count;

        services.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, IWidgetRequest, TestWidgetContext>();

        services.Should().HaveCount(registered);
        services.Should().OnlyContain(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped, "the check, the set and what a check keeps live as long as a request");
        FluentActions.Invoking(() => TenancyEntityFrameworkServiceCollectionExtensions.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, IWidgetRequest, TestWidgetContext>(null!))
            .Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------ helpers

    private static Task RequireAsync(IServiceProvider scoped, Declaring request)
        => scoped.GetRequiredService<AccessChecks<IHostRequest>>().RequireAsync(request, Cancellation).AsTask();

    /// <summary>Asserts that <paramref name="act"/> is refused with one of the toolkit's own codes, which Tenancy's table does not hold.</summary>
    private static async Task RefusedByTheToolkitAsync(string code, Func<Task> act, string because = "")
        => (await FluentActions.Awaiting(act).Should().ThrowAsync<RefusalException>(because)).Which.Code.Should().Be(code, because);

    /// <summary>Holds <paramref name="request"/> to what it declares in a scope of its own, as whoever the caller begun around it is.</summary>
    private async Task InScope(Declaring request)
    {
        await using var scope = _services.Scope();
        await RequireAsync(scope.ServiceProvider, request);
    }

    /// <summary>Holds <paramref name="request"/> to what it declares as a caller that is nobody to Tenancy, for <paramref name="reason"/>.</summary>
    private async Task ByNobody(Declaring request, string reason = TenancyRefusals.NotSeated)
    {
        using (TenancyCallers.Begin(HostCaller.Nobody(reason)))
        {
            await InScope(request);
        }
    }
}

/// <summary>Tenancy's access check, on SQLite in memory.</summary>
public sealed class TenancyAccessCheckTestsOnSqlite() : TenancyAccessCheckTests(TestDatabases.Sqlite);

/// <summary>Tenancy's access check, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql.</summary>
public sealed class TenancyAccessCheckTestsOnPostgres(PostgresDatabases postgres) : TenancyAccessCheckTests(postgres);
