using Campus.Courses;
using Campus.Tenants;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Campus.Access;

/// <summary>
/// What a request of the campus implements to say what it requires of its caller: the interface Tenancy's check and the
/// courses' check are both registered for, so one request may combine a case of each.
/// </summary>
/// <remarks>
/// Marked and declared partial as a module declares its own, so a request that states its requirement with attributes
/// answers it from the line the toolkit's generator writes into it. The host references no dispatcher, so no behavior
/// is written beside it: whoever sends a request asks the set of checks.
/// </remarks>
[AccessRequests]
public partial interface ICampusRequest : IRequireAccess;

/// <summary>Requests of the campus whose requirements combine cases of both supporting domains, or the caller's own with a key.</summary>
public static class CampusRequests
{
    /// <summary>
    /// Gives a course at another unit: the grading key on the course, through its members or from above, and the same key
    /// at the unit it moves to, both, the course's first. A key of Membership's and a key of Tenancy's in one requirement.
    /// </summary>
    /// <param name="Course">The course.</param>
    /// <param name="To">The unit it is given at from now on.</param>
    public sealed record MoveCourse(CourseId Course, OrganizationUnitId To) : ICampusRequest
    {
        /// <inheritdoc />
        AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.All(
            MemberAccess.On(CourseKeys.Grade, Course),
            TenancyAccess.AtUnit(CourseKeys.Grade, To));
    }

    /// <summary>
    /// Takes a tutor off a course: oneself, or anybody with the key that writes the course's outline on it. Yours, or the
    /// key on the resource.
    /// </summary>
    /// <param name="Course">The course.</param>
    /// <param name="Seat">The seat that leaves it.</param>
    [RequiresKey(CourseKeys.Outline)]
    public sealed record LeaveCourse([OnResource] CourseId Course, [OrOwn] SeatId Seat) : ICampusRequest;

    /// <summary>The college's access history: one of the application's operators, or a seat that manages seats for the whole college.</summary>
    public sealed record CollegeAuditLog : ICampusRequest
    {
        /// <inheritdoc />
        AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.Any(
            TenancyAccess.RequiresOperator(),
            TenancyAccess.ForTheWholeTenant(TenancyKeys.SeatsManage));
    }
}
