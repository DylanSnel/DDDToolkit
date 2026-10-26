using Campus.Tenants;
using DDDToolkit.Supporting.Membership;

namespace Campus.Courses;

/// <summary>
/// A role of courses, as Membership declares one: an aggregate of the application's own, beside the course.
/// The application adds whose it is, a college's, and keeps the rows of one college from another's the way it
/// keeps every row of a tenant: by Tenancy's own rule, where its context maps the class.
/// </summary>
[KeptRole<CourseRoleId, Course>]
public sealed partial class CourseRole
{
    /// <summary>Makes a role of courses for a college, from what somebody entered or from a starter role.</summary>
    public CourseRole(CourseRoleId id, TenantId tenant, KeptRoleDraft draft) : base(id, draft, CourseMembership.Rules) => TenantId = tenant;

    /// <summary>The college the role is of: the application's own column, which Membership knows nothing of.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>Has the role give these keys.</summary>
    public void HaveItGive(params string[] keys) => SetKeys(keys, CourseMembership.Rules);

    /// <summary>Puts the role away: it gives nothing from then on.</summary>
    public void PutAway() => Archive(CourseMembership.Rules);
}
