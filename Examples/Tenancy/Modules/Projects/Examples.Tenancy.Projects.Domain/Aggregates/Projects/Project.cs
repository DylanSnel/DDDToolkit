using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Membership;
using Examples.Tenancy.Projects.Domain.Aggregates.ProjectRoles.ValueObjects;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects.Entities;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects.Events;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects.ValueObjects;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects;

/// <summary>
/// A project: work in one tenant, hanging at one unit of its organization, done by a crew. The aggregate root of
/// this module.
/// </summary>
/// <remarks>
/// Who may see a project, and change it, is not the project's business. A seat reaches it in two ways: a role
/// held at the project's unit or above it, which Tenancy knows, and a project role on its crew, which the project
/// knows. The Membership package asks both inside one query. What the project keeps to itself is the crew, and
/// the rules about it that need nothing but the project:
/// <list type="bullet">
/// <item>A seat is on the crew once, for a period, and holds each project role at most once, each for a period
/// of its own.</item>
/// <item>There is one owner, always on the crew, with no end, holding the crew lead's role, with no end either.
/// The owner cannot be taken off the crew, nor lose that role, until somebody else has been named owner; the old
/// owner then loses the lead role, and that role alone: the seat stays on the crew, with whatever else it holds
/// there.</item>
/// <item>A closed project changes no more, until it is reopened.</item>
/// <item>It may have a planned range, the days the work is planned for: a first and a last day, the last not
/// before the first. A project without one is simply not planned yet.</item>
/// </list>
/// The rules about the crew are the Membership package's, kept through <c>Members</c>, the member list its
/// generator writes on the project from <see cref="Crew"/>, <see cref="OwnerSeatId"/> and <see cref="Codes"/>.
/// Each crew method here is the project's guard, a closed project changes nothing, in front of one call to it,
/// and the event the project raises for what the call answered. Which role is the crew lead's, and whether a
/// role or a seat may go on a crew at all, the command asks and passes in; the member list refuses what the
/// answer rules out.
/// <para>
/// Each method refuses a precondition with a coded <see cref="Exceptions.RefusalException"/> before it changes
/// anything. The nested invariants, one file each in <c>Invariants/</c>, state five of the same rules after the
/// fact, with the same codes, as the net under code that went round the methods; the save runs them.
/// </para>
/// </remarks>
[AggregateRoot<ProjectId>]
public sealed partial class Project
{
    /// <summary>The longest number.</summary>
    public const int LongestNumber = 40;

    /// <summary>The longest name.</summary>
    public const int LongestName = 200;

    /// <summary>
    /// Opens a project at <paramref name="unitId"/>, with <paramref name="owner"/> on its crew, holding
    /// <paramref name="ownerRole"/> there, both from <paramref name="now"/> on and with no end. A project that
    /// was never opened is not a project, so opening it is what constructing it means.
    /// </summary>
    /// <param name="id">Its id.</param>
    /// <param name="tenantId">The tenant it belongs to, for good.</param>
    /// <param name="number">Its number, unique in the tenant: trimmed, 1 to <see cref="LongestNumber"/> characters, and never changed.</param>
    /// <param name="name">Its name: trimmed, 1 to <see cref="LongestName"/> characters.</param>
    /// <param name="unitId">The unit it hangs at.</param>
    /// <param name="owner">The seat that owns it.</param>
    /// <param name="ownerRole">The owner's role on the crew: the tenant's crew lead's project role, which the command finds.</param>
    /// <param name="now">When it is opened.</param>
    /// <param name="planned">The days the work is planned for, or <see langword="null"/> when it is not planned yet.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.number-invalid</c>, <c>projects.name-invalid</c>, <c>projects.planned-range-invalid</c>.
    /// </exception>
    public Project(
        ProjectId id,
        TenantId tenantId,
        string number,
        string name,
        OrganizationUnitId unitId,
        SeatId owner,
        ProjectRoleId ownerRole,
        DateTimeOffset now,
        DateRange? planned = null) : base(id)
    {
        Number = CheckedNumber(number);
        Name = CheckedName(name);
        Planned = CheckedPlanned(planned);
        TenantId = tenantId;
        UnitId = unitId;
        State = ProjectState.Open;
        OwnerSeatId = owner;

        Members.Open(ownerRole, now);

        RaiseDomainEvent(new ProjectOpened(id, tenantId, unitId, owner));
    }

    /// <summary>The tenant the project belongs to. It never changes; the tenant filter reads it.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The project's number, such as <c>P-001</c>, unique in its tenant and never changed.</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>The project's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The unit the project hangs at: a role held there or above it reaches the project.</summary>
    public OrganizationUnitId UnitId { get; private set; }

    /// <summary>Whether the project is open or closed.</summary>
    public ProjectState State { get; private set; }

    /// <summary>The seat that owns the project, always on its crew, holding the crew lead role there.</summary>
    public SeatId OwnerSeatId { get; private set; }

    /// <summary>
    /// The days the work is planned for, or <see langword="null"/> while it is not planned. Another module that
    /// records what happened on a project keeps to these days; Projects tells it through its gate.
    /// </summary>
    public DateRange? Planned { get; private set; }

    /// <summary>The crew. Read-only outside the aggregate; the generated <c>_crew</c> field is what Entity Framework maps.</summary>
    public partial IReadOnlyList<CrewMember> Crew { get; }

    /// <summary>
    /// The codes the rules about the crew refuse under: the Membership package's rules under this module's codes.
    /// With the crew and the owner, what the package's generator writes the member list from.
    /// </summary>
    private static MembershipCodes Codes => ProjectRefusals.Membership;

    /// <summary>Renames the project. The same name again changes nothing.</summary>
    /// <exception cref="Exceptions.RefusalException"><c>projects.closed</c>, <c>projects.name-invalid</c>.</exception>
    public void Rename(string name)
    {
        RequireOpen();
        var renamed = CheckedName(name);
        if (string.Equals(renamed, Name, StringComparison.Ordinal))
        {
            return;
        }

        Name = renamed;
        RaiseDomainEvent(new ProjectRenamed(Id));
    }

    /// <summary>
    /// Plans the project for <paramref name="planned"/>, or takes its planned range away with
    /// <see langword="null"/>. The same range again changes nothing.
    /// </summary>
    /// <remarks>
    /// What other modules recorded on the project before stays as it is: the range says what is taken from now on.
    /// </remarks>
    /// <exception cref="Exceptions.RefusalException"><c>projects.closed</c>, <c>projects.planned-range-invalid</c>.</exception>
    public void Plan(DateRange? planned)
    {
        RequireOpen();
        var range = CheckedPlanned(planned);
        if (Equals(range, Planned))
        {
            return;
        }

        Planned = range;
        RaiseDomainEvent(new ProjectPlanned(Id));
    }

    /// <summary>
    /// Hangs the project at another unit, which changes who reaches it through the organization. The unit it
    /// hangs at already changes nothing. Whether the unit is active is the tenant's to say: the command asks.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException"><c>projects.closed</c>.</exception>
    public void MoveToUnit(OrganizationUnitId unit)
    {
        RequireOpen();
        if (unit == UnitId)
        {
            return;
        }

        var from = UnitId;
        UnitId = unit;
        RaiseDomainEvent(new ProjectMoved(Id, from, unit));
    }

    /// <summary>Closes the project. Its crew keeps seeing it; nothing about it changes until it is reopened.</summary>
    /// <exception cref="Exceptions.RefusalException"><c>projects.closed</c> when it is closed already.</exception>
    public void Close()
    {
        RequireOpen();
        State = ProjectState.Closed;
        RaiseDomainEvent(new ProjectClosed(Id));
    }

    /// <summary>
    /// Reopens a closed project, with the unit, crew and owner it was closed with: it can be worked on again.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException"><c>projects.not-closed</c> when it is open.</exception>
    public void Reopen()
    {
        if (State != ProjectState.Closed)
        {
            throw ProjectRefusals.Refuse(ProjectRefusals.NotClosed);
        }

        State = ProjectState.Open;
        RaiseDomainEvent(new ProjectReopened(Id));
    }

    /// <summary>
    /// Puts a seat on the crew for <paramref name="period"/>, with no role yet: on the crew it sees the project,
    /// and what else it may do there comes with the roles it is given (<see cref="GiveCrewRole"/>). A seat is on
    /// the crew once: a membership it still has at <paramref name="now"/>, one that runs or one still to start,
    /// is refused, and one that has ended by then is replaced, with the roles that were held in it.
    /// </summary>
    /// <param name="seat">The seat; the command has checked it is an active seat of the tenant.</param>
    /// <param name="period">When the membership counts.</param>
    /// <param name="now">When it is put on the crew: what a membership it had before is judged by.</param>
    /// <param name="addedBy">Who put it there, or <see langword="null"/> for system work.</param>
    /// <exception cref="Exceptions.RefusalException"><c>projects.closed</c>, <c>projects.invalid-period</c>, <c>projects.already-on-crew</c>.</exception>
    public CrewMember AddToCrew(SeatId seat, MemberPeriod period, DateTimeOffset now, SeatId? addedBy)
    {
        RequireOpen();
        var member = Members.Add(seat, period, now, addedBy);
        RaiseDomainEvent(new CrewMemberAdded(Id, seat));
        return member;
    }

    /// <summary>
    /// Puts a seat on the crew for <paramref name="period"/> and gives it <paramref name="role"/> for the same
    /// period, in one change: what <see cref="AddToCrew(SeatId, MemberPeriod, DateTimeOffset, SeatId?)"/> and
    /// <see cref="GiveCrewRole"/> do one after the other.
    /// </summary>
    /// <param name="seat">The seat; the command has checked it is an active seat of the tenant.</param>
    /// <param name="role">The role; the command has checked it is a project role of the tenant in use.</param>
    /// <param name="period">When the membership, and the role, count.</param>
    /// <param name="now">When it is put on the crew.</param>
    /// <param name="addedBy">Who put it there, or <see langword="null"/> for system work.</param>
    /// <exception cref="Exceptions.RefusalException"><c>projects.closed</c>, <c>projects.invalid-period</c>, <c>projects.already-on-crew</c>.</exception>
    public CrewMember AddToCrew(SeatId seat, ProjectRoleId role, MemberPeriod period, DateTimeOffset now, SeatId? addedBy)
    {
        RequireOpen();
        var member = Members.Add(seat, role, period, now, addedBy);
        RaiseDomainEvent(new CrewMemberAdded(Id, seat));
        RaiseDomainEvent(new CrewRoleGiven(Id, seat, role));
        return member;
    }

    /// <summary>
    /// Gives a crew member a role on the project for <paramref name="period"/>, next to whatever roles it holds
    /// there already. A seat holds a role once: one it still holds at <paramref name="now"/>, now or from a later
    /// start, is refused, and one that has ended by then is replaced. A seat whose membership has ended is not on
    /// the crew for this: a role counts only while the membership does. Put the seat on the crew again first.
    /// </summary>
    /// <param name="seat">The seat on the crew.</param>
    /// <param name="role">The role; the command has checked it is a project role of the tenant in use.</param>
    /// <param name="period">When the role counts. It counts only while the membership does.</param>
    /// <param name="now">When it is given.</param>
    /// <param name="givenBy">Who gave it, or <see langword="null"/> for system work.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.closed</c>, <c>projects.invalid-period</c>, <c>projects.member-not-found</c>, <c>projects.crew-role-held</c>.
    /// </exception>
    public void GiveCrewRole(SeatId seat, ProjectRoleId role, MemberPeriod period, DateTimeOffset now, SeatId? givenBy)
    {
        RequireOpen();
        Members.GiveRole(seat, role, period, now, givenBy);
        RaiseDomainEvent(new CrewRoleGiven(Id, seat, role));
    }

    /// <summary>
    /// Takes a role from a crew member, who stays on the crew with whatever else it holds there. The owner keeps
    /// the crew lead's role, and never ends up without any role that has no end, until somebody else is named owner.
    /// </summary>
    /// <param name="seat">The seat on the crew.</param>
    /// <param name="role">The role to take.</param>
    /// <param name="leadRole">
    /// The tenant's crew lead role, as the command found it, or <see langword="null"/> when the tenant has none
    /// in use. Read for the owner only.
    /// </param>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.closed</c>, <c>projects.member-not-found</c>, <c>projects.crew-role-not-found</c>,
    /// <c>projects.owner-protected</c>.
    /// </exception>
    public void TakeCrewRole(SeatId seat, ProjectRoleId role, ProjectRoleId? leadRole)
    {
        RequireOpen();
        Members.TakeRole(seat, role, leadRole);
        RaiseDomainEvent(new CrewRoleTaken(Id, seat, role));
    }

    /// <summary>Takes a seat off the crew, with every role it holds there. The owner stays until somebody else is named owner.</summary>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.closed</c>, <c>projects.member-not-found</c>, <c>projects.owner-protected</c>.
    /// </exception>
    public void RemoveFromCrew(SeatId seat)
    {
        RequireOpen();
        Members.Remove(seat);
        RaiseDomainEvent(new CrewMemberRemoved(Id, seat));
    }

    /// <summary>
    /// Names another owner: on the crew from now on and with no end, added when they are not on it yet, holding
    /// <paramref name="leadRole"/> there with no end. A seat whose time on the crew had ended begins anew, as the
    /// lead and nothing else: the roles of the ended membership do not come back. One whose time was to end keeps
    /// its place for good, and the roles it held there end where the membership would have. The old owner loses
    /// the lead role and nothing else: the seat stays on the crew, with every other role it holds there.
    /// </summary>
    /// <param name="seat">The seat that is to own the project; the command has checked it is an active seat of the tenant.</param>
    /// <param name="leadRole">The tenant's crew lead role, as the command found it.</param>
    /// <param name="now">When the owner is named.</param>
    /// <exception cref="Exceptions.RefusalException"><c>projects.closed</c>, <c>projects.already-owner</c>.</exception>
    public void ChangeOwner(SeatId seat, ProjectRoleId leadRole, DateTimeOffset now)
    {
        RequireOpen();
        var named = Members.NameOwner(seat, leadRole, now);
        OwnerSeatId = named.Owner;

        // The roles of a membership that had ended, or that never counted inside the one it had: taken.
        foreach (var dropped in named.RolesDropped)
        {
            RaiseDomainEvent(new CrewRoleTaken(Id, seat, dropped.RoleId));
        }

        // On the crew from now on: a seat that was not on it, and one whose time on it had ended and begins anew,
        // as a seat put on the crew again does.
        if (named.Added || named.BeganAnew)
        {
            RaiseDomainEvent(new CrewMemberAdded(Id, seat));
        }

        if (named.RoleGiven)
        {
            RaiseDomainEvent(new CrewRoleGiven(Id, seat, leadRole));
        }

        if (named.RoleTaken)
        {
            RaiseDomainEvent(new CrewRoleTaken(Id, named.Previous, leadRole));
        }

        RaiseDomainEvent(new OwnerChanged(Id, named.Previous, seat));
    }

    private void RequireOpen()
    {
        if (State == ProjectState.Closed)
        {
            throw ProjectRefusals.Refuse(ProjectRefusals.Closed);
        }
    }

    private static string CheckedNumber(string number)
        => number?.Trim() is { Length: > 0 and <= LongestNumber } trimmed
            ? trimmed
            : throw ProjectRefusals.Refuse(ProjectRefusals.NumberInvalid, ("Max", LongestNumber));

    private static string CheckedName(string name)
        => name?.Trim() is { Length: > 0 and <= LongestName } trimmed
            ? trimmed
            : throw ProjectRefusals.Refuse(ProjectRefusals.NameInvalid, ("Max", LongestName));

    private static DateRange? CheckedPlanned(DateRange? planned)
        => planned is null || planned.IsValid
            ? planned
            : throw ProjectRefusals.Refuse(ProjectRefusals.PlannedRangeInvalid);
}
