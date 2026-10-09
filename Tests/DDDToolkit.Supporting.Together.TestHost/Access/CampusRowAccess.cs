using Campus.Courses;
using Campus.Labs;
using Campus.Tenants;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace Campus.Access;

// What the database itself lets a seat read of the courses and the labs, on a database that checks every row:
// thin rules of the application's own, which ask the set functions Membership writes for each resource. Those
// functions ask Tenancy's in turn, by the names the resources' rules give them. Tenancy keeps every row of
// these tables to its tenant by policies of its own; these say what a seat may read inside it.

/// <summary>The questions about courses the database answers, under the names a course's rules give its functions.</summary>
[AccessFunctions(Owner = "courses")]
public static partial class CourseQuestions
{
    /// <summary>The courses the caller sees: those it teaches on, and those given where it holds the key that sees.</summary>
    [AccessSet("courses_i_see")]
    public static partial AccessSet<CourseId> Seen();

    /// <summary>The courses on which <paramref name="key"/> is held by the caller: as its owner, through a role of courses, or from the organization.</summary>
    /// <param name="key">A permission key.</param>
    [AccessSet("courses_where_i_hold")]
    public static partial AccessSet<CourseId> HeldOn(string key);
}

/// <summary>The questions about labs the database answers, under the names a lab's rules give its functions.</summary>
[AccessFunctions(Owner = "labs")]
public static partial class LabQuestions
{
    /// <summary>The labs the caller sees: those it works in, and those that are where it holds the key that sees.</summary>
    [AccessSet("labs_i_see")]
    public static partial AccessSet<LabId> Seen();

    /// <summary>The labs on which <paramref name="key"/> is held by the caller: through a role of the college's, or from the organization.</summary>
    /// <param name="key">A permission key.</param>
    [AccessSet("labs_where_i_hold")]
    public static partial AccessSet<LabId> HeldOn(string key);
}

/// <summary>A seat reads the courses it sees.</summary>
[RowAccess<Course>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class SeatsReadTheCoursesTheySee
{
    /// <summary>Whether the caller sees <paramref name="course"/>.</summary>
    public static bool Allows(Course course, Caller caller) => CourseQuestions.Seen().Contains(course.Id);
}

/// <summary>
/// A seat reads the roles of courses of its own college. Membership gives the role table no rule: which of
/// its rows a caller reads is the application's to say, and what a role gives is read from those rows where a
/// request is checked.
/// </summary>
[RowAccess<CourseRole>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class SeatsReadTheRolesOfCoursesOfTheirCollege
{
    /// <summary>Whether <paramref name="role"/> is one of the calling seat's college.</summary>
    public static bool Allows(CourseRole role, Caller caller) => role.TenantId == TenancyRowAccess.CallerTenant<TenantId>();
}

/// <summary>A seat reads the labs it sees.</summary>
[RowAccess<Lab>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class SeatsReadTheLabsTheySee
{
    /// <summary>Whether the caller sees <paramref name="lab"/>.</summary>
    public static bool Allows(Lab lab, Caller caller) => LabQuestions.Seen().Contains(lab.Id);
}

/// <summary>
/// A member reads the people of its own part of the college: the seats placed at a unit it is placed at, or below
/// one. A read rule on Tenancy's seats takes the place of Tenancy's default, which lets every member read every seat,
/// so it names no role: it is for the signed-in users the default is for. The campus runs without it; the tests of
/// a narrower read of the seats add it to the rules the access files are written with.
/// </summary>
[RowAccess<Seat>(RowOperations.Read)]
public static partial class MembersReadThePeopleOfTheirUnits
{
    /// <summary>Whether <paramref name="seat"/> is placed where the calling seat is placed, or below.</summary>
    public static bool Allows(Seat seat, Caller caller) => TenancyRowAccess.SeatsInMyUnits<SeatId>().Contains(seat.Id);
}
