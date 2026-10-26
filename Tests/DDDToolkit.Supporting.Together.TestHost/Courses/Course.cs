using Campus.Tenants;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Invariants;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.Access;

namespace Campus.Courses;

/// <summary>A course's id.</summary>
[EntityId<Guid>]
public readonly partial record struct CourseId;

/// <summary>The id of a course's member row.</summary>
[EntityId<Guid>]
public readonly partial record struct CourseTutorId;

/// <summary>The id of a role of courses: what a tutor holds a role by.</summary>
[EntityId<Guid>]
public readonly partial record struct CourseRoleId;

/// <summary>
/// Somebody who teaches on a course: a seat of the college, holding roles of courses that the college made for
/// itself, each by the role's id.
/// </summary>
[Member<CourseTutorId, SeatId, CourseRoleId, Course>]
public sealed partial class CourseTutor;

/// <summary>
/// A course, given at a faculty or an institute of a college: the usual resource of an application with an
/// organization. Its members are seats; the roles they hold on it are roles of courses, which the application
/// keeps for each college (<see cref="CourseRole"/>); and somebody who holds a key in the organization, at the
/// unit the course is given at or above it, holds that key on the course without teaching on it.
/// </summary>
[AggregateRoot<CourseId>]
public sealed partial class Course
{
    /// <summary>Sets up a course at a unit, with <paramref name="owner"/> on it from <paramref name="now"/> on, for good, in the college's role for owners.</summary>
    public Course(CourseId id, TenantId tenant, OrganizationUnitId unit, string title, SeatId owner, CourseRoleId ownerRole, DateTimeOffset now) : base(id)
    {
        TenantId = tenant;
        UnitId = unit;
        Title = title;
        OwnerSeatId = owner;
        Members.Open(ownerRole, now);
    }

    /// <summary>The college the course is of.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The faculty or institute the course is given at: where it sits.</summary>
    public OrganizationUnitId UnitId { get; private set; }

    /// <summary>What the course is called.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The seat that owns the course.</summary>
    public SeatId OwnerSeatId { get; private set; }

    /// <summary>Those who teach on the course.</summary>
    public partial IReadOnlyList<CourseTutor> Tutors { get; }

    /// <summary>
    /// The codes the rules about a course's tutors refuse under. With the collection above and the owner, it is what the
    /// toolkit writes the member list from: <c>Members</c>, which the methods below change the members through.
    /// </summary>
    private static MembershipCodes Codes => CourseMembership.Codes;

    /// <summary>Takes a seat on to the course, with no role yet.</summary>
    public CourseTutor TakeOn(SeatId seat, MemberPeriod period, DateTimeOffset now, SeatId? by) => Members.Add(seat, period, now, by);

    /// <summary>Takes a seat on to the course in a role, both for the same period.</summary>
    public CourseTutor TakeOn(SeatId seat, CourseRoleId role, MemberPeriod period, DateTimeOffset now, SeatId? by) => Members.Add(seat, role, period, now, by);

    /// <summary>Gives somebody on the course a role.</summary>
    public void GiveRole(SeatId to, CourseRoleId role, MemberPeriod period, DateTimeOffset now, SeatId? by) => Members.GiveRole(to, role, period, now, by);

    /// <summary>Moves the course to another unit.</summary>
    public void GiveAt(OrganizationUnitId unit) => UnitId = unit;

    /// <summary>A seat is on a course once.</summary>
    public sealed class OnePlacePerSeat : IInvariant<Course>
    {
        /// <inheritdoc />
        public string Code => CourseMembership.Codes[MembershipRefusals.AlreadyMember];

        /// <inheritdoc />
        public InvariantFailure? Check(Course entity) => entity.Members.OneMembershipPerMember();
    }

    /// <summary>The owner stays on the course, with a role that does not run out.</summary>
    public sealed class OwnerKeepsAPlace : IInvariant<Course>
    {
        /// <inheritdoc />
        public string Code => CourseMembership.Codes[MembershipRefusals.OwnerProtected];

        /// <inheritdoc />
        public InvariantFailure? Check(Course entity) => entity.Members.OwnerStays();
    }
}

/// <summary>The keys a course is asked about.</summary>
public static class CourseKeys
{
    /// <summary>See a course: what teaching on it gives, and what the organization gives to see it by.</summary>
    public const string See = "courses.see";

    /// <summary>Grade the work handed in for a course.</summary>
    public const string Grade = "courses.grade";

    /// <summary>Write a course's outline.</summary>
    public const string Outline = "courses.outline";

    /// <summary>
    /// Dissolve a course: its owner's alone. No role of courses may give it, and it is no key of the college's
    /// catalogue, so no role of the organization holds it either.
    /// </summary>
    public const string Dissolve = "courses.dissolve";
}

/// <summary>
/// The rules of access through a course's tutors, as an application with an organization usually says them:
/// a member is the caller's seat, which Tenancy resolves; the roles are kept for the resource, rows of
/// <see cref="CourseRole"/> that a college makes for itself; and a key held in the organization where the
/// course is given, or above, reaches it. The three names are those of Tenancy's functions, for a database that
/// answers the questions itself.
/// </summary>
public static class CourseMembership
{
    /// <summary>The starter role that sees and grades.</summary>
    public const string Tutor = "tutor";

    /// <summary>The starter role that sees a course and writes its outline.</summary>
    public const string Convenor = "convenor";

    /// <summary>The scope the application's own work on its courses runs in.</summary>
    public const string Scope = "campus";

    /// <summary>The codes a course refuses with.</summary>
    public static MembershipCodes Codes { get; } = MembershipCodes.Under("courses");

    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = new(
        "courses",
        keys: [CourseKeys.See, CourseKeys.Grade, CourseKeys.Outline, CourseKeys.Dissolve],
        roles: [new(Tutor, [CourseKeys.See, CourseKeys.Grade]), new(Convenor, [CourseKeys.See, CourseKeys.Outline])],
        members: MemberSource.Resolved(TenancyFunctions.CallerSeat),
        seeKey: CourseKeys.See,
        memberKeys: MemberKeys.Only(CourseKeys.See, CourseKeys.Grade, CourseKeys.Outline),
        codes: Codes,
        above: new(TenancyFunctions.UnitsWhereIHold),
        rolesKept: true,

        // The application's own work on a course runs in this scope, and holds every key there. A lab's
        // rules name no scope, so the same work holds nothing on a lab.
        systemScopes: [Scope]);
}
