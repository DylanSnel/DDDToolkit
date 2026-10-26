namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// How the caller reaches a project for a permission: this module's words for the Membership package's
/// <c>MemberVia</c>, which every answer of the module says them in.
/// </summary>
public enum ProjectVia
{
    /// <summary>
    /// Through the project's crew: the caller is on it, now, holding a project role there, now, that gives the
    /// permission; or owns the project, which gives every permission of a lead. To see the project, being on its crew is
    /// enough.
    /// </summary>
    Crew,

    /// <summary>Through the organization: the caller holds the permission at the project's unit or above it.</summary>
    Organization,

    /// <summary>The application's own work in the project's tenant, which holds every permission there.</summary>
    System,
}
