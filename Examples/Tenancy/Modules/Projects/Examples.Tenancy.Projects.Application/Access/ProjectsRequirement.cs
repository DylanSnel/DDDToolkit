namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// What a request of this module requires of its caller where only Projects can say whether the caller meets it:
/// a key held at a unit a project is to be opened at. A command says so through <see cref="IProjectsRequest"/>, and
/// <see cref="ProjectsAccessCheck"/> holds the caller to it before the handler runs.
/// </summary>
/// <remarks>
/// The other cases are not this module's to decide. A key held on a project, or the key a query of projects is
/// filtered by, is the Membership package's case (<c>MemberAccess.On</c>, <c>MemberAccess.SeenWith</c>), which
/// its check decides from the projects' rules; that the caller works in a tenant, or is an operator, is
/// Tenancy's. This one is left because it is asked where there is no project yet, and so no crew either: only
/// the organization holds a key there.
/// <para>
/// Tenancy has a case for a key at a unit too, <c>TenancyAccess.AtUnit</c>, and a module that needs nothing
/// more declares that one. This one is Projects' own for two things that case does not do: it refuses under the
/// module's own code, <c>projects.not-permitted</c>, which the module's clients read for every refusal about a
/// project, and it keeps the unit it checked for the handler (<see cref="Checked{T}"/>), which opens the project
/// at exactly that unit.
/// </para>
/// <para>
/// A closed set: the constructor is private, so the cases below are all there are, and each is a record, so two
/// requirements that say the same are equal. What a requirement cannot say stays in the handler, in plain sight:
/// that naming somebody else as owner needs a second key, that moving a project needs one at the destination.
/// </para>
/// </remarks>
public abstract record ProjectsRequirement : AccessRequirement
{
    private ProjectsRequirement()
    {
    }

    /// <summary>
    /// <paramref name="Key"/> is held by the caller at <paramref name="Unit"/>, there or above it: for what is asked
    /// where there is no project yet. Otherwise <c>projects.not-permitted</c>, naming the key and the unit.
    /// </summary>
    /// <param name="Key">The key the request needs at the unit.</param>
    /// <param name="Unit">The unit, from the request.</param>
    public sealed record AtUnit(string Key, OrganizationUnitId Unit) : ProjectsRequirement;
}
