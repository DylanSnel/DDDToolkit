using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy;
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
/// <item>There is one owner, always on the crew, with no end, and holding no project role: the owner holds what
/// the projects' permissions give an owner by owning the project. The owner cannot be taken off the crew until
/// somebody else has been named owner; the command that names one says what the old owner becomes, a crew member
/// in a project role, or nobody on the crew.</item>
/// <item>A closed project changes no more, until it is reopened.</item>
/// <item>It may have a planned range, the days the work is planned for: a first and a last day, the last not
/// before the first. A project without one is simply not planned yet.</item>
/// </list>
/// The rules about the crew are the Membership package's, kept through <c>Members</c>, the member list its
/// generator writes on the project from <see cref="Crew"/>, the owner it marks <c>[Owner]</c> and <see cref="Codes"/>.
/// The crew manager the generator writes beside it, <c>CrewMemberManager</c>, makes every change of the crew: it asks
/// whether the caller may and whether the seat and the role may go on a crew, then calls the project's guard,
/// <c>OnChangingMembers</c>, then the member list, then the hook that raises the project's event for what changed. The
/// project writes the guard and the events, and nothing else of a crew change.
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
    /// Opens a project at <paramref name="unitId"/>, with <paramref name="owner"/> on its crew from
    /// <paramref name="now"/> on and with no end, holding no project role: the owner holds what an owner does by
    /// owning it. A project that was never opened is not a project, so opening it is what constructing it means.
    /// </summary>
    /// <param name="id">Its id.</param>
    /// <param name="tenantId">The tenant it belongs to, for good.</param>
    /// <param name="number">Its number, unique in the tenant: trimmed, 1 to <see cref="LongestNumber"/> characters, and never changed.</param>
    /// <param name="name">Its name: trimmed, 1 to <see cref="LongestName"/> characters.</param>
    /// <param name="unitId">The unit it hangs at.</param>
    /// <param name="owner">The seat that owns it.</param>
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

        Members.Open(now);

        RaiseDomainEvent(new ProjectOpened(id, tenantId, unitId, owner));
    }

    /// <summary>
    /// The tenant the project belongs to. It never changes. <c>[OfTenant]</c> says so in the domain: every context that
    /// maps the project keeps it to the caller's tenant by this property, with the tenant filter and the save check.
    /// </summary>
    [OfTenant]
    public TenantId TenantId { get; private set; }

    /// <summary>The project's number, such as <c>P-001</c>, unique in its tenant and never changed.</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>The project's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The unit the project hangs at: a role held there or above it reaches the project.</summary>
    [At]
    public OrganizationUnitId UnitId { get; private set; }

    /// <summary>Whether the project is open or closed.</summary>
    public ProjectState State { get; private set; }

    /// <summary>The seat that owns the project, always on its crew, holding no project role there.</summary>
    [Owner]
    public SeatId OwnerSeatId { get; private set; }

    /// <summary>
    /// The days the work is planned for, or <see langword="null"/> while it is not planned. Another module that
    /// records what happened on a project keeps to these days; Projects tells it through its gate.
    /// </summary>
    public DateRange? Planned { get; private set; }

    /// <summary>The crew. Read-only outside the aggregate; the generated <c>_crew</c> field is what Entity Framework maps.</summary>
    public partial IReadOnlyList<CrewMember> Crew { get; }

    /// <summary>
    /// The codes the rules about the crew refuse under: the Membership package's rules under this module's own
    /// words for them, <c>projects.already-on-crew</c> and the like. Declared here, so the package's generator writes
    /// none of its own, and the member list and the projects' rules refuse under these.
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
    /// The guard in front of every change of the crew: a closed project changes no more. The crew manager the Membership
    /// package's generator writes, <c>CrewMemberManager</c>, makes every change and calls this first.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException"><c>projects.closed</c>.</exception>
    partial void OnChangingMembers() => RequireOpen();

    /// <summary>A seat went on the crew.</summary>
    partial void OnMemberAdded(CrewMember member) => RaiseDomainEvent(new CrewMemberAdded(Id, member.MemberId));

    /// <summary>A seat came off the crew, with every role it held there.</summary>
    partial void OnMemberRemoved(SeatId member) => RaiseDomainEvent(new CrewMemberRemoved(Id, member));

    /// <summary>A crew member was given a project role.</summary>
    partial void OnRoleGiven(SeatId member, MemberRole<SeatId, ProjectRoleId> role) => RaiseDomainEvent(new CrewRoleGiven(Id, member, role.RoleId));

    /// <summary>A project role was taken from a crew member.</summary>
    partial void OnRoleTaken(SeatId member, MemberRole<SeatId, ProjectRoleId> role) => RaiseDomainEvent(new CrewRoleTaken(Id, member, role.RoleId));

    /// <summary>
    /// Another owner was named, and <see cref="OwnerSeatId"/> holds it already: the project's events for everything that
    /// came with it, in this order. The roles the new owner held on the crew go, since it holds by owning now; it is on
    /// the crew from now on, where it was not or its time had ended; the old owner stays in a project role or leaves; and
    /// the owner changed.
    /// </summary>
    partial void OnOwnerNamed(OwnerNamed<SeatId, ProjectRoleId> named)
    {
        foreach (var dropped in named.RolesDropped)
        {
            RaiseDomainEvent(new CrewRoleTaken(Id, named.Owner, dropped.RoleId));
        }

        if (named.Added || named.BeganAnew)
        {
            RaiseDomainEvent(new CrewMemberAdded(Id, named.Owner));
        }

        if (named.PreviousRole is { } role)
        {
            RaiseDomainEvent(new CrewRoleGiven(Id, named.Previous, role));
        }
        else if (named.PreviousLeft)
        {
            RaiseDomainEvent(new CrewMemberRemoved(Id, named.Previous));
        }

        RaiseDomainEvent(new OwnerChanged(Id, named.Previous, named.Owner));
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
