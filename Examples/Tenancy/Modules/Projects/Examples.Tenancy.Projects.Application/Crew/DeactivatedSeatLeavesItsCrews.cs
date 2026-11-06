using DDDToolkit.Mediator;
using DDDToolkit.Supporting.Tenancy;

namespace Examples.Tenancy.Projects.Application.Crew;

/// <summary>
/// Takes a deactivated seat off every crew it is on. Tenancy raises <see cref="SeatDeactivated{TTenantId, TSeatId}"/>
/// when a seat is deactivated for good, and a deactivated seat sees no project. Its place on a crew would stay in every
/// crew list and hold nothing.
/// </summary>
/// <remarks>
/// The event is the Tenancy package's, closed over the application's ids. Every domain event is a notification of the
/// toolkit's mediator, so this module handles it like an event of its own. The toolkit's generator registers this class
/// in <c>AddProjectsHandlers()</c>, which <see cref="ProjectsApplicationServices.AddProjectsApplication"/> calls.
/// <para>
/// The Tenants module's outbox delivers the event after the deactivation committed. A failure here never undoes the
/// deactivation: the outbox delivers the event again, and a seat already off a crew is not found a second time.
/// </para>
/// <para>
/// The seat stays on the crew of a project it owns, until somebody else is named owner. It stays on the crew of a closed
/// project too, which changes no more.
/// </para>
/// </remarks>
/// <param name="crew">The projects' crew manager, which takes the seat off a crew and saves.</param>
/// <param name="reads">Where the crews the seat is on are read.</param>
/// <param name="tenancy">Begins the module's own work in the event's tenant.</param>
internal sealed class DeactivatedSeatLeavesItsCrews(CrewMemberManager crew, IProjectReads reads, TenancyAccessManager tenancy)
    : INotificationHandler<SeatDeactivated<TenantId, SeatId>>
{
    /// <inheritdoc />
    public async ValueTask Handle(SeatDeactivated<TenantId, SeatId> notification, CancellationToken cancellationToken)
    {
        // The outbox hands the event over with nobody begun, and the host requires every flow of work to begin its
        // caller. The work is this module's own, in the event's tenant, in the scope the projects' rules let through,
        // as the seeding's is. One handler begins it itself. A module with several handlers of Tenancy's events would
        // begin it once, in an INotificationBehavior registered for them.
        using (tenancy.BeginSystemIn(notification.TenantId, scope: ProjectMembership.Scope))
        {
            IReadOnlyList<ProjectId> crews;
            await using (var reading = reads.Open())
            {
                crews = await reading.CrewsToLeaveAsync(notification.SeatId, cancellationToken);
            }

            foreach (var project in crews)
            {
                await crew.RemoveAsync(project, notification.SeatId, cancellationToken);
            }
        }
    }
}
