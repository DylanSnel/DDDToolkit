using Campus.Tenants;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Tenancy;

namespace Campus.Courses;

/// <summary>
/// A role of courses, as Membership declares one: an aggregate of the application's own, beside the course.
/// The application adds whose it is, a college's, and marks it <c>[OfTenant]</c>. The toolkit's conventions then keep
/// the rows of one college from another's by Tenancy's own rule, and Membership's mapping gives a name and a starter role
/// once per college, with no line in the context.
/// </summary>
[CustomRole<CourseRoleId, Course>]
public sealed partial class CourseRole
{
    /// <summary>Makes a role of courses for a college, from what somebody entered or from a starter role.</summary>
    public CourseRole(CourseRoleId id, TenantId tenant, CustomRoleDraft draft) : base(id, draft, CourseMembership.Rules) => TenantId = tenant;

    /// <summary>The college the role is of: the application's own column, marked with Tenancy's marker of a row's tenant.</summary>
    [OfTenant]
    public TenantId TenantId { get; private set; }

    /// <summary>Has the role give these permissions.</summary>
    public void HaveItGive(params string[] permissions) => SetPermissions(permissions, CourseMembership.Rules);

    /// <summary>Puts the role away: it gives nothing from then on.</summary>
    public void PutAway() => Archive(CourseMembership.Rules);
}
