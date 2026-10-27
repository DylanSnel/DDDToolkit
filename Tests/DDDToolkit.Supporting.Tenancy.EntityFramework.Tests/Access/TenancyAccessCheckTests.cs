using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// Tenancy's access check over a database: what each case of <see cref="TenancyRequirement"/> lets through and
/// what it refuses with, asked the way anything that stands in front of a module's handlers asks it, through the
/// module's set of checks. Only a permission for the whole tenant reads, in one statement, over the context the check
/// was registered for.
/// </summary>
public abstract class TenancyAccessCheckTests(TestDatabases databases) : IAsyncLifetime
{
    private const string OperatorRole = "operator";

    private TestServices _services = null!;
    private ProvisionedTenant _harbor = null!;
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
        _seth = await _services.SeatAtAsync(_harbor, "Seth", _north, HostCatalogue.SupervisorStarterRole);
        _wes = await _services.SeatAtAsync(_harbor, "Wes", _harbor.RootUnit, HostCatalogue.WatcherStarterRole);
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
            afterTenancy: registered => registered.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, TenancyAccessManager, IWidgetRequest, TestWidgetContext>());
        services.Provider.GetRequiredService<TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId>>().OperatorTokenRoles.Add(OperatorRole);
        return services;
    }

    // ------------------------------------------------------------------ the cases

    [Fact]
    public async Task Who_is_calling_is_the_core_s_to_decide_in_a_module_of_tenancy_s_too()
    {
        var signedIn = new Declaring(AccessRequirement.SignedIn());
        var inTenant = new Declaring(TenancyAccess.InTenant());

        // A person who signed in and has no seat yet, as when accepting an invitation: Tenancy is not asked.
        await using (var scope = _services.Scope())
        using (Callers.Begin(Caller.User(Guid.NewGuid())))
        using (TenancyCallers.BeginNone())
        {
            await RequireAsync(scope.ServiceProvider, signedIn);
        }

        await using (var scope = _services.Scope())
        using (Callers.Begin(Caller.Anonymous))
        {
            await RefusedByTheToolkitAsync(ToolkitRefusals.NotSignedIn, () => RequireAsync(scope.ServiceProvider, signedIn));
        }

        // System work is no requirement but a scope, begun around what it sends: a request it sends in a tenant says
        // who may send it there, a caller in the tenant, and passes as the system work in that tenant. It is nobody's
        // sign-in, so a request for a signed-in user refuses it.
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            await RefusedByTheToolkitAsync(ToolkitRefusals.NotSignedIn, () => InScope(signedIn), "the application's own work is nobody's sign-in");
        }

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(_harbor.Tenant))
        {
            await InScope(inTenant);
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
    public async Task A_permission_for_the_whole_tenant_is_asked_at_the_root_in_one_statement()
    {
        var seats = new Declaring(TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));

        // Ada administers the tenant: every permission, at the root.
        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => RequireAsync(scoped, seats));
        _services.Commands.Count.Should().Be(1, "the permission is asked in one statement");
        _services.Commands.Sent[0].Writes.Should().BeFalse();
        _services.Commands.Sent[0].Context.Should().BeOfType<TestTenancyContext>("over the context the check was registered for");

        // Seth manages seats at North, which is not the whole tenant.
        _services.Commands.Reset();
        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _seth, scoped => RequireAsync(scoped, seats)));
        refusal.Arguments["Permission"].Should().Be(TenancyPermissions.SeatsManage, "the refusal names the permission, as the package's own use cases name it");
        _services.Commands.Count.Should().Be(1);

        // Wes holds nothing of the kind anywhere.
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _wes, scoped => RequireAsync(scoped, seats)));

        // System work in the tenant holds every permission there.
        await _services.BySystemIn(_harbor.Tenant, scoped => RequireAsync(scoped, seats));
    }

    [Fact]
    public async Task Nobody_is_refused_as_nobody_before_anything_is_read_for_a_permission()
    {
        var seats = new Declaring(TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));

        await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => ByNobody(seats));

        _services.Commands.Commands.Should().BeEmpty("no context is made, and no statement sent, for a caller that is nobody");
    }

    [Fact]
    public async Task A_permission_the_catalogue_does_not_know_is_a_mistake_in_the_request_whoever_calls()
    {
        var unknown = new Declaring(TenancyAccess.AtTenant("widget.polish"));

        await FluentActions.Awaiting(() => _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => RequireAsync(scoped, unknown)))
            .Should().ThrowAsync<ArgumentException>("a request that required a permission nobody can hold would refuse everyone, so it fails as the bug it is");
    }

    [Fact]
    public async Task A_permission_at_a_unit_is_asked_there_and_above_in_one_statement()
    {
        static Declaring At(OrganizationUnitId unit) => new(TenancyAccess.AtUnit(TenancyPermissions.SeatsManage, unit));

        // Seth manages seats at North: there the permission is his, asked in one statement over the context the check was registered for.
        await _services.BySeat(_harbor.Tenant, _seth, scoped => RequireAsync(scoped, At(_north)));
        _services.Commands.Count.Should().Be(1, "the permission is asked in one statement");
        _services.Commands.Sent[0].Writes.Should().BeFalse();
        _services.Commands.Sent[0].Context.Should().BeOfType<TestTenancyContext>();

        // At the root, above where he holds it, it is not: the refusal names the permission and the unit, as the package's own use cases do.
        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _seth, scoped => RequireAsync(scoped, At(_harbor.RootUnit))));
        refusal.Arguments["Permission"].Should().Be(TenancyPermissions.SeatsManage);
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
    public async Task A_permission_at_a_unit_known_by_another_type_than_the_application_s_units_stops_the_request()
    {
        var elsewhere = new Declaring(TenancyAccess.AtUnit(TenancyPermissions.SeatsManage, _seth));

        (await FluentActions.Awaiting(() => _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => RequireAsync(scoped, elsewhere)))
                .Should().ThrowAsync<InvalidOperationException>("a case the check cannot decide lets nobody through, the administrator included"))
            .WithMessage("*Tenancy's access check does not decide: the application's units are known by OrganizationUnitId*");
    }

    [Fact]
    public async Task Only_an_operator_passes_what_is_for_operators()
    {
        var operators = new Declaring(TenancyAccess.Operator());
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

    [Fact]
    public async Task Every_operator_case_refuses_a_caller_who_is_no_operator_before_anything()
    {
        AccessRequirement[] cases = [TenancyAccess.Operator(), TenancyAccess.Operator(_harbor.Tenant), TenancyAccess.Operator(OperatorTenant.FromRequest)];
        foreach (var required in cases)
        {
            // The tenant's administrator, signed in as a user, and system work in the tenant: neither is an operator.
            await using (var scope = _services.Scope())
            using (Callers.Begin(Caller.User(Guid.NewGuid())))
            using (TenancyCallers.Begin(HostCaller.InSeat(_harbor.Tenant, _harbor.AdminSeat)))
            {
                await Refused.WithCodeAsync(TenancyRefusals.OperatorsOnly, () => RequireAsync(scope.ServiceProvider, new Declaring(required)), $"{required} is for operators");
            }

            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(_harbor.Tenant))
            {
                await Refused.WithCodeAsync(TenancyRefusals.OperatorsOnly, () => InScope(new Declaring(required)), "system work is no operator");
            }
        }

        _services.Commands.Commands.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ the work an operator's command runs in

    [Fact]
    public async Task An_operators_command_runs_as_operator_work_in_the_tenant_its_ForTenant_member_names()
    {
        var odette = Guid.NewGuid();
        var suspend = new StatedRequests.ForOperatorsInTenant(_harbor.Tenant);
        ((IHostRequest)suspend).RequiredAccess.Should().Be(TenancyAccess.Operator(_harbor.Tenant), "[ForTenant] marks the tenant the command names");

        await using var scope = _services.Scope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IHostRequest>>();

        // The request names Harbor beside the command as well, as a client that always sends the tenant does.
        using (Callers.Begin(Caller.User(odette, OperatorRole)))
        using (TenancyCallers.Begin(HostCaller.OperatorNaming(_harbor.Tenant)))
        {
            await checks.RequireAsync(suspend, Cancellation);
            using (checks.BeginWork(suspend, isQuery: false))
            {
                // Operator work in Harbor, recorded as Odette: system work, whose power the tenant's use cases accept.
                TenancyCallers.Current<TenantId, SeatId>().Should().Be(HostCaller.SystemIn(_harbor.Tenant, TenancyActor<SeatId>.OfOperator(odette, TenancyWork.SystemScope)));
                Callers.Ambient!.Kind.Should().Be(CallerKind.SystemIn);
                Callers.Ambient.UserId.Should().BeNull("the operator is who the work is recorded as, never who it runs as");

                var suspended = await scope.ServiceProvider.Tenants().SuspendAsync("Asked for by the owner", Cancellation);
                suspended.Status.Should().Be(TenantStatus.Suspended);

                // Asked again inside the work, as the toolkit asks when the database refuses a save, the check still passes.
                (await RequestInHand.Current!.StillPassesAsync(Cancellation)).Should().BeTrue("the operator who was let through is who the work is recorded as");
            }

            TenancyCallers.Current<TenantId, SeatId>().Should().Be(HostCaller.OperatorNaming(_harbor.Tenant), "the work ends with the handler");
            Callers.Ambient!.UserId.Should().Be(odette);
        }

        // The event the suspension raised names the operator, by identity, in the access history.
        var kept = await scope.ServiceProvider.TenancyContext().Set<EventLogEntry>()
            .Where(entry => entry.EventName == "tenancy.tenant-suspended")
            .Select(entry => new { entry.ActedByKind, entry.ActedById })
            .SingleAsync(Cancellation);
        kept.ActedByKind.Should().Be("operator");
        kept.ActedById.Should().Be(odette.ToString("D"));
    }

    [Fact]
    public async Task A_request_that_names_another_tenant_than_its_command_is_refused()
    {
        var orchard = await _services.ProvisionAsync("orchard");
        var suspend = new StatedRequests.ForOperatorsInTenant(_harbor.Tenant);

        await using var scope = _services.Scope();
        using (Callers.Begin(Caller.User(Guid.NewGuid(), OperatorRole)))
        using (TenancyCallers.Begin(HostCaller.OperatorNaming(orchard.Tenant)))
        {
            var refusal = await Refused.WithCodeAsync(TenancyRefusals.TenantMismatch, () => Stated(scope.ServiceProvider, suspend));
            refusal.Kind.Should().Be(RefusalKind.Invalid);
            refusal.Message.Should().Be("This request names two different tenants.");
        }

        // A request that names no tenant beside the command is let through: the command names it.
        using (Callers.Begin(Caller.User(Guid.NewGuid(), OperatorRole)))
        using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.TenantRequired)))
        {
            await Stated(scope.ServiceProvider, suspend);
        }
    }

    [Fact]
    public async Task FromRequest_runs_in_the_tenant_the_request_names_and_refuses_a_request_that_names_none()
    {
        var odette = Guid.NewGuid();
        var close = new StatedRequests.ForOperatorsInTheNamedTenant();
        ((IHostRequest)close).RequiredAccess.Should().Be(TenancyAccess.Operator(OperatorTenant.FromRequest));

        await using var scope = _services.Scope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IHostRequest>>();
        using (Callers.Begin(Caller.User(odette, OperatorRole)))
        {
            using (TenancyCallers.Begin(HostCaller.OperatorNaming(_harbor.Tenant)))
            {
                await checks.RequireAsync(close, Cancellation);
                using (checks.BeginWork(close, isQuery: false))
                {
                    TenancyCallers.Current<TenantId, SeatId>().Should().Be(HostCaller.SystemIn(_harbor.Tenant, TenancyActor<SeatId>.OfOperator(odette, TenancyWork.SystemScope)));
                }
            }

            // No tenant named, and one nobody has: refused as a seat is, before the handler.
            using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.TenantRequired)))
            {
                await Refused.WithCodeAsync(TenancyRefusals.TenantRequired, () => Stated(scope.ServiceProvider, close));
            }

            using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.NotSeated)))
            {
                await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => Stated(scope.ServiceProvider, close));
            }
        }
    }

    [Fact]
    public async Task Without_a_tenant_an_operators_command_runs_in_none_and_the_tenant_the_request_names_is_ignored()
    {
        var odette = Guid.NewGuid();
        var provision = new StatedRequests.ForOperators();

        await using var scope = _services.Scope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IHostRequest>>();
        using (Callers.Begin(Caller.User(odette, OperatorRole)))
        using (TenancyCallers.Begin(HostCaller.OperatorNaming(_harbor.Tenant)))
        {
            await checks.RequireAsync(provision, Cancellation);
            using (checks.BeginWork(provision, isQuery: false))
            {
                var current = TenancyCallers.Current<TenantId, SeatId>();
                current.Kind.Should().Be(TenancyCallerKind.System, "it acts in no tenant, which is what provisioning needs");
                current.Tenant.Should().BeNull("a tenant the request names cannot move the command into it");
                current.Actor.Should().Be(TenancyActor<SeatId>.OfOperator(odette, TenancyWork.SystemScope));
            }
        }
    }

    [Fact]
    public async Task A_query_for_operators_begins_nothing()
    {
        var reading = new StatedRequests.ForOperatorsInTenant(_harbor.Tenant);

        await using var scope = _services.Scope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IHostRequest>>();
        using (Callers.Begin(Caller.User(Guid.NewGuid(), OperatorRole)))
        using (TenancyCallers.Begin(HostCaller.OperatorNaming(_harbor.Tenant)))
        {
            await checks.RequireAsync(reading, Cancellation);
            checks.BeginWork(reading, isQuery: true).Should().BeNull("an operator reads across tenants through its own database role");
            TenancyCallers.Current<TenantId, SeatId>().Should().Be(HostCaller.OperatorNaming(_harbor.Tenant));
        }
    }

    [Fact]
    public async Task A_caller_another_case_let_through_begins_no_operator_work()
    {
        // The tenant's access history: an operator, or a seat that manages seats for the whole tenant. Ada is the second.
        var auditLog = new Declaring(AccessRequirement.Any(TenancyAccess.Operator(), TenancyAccess.AtTenant(TenancyPermissions.SeatsManage)));

        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, async scoped =>
        {
            var checks = scoped.GetRequiredService<AccessChecks<IHostRequest>>();
            await checks.RequireAsync(auditLog, Cancellation);
            checks.BeginWork(auditLog, isQuery: false).Should().BeNull("a seat is no operator, whichever case let it through");
            TenancyCallers.Current<TenantId, SeatId>().Should().Be(HostCaller.InSeat(_harbor.Tenant, _harbor.AdminSeat));
        });
    }

    [Fact]
    public async Task Work_is_begun_only_for_a_request_its_checks_let_through()
    {
        await using var scope = _services.Scope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IHostRequest>>();

        using (Callers.Begin(Caller.User(Guid.NewGuid(), OperatorRole)))
        {
            FluentActions.Invoking(() => checks.BeginWork(new StatedRequests.ForOperators(), isQuery: false))
                .Should().Throw<InvalidOperationException>("a request its checks were not asked about is in nobody's hand")
                .WithMessage("*is not in hand in this flow of work*");
        }
    }

    // ------------------------------------------------------------------ the cases, stated with attributes

    [Fact]
    public void A_request_that_states_its_case_with_an_attribute_answers_the_case_it_stands_for()
    {
        // The generator wrote one line per request into the module's interface: each answers the very case the
        // property would, wherever the property is read.
        AccessRequirement Of(IHostRequest request) => request.RequiredAccess;

        Of(new StatedRequests.ForAnyone()).Should().Be(AccessRequirement.AllowAnonymous());
        Of(new StatedRequests.ForTheSignedIn()).Should().Be(AccessRequirement.SignedIn());
        Of(new StatedRequests.InTheTenant()).Should().Be(TenancyAccess.InTenant());
        Of(new StatedRequests.ManageEverySeat()).Should().Be(TenancyAccess.AtTenant(TenancyPermissions.SeatsManage), "a permission without a marker is held for the whole tenant");
        Of(new StatedRequests.ManageSeatsAt(_north)).Should().Be(TenancyAccess.AtUnit(TenancyPermissions.SeatsManage, _north), "[AtUnit] marks the unit");
        Of(new StatedRequests.ForOperators()).Should().Be(TenancyAccess.Operator());
        Of(new StatedRequests.ForOperatorsInTenant(_harbor.Tenant)).Should().Be(TenancyAccess.Operator(_harbor.Tenant), "[ForTenant] marks the tenant");
        Of(new StatedRequests.ForOperatorsInTheNamedTenant()).Should().Be(TenancyAccess.Operator(OperatorTenant.FromRequest));
    }

    [Fact]
    public async Task A_case_stated_with_an_attribute_lets_the_same_callers_through_as_the_property()
    {
        // Ada administers the tenant; Seth manages seats at North; Wes holds nothing of the kind anywhere.
        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => Stated(scoped, new StatedRequests.ManageEverySeat()));
        (await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _seth, scoped => Stated(scoped, new StatedRequests.ManageEverySeat()))))
            .Arguments["Permission"].Should().Be(TenancyPermissions.SeatsManage);

        await _services.BySeat(_harbor.Tenant, _seth, scoped => Stated(scoped, new StatedRequests.ManageSeatsAt(_north)));
        (await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _seth, scoped => Stated(scoped, new StatedRequests.ManageSeatsAt(_harbor.RootUnit)))))
            .Arguments["Unit"].Should().Be(_harbor.RootUnit, "the unit is the one the request names, read from the member [AtUnit] marks");
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _wes, scoped => Stated(scoped, new StatedRequests.ManageSeatsAt(_north))));

        await _services.BySeat(_harbor.Tenant, _wes, scoped => Stated(scoped, new StatedRequests.InTheTenant()));
        await _services.BySystemIn(_harbor.Tenant, scoped => Stated(scoped, new StatedRequests.ManageSeatsAt(_north)));

        await using (var scope = _services.Scope())
        using (Callers.Begin(Caller.Anonymous))
        using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.NotSeated)))
        {
            await Stated(scope.ServiceProvider, new StatedRequests.ForAnyone());
            await RefusedByTheToolkitAsync(ToolkitRefusals.NotSignedIn, () => Stated(scope.ServiceProvider, new StatedRequests.ForTheSignedIn()));
            await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => Stated(scope.ServiceProvider, new StatedRequests.InTheTenant()));
            await Refused.WithCodeAsync(TenancyRefusals.OperatorsOnly, () => Stated(scope.ServiceProvider, new StatedRequests.ForOperators()));
        }
    }

    // ------------------------------------------------------------------ the caller's own, and cases that combine

    [Fact]
    public async Task A_seat_is_its_own_with_nothing_read_and_the_permission_stands_in_for_another_seat()
    {
        var renameWes = new StatedRequests.RenameSeat(_wes);
        ((IHostRequest)renameWes).RequiredAccess.Should().Be(
            AccessRequirement.Any(TenancyAccess.Own(_wes), TenancyAccess.AtTenant(TenancyPermissions.SeatsManage)), "[OrOwn] beside the permission is the caller's own, or the permission");

        // Wes renames himself: his own seat in the tenant, compared at the door, and the permission after it is not asked.
        _services.Commands.Reset();
        await _services.BySeat(_harbor.Tenant, _wes, scoped => Stated(scoped, renameWes));
        _services.Commands.Commands.Should().BeEmpty("the caller's own reads nothing, and the permission is asked only where it is not the caller's");

        // Ada renames Wes with the permission: his seat is not hers, so the permission is asked, in one statement.
        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => Stated(scoped, renameWes));
        _services.Commands.Count.Should().Be(1);

        // Seth manages seats at North, not for the whole tenant, and Wes's seat is not his: one refusal, both reasons.
        var refusal = await RefusedOnceAsync(() => _services.BySeat(_harbor.Tenant, _seth, scoped => Stated(scoped, renameWes)));
        Reasons(refusal).Select(reason => reason.Code).Should().Equal(ToolkitRefusals.NotOwn, TenancyRefusals.NotPermitted);
        Reasons(refusal)[1].Arguments["Permission"].Should().Be(TenancyPermissions.SeatsManage, "each reason carries what its case's refusal carries");

        // System work in the tenant has no seat of its own, and holds every permission there.
        await _services.BySystemIn(_harbor.Tenant, scoped => Stated(scoped, renameWes));

        // Nobody has no seat, and is refused by the permission as nobody, with the reason it was given.
        using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.NotSeated)))
        {
            await using var scope = _services.Scope();
            Reasons(await RefusedOnceAsync(() => Stated(scope.ServiceProvider, renameWes))).Select(reason => reason.Code)
                .Should().Equal(ToolkitRefusals.NotOwn, TenancyRefusals.NotSeated);
        }
    }

    [Fact]
    public async Task What_is_about_the_seat_alone_lets_the_seat_through_and_no_permission_stands_in()
    {
        await _services.BySeat(_harbor.Tenant, _wes, scoped => Stated(scoped, new StatedRequests.LeaveOwnPlace(_wes)));

        await RefusedByTheToolkitAsync(
            ToolkitRefusals.NotOwn,
            () => _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => Stated(scoped, new StatedRequests.LeaveOwnPlace(_wes))),
            "no permission stands in for the seat itself, its tenant's administrator's included");
        await RefusedByTheToolkitAsync(
            ToolkitRefusals.NotOwn,
            () => _services.BySystemIn(_harbor.Tenant, scoped => Stated(scoped, new StatedRequests.LeaveOwnPlace(_wes))),
            "system work in the tenant has no seat of its own, and acts for one at most as who is recorded");
    }

    [Fact]
    public async Task Stacked_attributes_all_hold_and_the_first_that_fails_refuses_with_its_own_reason()
    {
        var asAPerson = new StatedRequests.ManageEverySeatAsAPerson();
        ((IHostRequest)asAPerson).RequiredAccess.Should().Be(AccessRequirement.All(AccessRequirement.SignedIn(), TenancyAccess.AtTenant(TenancyPermissions.SeatsManage)));

        // Ada signed in, and administers Harbor.
        using (Callers.Begin(Caller.User(Guid.NewGuid())))
        {
            await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => Stated(scoped, asAPerson));
            (await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _seth, scoped => Stated(scoped, asAPerson))))
                .Arguments["Permission"].Should().Be(TenancyPermissions.SeatsManage, "the second case fails, with its own reason");
        }

        // System work in the tenant holds the permission, and is nobody's sign-in: the first case refuses it, and the permission is never asked.
        _services.Commands.Reset();
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(_harbor.Tenant))
        {
            await RefusedByTheToolkitAsync(ToolkitRefusals.NotSignedIn, () => StatedInScope(asAPerson));
        }

        _services.Commands.Commands.Should().BeEmpty("what follows the first refusal is not asked");
    }

    [Fact]
    public async Task One_caller_or_another_is_let_through_by_the_first_case_it_meets_and_refused_once_with_each_reason()
    {
        // The tenant's access history: one of the application's operators, or a seat that manages seats for the whole tenant.
        var auditLog = new Declaring(AccessRequirement.Any(TenancyAccess.Operator(), TenancyAccess.AtTenant(TenancyPermissions.SeatsManage)));

        // An operator is let through by the first case, with nothing read.
        _services.Commands.Reset();
        await using (var scope = _services.Scope())
        using (Callers.Begin(Caller.User(Guid.NewGuid(), OperatorRole)))
        using (TenancyCallers.Begin(HostCaller.Nobody(TenancyRefusals.NotSeated)))
        {
            await RequireAsync(scope.ServiceProvider, auditLog);
        }

        _services.Commands.Commands.Should().BeEmpty("the operator passed the first case, and the permission was never asked");

        // Ada is no operator, and holds the permission: let through by the second, in one statement.
        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => RequireAsync(scoped, auditLog));
        _services.Commands.Count.Should().Be(1);

        // Seth is neither: one access.refused, with the operators' reason and the permission's, in order.
        var refusal = await RefusedOnceAsync(() => _services.BySeat(_harbor.Tenant, _seth, scoped => RequireAsync(scoped, auditLog)));
        refusal.Kind.Should().Be(RefusalKind.NotPermitted);
        Reasons(refusal).Select(reason => reason.Code).Should().Equal(TenancyRefusals.OperatorsOnly, TenancyRefusals.NotPermitted);
    }

    [Fact]
    public async Task Two_permissions_in_two_places_are_both_asked_and_the_first_that_is_not_held_refuses()
    {
        // A seat moves from one unit to another: the permission to manage seats at both, the one it leaves first.
        static Declaring MoveSeat(OrganizationUnitId from, OrganizationUnitId to)
            => new(AccessRequirement.All(TenancyAccess.AtUnit(TenancyPermissions.SeatsManage, from), TenancyAccess.AtUnit(TenancyPermissions.SeatsManage, to)));

        // Seth manages seats at North: within North he moves a seat, and not to the root.
        var northPier = await _services.AddUnitAsync(_harbor.Tenant, _north, "North Pier");
        _services.Commands.Reset();
        await _services.BySeat(_harbor.Tenant, _seth, scoped => RequireAsync(scoped, MoveSeat(_north, northPier)));
        _services.Commands.Count.Should().Be(2, "each permission at its unit, in a statement of its own");

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _seth, scoped => RequireAsync(scoped, MoveSeat(_north, _harbor.RootUnit))));
        refusal.Arguments["Unit"].Should().Be(_harbor.RootUnit, "the case that failed refuses, naming its own unit");

        // Ada holds it at the root, and so everywhere below it.
        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => RequireAsync(scoped, MoveSeat(_north, _harbor.RootUnit)));
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
            TenancyAccess.AtTenant(TenancyPermissions.SeatsManage),
            TenancyAccess.Operator(),
            TenancyAccess.Operator(_harbor.Tenant),
            TenancyAccess.AtUnit(TenancyPermissions.SeatsManage, _north),
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
        var seats = new OfWidgetsDeclaring(TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));

        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, scoped => scoped.GetRequiredService<AccessChecks<IWidgetRequest>>().RequireAsync(seats, Cancellation).AsTask());

        _services.Commands.Count.Should().Be(1);
        _services.Commands.Sent[0].Context.Should().BeOfType<TestWidgetContext>("the widgets' module asks over the read model its own context maps, and names no context of Tenancy's");

        await Refused.WithCodeAsync(
            TenancyRefusals.NotPermitted,
            () => _services.BySeat(_harbor.Tenant, _seth, scoped => scoped.GetRequiredService<AccessChecks<IWidgetRequest>>().RequireAsync(seats, Cancellation).AsTask()));
    }

    [Fact]
    public async Task Without_a_factory_the_permission_is_asked_on_the_scope_s_own_context()
    {
        var seats = new Declaring(TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));

        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, async scoped =>
        {
            await RequireAsync(scoped, seats);
            _services.Commands.Sent.Should().ContainSingle().Which.Context.Should().BeSameAs(scoped.TenancyContext());
        });
    }

    [Fact]
    public async Task With_a_factory_the_permission_is_asked_on_a_context_of_its_own()
    {
        using var pooled = await ServicesAsync(pooled: true);
        var harbor = await pooled.ProvisionAsync("harbor");
        var seats = new Declaring(TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));

        await pooled.BySeat(harbor.Tenant, harbor.AdminSeat, async scoped =>
        {
            // The scope's own context, the unit of work of its commands, rented before the check runs.
            var ofTheScope = scoped.TenancyContext();
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
        services.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, TenancyAccessManager, IWidgetRequest, TestWidgetContext>();
        var registered = services.Count;

        services.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, TenancyAccessManager, IWidgetRequest, TestWidgetContext>();

        services.Should().HaveCount(registered);
        services.Should().OnlyContain(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped, "the check, the set and what a check keeps live as long as a request");
        FluentActions.Invoking(() => TenancyEntityFrameworkServiceCollectionExtensions.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, TenancyAccessManager, IWidgetRequest, TestWidgetContext>(null!))
            .Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------ helpers

    private static Task RequireAsync(IServiceProvider scoped, Declaring request)
        => scoped.GetRequiredService<AccessChecks<IHostRequest>>().RequireAsync(request, Cancellation).AsTask();

    /// <summary>Holds a request that states its requirement with an attribute to it, through the module's set of checks.</summary>
    private static Task Stated(IServiceProvider scoped, IHostRequest request)
        => scoped.GetRequiredService<AccessChecks<IHostRequest>>().RequireAsync(request, Cancellation).AsTask();

    /// <summary>Asserts that <paramref name="act"/> is refused with one of the toolkit's own codes, which Tenancy's table does not hold.</summary>
    private static async Task RefusedByTheToolkitAsync(string code, Func<Task> act, string because = "")
        => (await FluentActions.Awaiting(act).Should().ThrowAsync<RefusalException>(because)).Which.Code.Should().Be(code, because);

    /// <summary>Asserts that <paramref name="act"/> is refused once for every way it may be sent, and hands the refusal back.</summary>
    private static async Task<RefusalException> RefusedOnceAsync(Func<Task> act)
    {
        var refusal = (await FluentActions.Awaiting(act).Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(ToolkitRefusals.Refused, refusal.Message);
        return refusal;
    }

    /// <summary>The reason of each case of a refusal that none of them let through, in the order of the cases.</summary>
    private static IReadOnlyList<RefusalReason> Reasons(RefusalException refusal)
        => (IReadOnlyList<RefusalReason>)refusal.Arguments[ToolkitRefusals.ReasonsArgument]!;

    /// <summary>Holds a request that states its requirement with attributes to it in a scope of its own, as whoever the caller begun around it is.</summary>
    private async Task StatedInScope(IHostRequest request)
    {
        await using var scope = _services.Scope();
        await Stated(scope.ServiceProvider, request);
    }

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
