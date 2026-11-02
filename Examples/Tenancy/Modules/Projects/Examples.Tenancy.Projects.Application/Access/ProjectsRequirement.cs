namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// What a request of this module requires of its caller where only Projects can say whether the caller meets it:
/// a permission held at a unit a project is to be opened at. A command says so through <see cref="IProjectsRequest"/>, and
/// <see cref="ProjectsAccessCheck"/> holds the caller to it before the handler runs.
/// </summary>
/// <remarks>
/// The other cases are not this module's to decide. A permission held on a project, or the permission a query of projects is
/// filtered by, is the Membership package's case (<c>MemberAccess.On</c>, <c>MemberAccess.SeenWith</c>), which
/// its check decides from the projects' rules; that the caller works in a tenant, or is an operator, is
/// Tenancy's. This one is left because it is asked where there is no project yet, and so no crew either: only
/// the organization holds a permission there.
/// <para>
/// Tenancy has a case for a permission at a unit too, <c>TenancyAccess.AtUnit</c>, and a module that needs nothing
/// more declares that one. This one is Projects' own for what that case does not do: it refuses under the
/// module's own code, <c>projects.not-permitted</c>, naming the permission and the unit, which the module's clients read
/// for every refusal about a project.
/// </para>
/// <para>
/// The set of cases is closed. The constructor is private, so the cases below are the only ones. Each case is a record,
/// so two requirements with the same values are equal. A case combines with the packages' cases in the access check.
/// Moving a project needs the permission on the project and this case at the destination, combined with
/// <c>AccessRequirement.All</c>. The request states the second with the module's marker,
/// <see cref="AtProjectUnitAttribute"/>. Naming somebody else as the owner of a new project needs the caller's own seat,
/// or this case with the owner's permission, combined with <c>AccessRequirement.Any</c>. The handler checks that the
/// unit takes anything new, because that is the tenant's state and no permission of the caller.
/// </para>
/// </remarks>
public abstract record ProjectsRequirement : AccessRequirement
{
    private ProjectsRequirement()
    {
    }

    /// <summary>The case that the generator writes for <see cref="AtProjectUnitAttribute"/> on a request's unit: <see cref="AtUnit"/>.</summary>
    /// <param name="permission">The permission the request needs at the unit.</param>
    /// <param name="unit">The unit, from the request.</param>
    public static AtUnit HeldAt(string permission, OrganizationUnitId unit) => new(permission, unit);

    /// <summary>
    /// <paramref name="Permission"/> is held by the caller at <paramref name="Unit"/>, there or above it: for what is asked
    /// where there is no project yet. Otherwise <c>projects.not-permitted</c>, naming the permission and the unit.
    /// </summary>
    /// <param name="Permission">The permission the request needs at the unit.</param>
    /// <param name="Unit">The unit, from the request.</param>
    public sealed record AtUnit(string Permission, OrganizationUnitId Unit) : ProjectsRequirement;
}
