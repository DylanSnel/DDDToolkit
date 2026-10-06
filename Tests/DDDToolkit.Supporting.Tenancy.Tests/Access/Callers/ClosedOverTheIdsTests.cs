using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// System work and the current caller are reached through the class the use cases are named through, closed over the
/// application's ids, so no project that sees the classes names an id to begin either: <c>HostTenancy.BeginSystem()</c>
/// here, <c>TenantsTenancy.BeginSystem()</c> in the sample. Where a seat is given, C# infers both ids of
/// <see cref="TenancyWork"/>'s own method, for a module that sees the ids alone. Each begins exactly what the method
/// generic over the ids begins, and ends it.
/// </summary>
public class ClosedOverTheIdsTests
{
    private static readonly TenantId Harbor = new(1);
    private static readonly SeatId Ada = SeatId.CreateSequential();
    private static readonly Guid Odette = Guid.NewGuid();

    /// <summary>What the toolkit's caller and Tenancy's are while the work <paramref name="begin"/> begins runs.</summary>
    private static (CallerKind? Kind, string? Scope, ITenancyCaller? Tenancy) Within(Func<IDisposable> begin)
    {
        using (begin())
        {
            return (Callers.Ambient?.Kind, Callers.Ambient?.Scope, TenancyCallers.Ambient);
        }
    }

    [Fact]
    public void Each_closed_form_begins_what_its_counterpart_generic_over_the_ids_begins()
    {
        (string Name, Func<IDisposable> Closed, Func<IDisposable> Open)[] pairs =
        [
            ("BeginSystem", HostTenancy.BeginSystem, TenancyWork.BeginSystem<TenantId, SeatId>),
            ("BeginSystemIn", () => HostTenancy.BeginSystemIn(Harbor), () => TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor)),
            ("BeginSystemIn for a seat", () => HostTenancy.BeginSystemIn(Harbor, Ada, "projects"), () => TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor, (SeatId?)Ada, "projects")),
            ("BeginOperator", () => HostTenancy.BeginOperator(Odette), () => TenancyWork.BeginOperator<TenantId, SeatId>(Odette)),
            ("BeginOperatorIn", () => HostTenancy.BeginOperatorIn(Harbor, Odette, "projects"), () => TenancyWork.BeginOperatorIn<TenantId, SeatId>(Harbor, Odette, "projects")),
            ("BeginTokenIn", () => HostTenancy.BeginTokenIn(Harbor, Ada, "feeds"), () => TenancyWork.BeginTokenIn<TenantId, SeatId>(Harbor, Ada, "feeds")),
        ];

        foreach (var (name, closed, open) in pairs)
        {
            var begun = Within(closed);

            begun.Should().Be(Within(open), "{0} is the same work, closed over the host's ids", name);
            begun.Tenancy.Should().BeOfType<HostCaller>("{0} is closed over the ids the use cases are", name);
            Callers.Ambient.Should().BeNull("{0} ends what it began", name);
            TenancyCallers.Ambient.Should().BeNull();
        }
    }

    [Fact]
    public void The_closed_forms_check_what_their_counterparts_check()
    {
        // A scope that is not one, and an operator without an identity: nothing is begun.
        FluentActions.Invoking(() => HostTenancy.BeginSystemIn(Harbor, scope: "Projects!")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => HostTenancy.BeginOperator(Guid.Empty)).Should().Throw<ArgumentException>();

        Callers.Ambient.Should().BeNull();
        TenancyCallers.Ambient.Should().BeNull();
    }

    [Fact]
    public void BeginSystemIn_infers_both_ids_where_a_seat_is_given()
    {
        // As a module that sees only the ids writes it: no type argument, the tenant and the seat say which.
        using (TenancyWork.BeginSystemIn(Harbor, Ada, "projects"))
        {
            TenancyCallers.Current<TenantId, SeatId>().Should().Be(HostCaller.SystemIn(Harbor, TenancyActor<SeatId>.OfSystem("projects", Ada)));
            Callers.Ambient!.Scope.Should().Be("projects");
        }

        using (TenancyWork.BeginSystemIn(Harbor, Ada))
        {
            TenancyCallers.Current<TenantId, SeatId>().Should().Be(HostCaller.SystemIn(Harbor, Ada), "Tenancy's own scope, as without the seat");
        }

        TenancyCallers.Ambient.Should().BeNull();
    }

    [Fact]
    public void A_bare_default_for_the_seat_is_no_seat_where_the_ids_are_written_as_in_the_closed_form()
    {
        // Both overloads apply where the ids are written, and the one for a seat given yields to the one for a seat
        // that may be missing: default stays no seat, never an empty id recorded as the seat the work is done for.
        var written = Within(() => TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor, default));

        written.Should().Be(Within(() => HostTenancy.BeginSystemIn(Harbor, default)));
        written.Should().Be(Within(() => HostTenancy.BeginSystemIn(Harbor)));
        written.Tenancy.Should().Be(HostCaller.SystemIn(Harbor));

        var caller = written.Tenancy.Should().BeOfType<HostCaller>().Subject;
        caller.Seat.Should().BeNull("the work acts for no seat");
        caller.Actor!.Value.Seat.Should().BeNull("and is recorded for none");
    }

    [Fact]
    public void CurrentCaller_is_the_current_caller_closed_over_the_ids()
    {
        HostTenancy.CurrentCaller().Should().Be(HostCaller.Nobody(TenancyRefusals.NotSeated), "outside any scope there is nobody");

        using (TenancyCallers.Begin(HostCaller.InSeat(Harbor, Ada)))
        {
            HostTenancy.CurrentCaller().Should().Be(HostCaller.InSeat(Harbor, Ada));
        }

        using (HostTenancy.BeginSystemIn(Harbor))
        {
            HostTenancy.CurrentCaller().Should().Be(TenancyCallers.Current<TenantId, SeatId>());
        }
    }
}
