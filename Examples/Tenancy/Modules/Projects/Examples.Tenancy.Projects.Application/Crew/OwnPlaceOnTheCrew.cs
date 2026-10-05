using DDDToolkit.Supporting.Tenancy.Access;

namespace Examples.Tenancy.Projects.Application.Crew;

/// <summary>
/// The save in which a seat gives up its own place on a crew: it takes a role of its own, or takes itself off the
/// crew. That one save is written as the application's own work in the tenant, for that seat.
/// </summary>
/// <remarks>
/// Whoever manages a crew may do both, and the command's access check has decided that against the project as it
/// was. But here the right came from the very rows the save removes. A database that checks rows judges each
/// statement by the rows as they are when it runs, and a project's own row is written after its crew's: by then
/// the seat holds nothing on the project, and the database would refuse the rest of a save the seat had every
/// right to begin.
/// <para>
/// So the application answers for this save itself, having checked it. Only the save runs that way, in this
/// module's own scope, which writes none of Tenancy's tables; a change to anybody else's place is saved as the
/// caller, under the row rules like every other change. The row then says the application changed it for the
/// seat, which is what happened.
/// </para>
/// <para>
/// Having checked it is something the save knows, not something it assumes: the command must be the request in
/// hand (<see cref="RequestInHand"/>), the one whose access check let it through in this very handling. A handler
/// reached around its check, called directly or by a transport that runs it around the pipeline, saves as the
/// caller, and the database judges that save as it judges every other: a seat that manages no crew is refused
/// there, its own place or not.
/// </para>
/// <para>
/// What was checked is one change to one project, so that is all such a save may write: the two commands save
/// through <see cref="IProjectStore.SaveOnlyAsync"/>, which refuses when the unit of work holds a change to
/// anything else. A scope runs one command, so nothing else is there; a host that sent two commands in one scope
/// would otherwise have the first one's leftovers written here as the application's own work, which no row rule
/// judges.
/// </para>
/// </remarks>
internal static class OwnPlaceOnTheCrew
{
    /// <summary>
    /// The scope of the system work: this module's, so the work writes this module's tables and no other's. It is
    /// the scope the projects' rules name as the application's own work, without which the application's own
    /// questions would answer such work nothing on any project. The database asks it nothing: the projects'
    /// functions answer signed-in users alone, and the policies hold system work to its tenant.
    /// </summary>
    private const string Scope = ProjectMembership.Scope;

    /// <summary>
    /// Begins the save of a change to the place of <paramref name="seat"/> on a crew: as the application's own
    /// work for that seat when it is the caller's own seat and <paramref name="command"/> passed its access check
    /// in this handling, and as the caller, with nothing begun, otherwise. Whoever begins it saves the one project
    /// it changed, with <see cref="IProjectStore.SaveOnlyAsync"/>.
    /// </summary>
    /// <param name="command">The command being handled, the very one its handler was handed.</param>
    /// <param name="seat">The seat whose role is taken, or that is taken off the crew.</param>
    /// <param name="caller">The tenant the caller acts in, and its seat.</param>
    /// <returns>What ends the work when disposed; <see langword="null"/> when the save runs as the caller.</returns>
    public static IDisposable? BeginSave(IProjectsRequest command, SeatId seat, TenantInScope<TenantId, SeatId> caller)
        => caller.Seat == seat && !caller.BySystem && ReferenceEquals(RequestInHand.Current, command)
            ? TenancyWork.BeginSystemIn<TenantId, SeatId>(caller.Tenant, seat, Scope)
            : null;
}
