namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// Marks the unit at which the caller of a request of this module holds a permission, there or above it, and carries
/// that permission: the module's own case, <see cref="ProjectsRequirement.AtUnit"/>, which refuses under
/// <c>projects.not-permitted</c>.
/// <code>
/// public sealed record MoveProjectToUnit(
///     [OnResource(MoveProjectToUnit.RequiredPermission)] ProjectId Id,
///     [AtProjectUnit(MoveProjectToUnit.DestinationPermission)] OrganizationUnitId UnitId,
///     [ExpectedVersion] long? ExpectedVersion = null) : ICommand, IProjectsRequest;
/// </code>
/// is <c>AccessRequirement.All(MemberAccess.On(..., request.Id, request.ExpectedVersion), ProjectsRequirement.HeldAt(..., request.UnitId))</c>.
/// </summary>
/// <remarks>
/// The module declares it the way a package declares its markers, with the toolkit's <see cref="PermissionMarkerAttribute"/>,
/// so the generator writes its call into <see cref="IProjectsRequest"/> beside the packages' markers. Tenancy's
/// <c>[AtUnit(permission)]</c> says the same at a unit, and refuses under Tenancy's code.
/// </remarks>
/// <param name="permission">The permission the caller holds at the unit.</param>
[PermissionMarker(typeof(ProjectsRequirement), nameof(ProjectsRequirement.HeldAt))]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class AtProjectUnitAttribute(string permission) : Attribute
{
    /// <summary>The permission the caller holds at the unit.</summary>
    public string Permission { get; } = permission;
}
