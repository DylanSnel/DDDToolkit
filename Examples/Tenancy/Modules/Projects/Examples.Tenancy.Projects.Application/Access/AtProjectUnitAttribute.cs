namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// Marks the unit at which the caller of a request of this module holds a permission, there or at a unit above it. The
/// marker's constructor takes the permission. The generator turns the marker into the module's own case,
/// <see cref="ProjectsRequirement.AtUnit"/>, which refuses with <c>projects.not-permitted</c>.
/// <code>
/// public sealed record MoveProjectToUnit(
///     [OnResource(ProjectPermissionNames.Edit)] ProjectId Id,
///     [AtProjectUnit(ProjectPermissionNames.Open)] OrganizationUnitId UnitId,
///     [ExpectedVersion] long? ExpectedVersion = null) : ICommand, IProjectsRequest;
/// </code>
/// becomes <c>AccessRequirement.All(MemberAccess.On(..., request.Id, request.ExpectedVersion), ProjectsRequirement.HeldAt(..., request.UnitId))</c>.
/// </summary>
/// <remarks>
/// The module declares the marker with the toolkit's <see cref="PermissionMarkerAttribute"/>, as a package declares its
/// markers. So the generator writes its call into <see cref="IProjectsRequest"/>, next to the calls of the packages'
/// markers. Tenancy's <c>[AtUnit(permission)]</c> checks the same permission at a unit, and refuses with Tenancy's code,
/// <c>tenancy.not-permitted</c>.
/// </remarks>
/// <param name="permission">The permission the caller holds at the unit.</param>
[PermissionMarker(typeof(ProjectsRequirement), nameof(ProjectsRequirement.HeldAt))]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class AtProjectUnitAttribute(string permission) : Attribute
{
    /// <summary>The permission the caller holds at the unit.</summary>
    public string Permission { get; } = permission;
}
