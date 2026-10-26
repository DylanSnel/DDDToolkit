namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// How the caller reaches a project for a key: this module's words for the Membership package's
/// <c>MemberVia</c>, which every answer of the module says them in.
/// </summary>
public enum ProjectVia
{
    /// <summary>
    /// Through the project's crew: the caller is on it, now, holding a project role there, now, that gives the
    /// key; or owns the project, which gives every key of a lead. To see the project, being on its crew is
    /// enough.
    /// </summary>
    Crew,

    /// <summary>Through the organization: the caller holds the key at the project's unit or above it.</summary>
    Organization,

    /// <summary>The application's own work in the project's tenant, which holds every key there.</summary>
    System,
}
