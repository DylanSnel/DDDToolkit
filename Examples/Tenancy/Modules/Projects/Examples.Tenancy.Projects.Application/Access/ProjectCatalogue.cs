using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// Projects' part of the permission catalogue: the keys it asks for, declared next to the code that asks, and
/// added to the application's catalogue by the module's registration.
/// </summary>
/// <remarks>
/// A role of the organization holding one of these keys at a unit holds it for every project at that unit and
/// below it. A project role on a project's crew holds it for that project alone, and only the keys that act on a
/// project: never <see cref="OrganizationKeys"/>, nor any of Tenancy's (<see cref="CrewGives"/>). Editing and
/// managing the crew both mean seeing the project, so they bring <see cref="ProjectKeys.View"/> with them.
/// </remarks>
public static class ProjectCatalogue
{
    /// <summary>The module the keys are listed under.</summary>
    public const string Module = "Projects";

    /// <summary>The keys, as the catalogue lists them.</summary>
    public static IReadOnlyList<Permission> Permissions { get; } =
    [
        new(ProjectKeys.View, Module, "See a project and its crew", Order: 10),
        new(ProjectKeys.Open, Module, "Open a project at a unit", Order: 20),
        new(ProjectKeys.Edit, Module, "Rename or move a project", Implies: [ProjectKeys.View], Order: 30),
        new(ProjectKeys.Close, Module, "Close or reopen a project", Order: 40),
        new(ProjectKeys.ManageCrew, Module, "Add and remove crew members, give and take their project roles", Implies: [ProjectKeys.View], Order: 50),
        new(ProjectKeys.ChangeOwner, Module, "Name a project's owner", Order: 60),
    ];

    /// <summary>
    /// The keys a lead holds on a crew: every key of this module that a crew role can give. A project's owner
    /// holds them by owning it (<see cref="ProjectMembership"/>), whatever the tenant's crew lead role holds.
    /// </summary>
    public static IReadOnlyList<string> LeadKeys { get; } = [ProjectKeys.View, ProjectKeys.Edit, ProjectKeys.Close, ProjectKeys.ManageCrew];

    /// <summary>
    /// The keys held at a unit, never through a crew: a project role that holds them gives nothing with them.
    /// Opening a project is asked at a unit, where there is no project yet, and naming its owner is the
    /// organization's decision, not the crew's.
    /// </summary>
    public static IReadOnlyList<string> OrganizationKeys { get; } = [ProjectKeys.Open, ProjectKeys.ChangeOwner];

    /// <summary>
    /// Whether a project role on a project's crew gives <paramref name="key"/> on that project. Every key that acts
    /// on a project does. <see cref="OrganizationKeys"/> do not, and neither do Tenancy's own keys, which manage
    /// the organization itself: a project role that holds them gives nothing with them. The projects' rules say the
    /// same (<see cref="ProjectMembership.Rules"/>), for the access checks and for the database.
    /// </summary>
    /// <param name="key">A key of the catalogue.</param>
    public static bool CrewGives(string key)
        => !OrganizationKeys.Contains(key, StringComparer.Ordinal)
            && !TenancyKeys.Permissions.Any(permission => string.Equals(permission.Key, key, StringComparison.Ordinal));
}
