using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// System work and the current tenant are reached through the access manager, closed over the application's ids, so no
/// project that sees the classes names an id to begin either: <c>access.BeginSystem()</c> on the
/// <c>TenancyAccessManager</c> the generator writes into the test host. An operator's work is begun by nothing a project
/// names: a command marked <c>[Operator]</c> runs as it. Where a seat is given, C# infers both
/// ids of <see cref="TenancyWork"/>'s own method, for code that sees the ids alone. Each begins exactly what the method
/// generic over the ids begins, and ends it.
/// </summary>
public class ClosedOverTheIdsTests
{
    private static readonly TenantId Harbor = new(1);
    private static readonly SeatId Ada = SeatId.CreateSequential();

    /// <summary>The access manager as the container makes it: the class the generator wrote into the test host.</summary>
    private static readonly TenancyAccessManager Access = new(TenancyCatalogue.Build(HostCatalogue.Application, []), TimeProvider.System, NoServices.Instance);

    /// <summary>What the toolkit's current user and Tenancy's are while the work <paramref name="begin"/> begins runs.</summary>
    private static (CurrentUserKind? Kind, string? Scope, ITenancyUser? Tenancy) Within(Func<IDisposable> begin)
    {
        using (begin())
        {
            return (BegunUser?.Kind, BegunUser?.SystemScope, TenancyUsers.Ambient);
        }
    }

    [Fact]
    public void Each_closed_form_begins_what_its_counterpart_generic_over_the_ids_begins()
    {
        (string Name, Func<IDisposable> Closed, Func<IDisposable> Open)[] pairs =
        [
            ("BeginSystem", () => Access.BeginSystem(), () => TenancyWork.BeginSystem<TenantId, SeatId>()),
            ("BeginSystem in a module's scope", () => Access.BeginSystem("billing"), () => TenancyWork.BeginSystem<TenantId, SeatId>("billing")),
            ("BeginSystemIn", () => Access.BeginSystemIn(Harbor), () => TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor)),
            ("BeginSystemIn for a seat", () => Access.BeginSystemIn(Harbor, Ada, "projects"), () => TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor, (SeatId?)Ada, "projects")),
            ("BeginTokenIn", () => Access.BeginTokenIn(Harbor, Ada, "feeds"), () => TenancyWork.BeginTokenIn<TenantId, SeatId>(Harbor, Ada, "feeds")),
        ];

        foreach (var (name, closed, open) in pairs)
        {
            var begun = Within(closed);

            begun.Should().Be(Within(open), "{0} is the same work, closed over the host's ids", name);
            begun.Tenancy.Should().BeOfType<HostUser>("{0} is closed over the ids the use cases are", name);
            BegunUser.Should().BeNull("{0} ends what it began", name);
            TenancyUsers.Ambient.Should().BeNull();
        }
    }

    [Fact]
    public void It_is_the_cores_access_manager_and_the_system_work_it_begins_always_has_a_scope()
    {
        AccessManager core = Access;

        using (Access.BeginSystem())
        {
            BegunUser!.Kind.Should().Be(CurrentUserKind.SystemIn, "system work a Tenancy application begins stays under the policies");
            BegunUser.SystemScope.Should().Be(TenancyWork.SystemScope, "Tenancy's own scope where none is given");
            TenancyUsers.Current<TenantId, SeatId>().Should().Be(HostUser.System, "it acts in no tenant");
        }

        using (core.BeginSystem("billing"))
        {
            BegunUser!.SystemScope.Should().Be("billing");
            var tenancy = TenancyUsers.Current<TenantId, SeatId>();
            tenancy.Kind.Should().Be(TenancyUserKind.System, "a module's system work acts in no tenant either, begun through the core's type too");
            tenancy.Actor.Should().Be(TenancyActor<SeatId>.OfSystem("billing"), "and is recorded as the system in its scope");
        }

        // BeginAs, BeginSystemBypassingRowAccess and BeginNone are the core's: they leave Tenancy's user as it is.
        using (TenancyUsers.Begin(HostUser.InSeat(Harbor, Ada)))
        {
            using (Access.BeginAs(Guid.NewGuid()))
            {
                Access.CurrentTenant.Seat.Should().Be(Ada);
            }

            using (Access.BeginSystemBypassingRowAccess())
            {
                BegunUser!.Kind.Should().Be(CurrentUserKind.System, "its name says that no row access rule holds it");
                Access.CurrentTenant.Seat.Should().Be(Ada);
            }

            using (Access.BeginNone())
            {
                Access.CurrentTenant.Seat.Should().Be(Ada);
            }
        }

        FluentActions.Invoking(() => Access.BeginSystem("Billing!")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => core.BeginSystem(null!)).Should().Throw<ArgumentNullException>();
        BegunUser.Should().BeNull("a scope that is no scope begins nothing");
        TenancyUsers.Ambient.Should().BeNull();
    }

    [Fact]
    public void The_closed_forms_check_what_their_counterparts_check()
    {
        // A scope that is not one: nothing is begun.
        FluentActions.Invoking(() => Access.BeginSystemIn(Harbor, scope: "Projects!")).Should().Throw<ArgumentException>();

        BegunUser.Should().BeNull();
        TenancyUsers.Ambient.Should().BeNull();
    }

    [Fact]
    public void BeginSystemIn_infers_both_ids_where_a_seat_is_given()
    {
        // As code that sees only the ids writes it: no type argument, the tenant and the seat say which.
        using (TenancyWork.BeginSystemIn(Harbor, Ada, "projects"))
        {
            TenancyUsers.Current<TenantId, SeatId>().Should().Be(HostUser.SystemIn(Harbor, TenancyActor<SeatId>.OfSystem("projects", Ada)));
            BegunUser!.SystemScope.Should().Be("projects");
        }

        using (TenancyWork.BeginSystemIn(Harbor, Ada))
        {
            TenancyUsers.Current<TenantId, SeatId>().Should().Be(HostUser.SystemIn(Harbor, Ada), "Tenancy's own scope, as without the seat");
        }

        TenancyUsers.Ambient.Should().BeNull();
    }

    [Fact]
    public void A_bare_default_for_the_seat_is_no_seat_where_the_ids_are_written_as_in_the_closed_form()
    {
        // Both overloads apply where the ids are written, and the one for a seat given yields to the one for a seat
        // that may be missing: default stays no seat, never an empty id recorded as the seat the work is done for.
        var written = Within(() => TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor, default));

        written.Should().Be(Within(() => Access.BeginSystemIn(Harbor, default)));
        written.Should().Be(Within(() => Access.BeginSystemIn(Harbor)));
        written.Tenancy.Should().Be(HostUser.SystemIn(Harbor));

        var caller = written.Tenancy.Should().BeOfType<HostUser>().Subject;
        caller.Seat.Should().BeNull("the work acts for no seat");
        caller.Actor!.Value.Seat.Should().BeNull("and is recorded for none");
    }

    [Fact]
    public void The_current_tenant_is_the_current_caller_closed_over_the_ids()
    {
        Access.CurrentTenant.IsAvailable.Should().BeFalse("outside any scope there is nobody");
        Access.CurrentTenant.Id.Should().BeNull();
        Access.CurrentTenant.Seat.Should().BeNull();
        Refused.With(TenancyRefusals.NotSeated, () => Access.CurrentTenant.Require());

        using (TenancyUsers.Begin(HostUser.InSeat(Harbor, Ada)))
        {
            Access.CurrentTenant.IsAvailable.Should().BeTrue();
            Access.CurrentTenant.Id.Should().Be(Harbor);
            Access.CurrentTenant.Seat.Should().Be(Ada);
            Access.CurrentTenant.Require().Should().Be(new TenantInScope<TenantId, SeatId>(Harbor, Ada, BySystem: false));
        }

        using (Access.BeginSystemIn(Harbor, Ada))
        {
            Access.CurrentTenant.Id.Should().Be(Harbor);
            Access.CurrentTenant.Seat.Should().Be(Ada, "the seat system work acts for, which is only recorded");
            Access.CurrentTenant.Require().BySystem.Should().BeTrue();
        }

        using (Access.BeginSystem())
        {
            Access.CurrentTenant.IsAvailable.Should().BeFalse("system work outside any tenant acts in none");
            Access.CurrentTenant.Id.Should().BeNull();
        }
    }

    [Fact]
    public void The_current_user_follows_what_is_begun()
    {
        var user = Guid.NewGuid();
        using (TestAccess.BeginAs(user))
        {
            Access.CurrentUser.Id.Should().Be(user);
            Access.CurrentUser.IsAuthenticated.Should().BeTrue();
            Access.CurrentUser.IsSystem.Should().BeFalse();
            Access.CurrentUser.Role.Should().Be("authenticated");

            using (Access.BeginSystemIn(Harbor))
            {
                Access.CurrentUser.IsSystem.Should().BeTrue("system work is no user's");
                Access.CurrentUser.Id.Should().BeNull();
                Access.CurrentUser.IsAuthenticated.Should().BeFalse();
                Access.CurrentUser.Role.Should().BeNull();
            }

            Access.CurrentUser.Id.Should().Be(user, "after the using, the user acts again");
        }

        using (TestAccess.BeginAs(CurrentUserSnapshot.Anonymous))
        {
            Access.CurrentUser.Id.Should().BeNull();
            Access.CurrentUser.IsAuthenticated.Should().BeFalse();
            Access.CurrentUser.IsSystem.Should().BeFalse();
            Access.CurrentUser.Role.Should().Be("anon");
        }
    }

    [Fact]
    public async Task The_tenants_to_visit_are_the_storage_packages_to_read()
    {
        // No storage package registered the read: the access manager says so, rather than answer nothing.
        await FluentActions.Awaiting(() => Access.TenantsToSweepAsync("projects", CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddTenancy<TContext>()*");
    }
}
