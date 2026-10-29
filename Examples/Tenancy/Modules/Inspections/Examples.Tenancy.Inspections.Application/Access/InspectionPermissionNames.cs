namespace Examples.Tenancy.Inspections.Application.Access;

/// <summary>The permissions Inspections asks for.</summary>
/// <remarks>
/// Kept in the module, not in a contracts project: no other module asks for them. The host names them when it
/// builds the starter roles, which a composition root may.
/// </remarks>
public static class InspectionPermissionNames
{
    /// <summary>Record an inspection on a project. Asked of Projects' gate, on the project.</summary>
    public const string Record = "inspections.record";
}
