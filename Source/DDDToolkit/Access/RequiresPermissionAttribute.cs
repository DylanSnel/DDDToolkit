namespace DDDToolkit.Access;

/// <summary>
/// States on a request that its caller holds a permission without a marker. With Tenancy, such a permission is held for
/// the whole tenant.
/// <code>
/// [RequiresPermission(BillingPermissions.CloseTheBooks)]                                   // for the whole tenant
/// public sealed record CloseTheBooks(int Year) : IBillingRequest;
/// </code>
/// A permission held at a place that the request names goes on the marker of that place:
/// <code>
/// public sealed record OpenLedger(string Name, [AtUnit(BillingPermissions.OpenLedger)] OrganizationUnitId Unit) : IBillingRequest;
///
/// public sealed record ShareDocument(
///     [OnResource(DocumentPermissions.Share)] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
/// </code>
/// </summary>
/// <remarks>
/// A package declares the call for a permission without a marker with <see cref="PermissionWithoutMarkerAttribute"/>.
/// Tenancy declares <c>TenancyAccess.AtTenant(permission)</c>. In a project where no package declares it, or where two
/// do, the generator cannot tell where such a permission is held, and the attribute is DDD00080. The toolkit knows no
/// package's marker by name. A package declares its markers with <see cref="PermissionMarkerAttribute"/>.
/// <para>
/// The toolkit's generator turns the attribute into one line of plain C# at compile time, in the module's request
/// interface (<c>[AccessRequests]</c>, declared <c>partial</c>): <c>CloseTheBooks =&gt; TenancyAccess.AtTenant("billing.close-the-books")</c>.
/// So nothing is read by reflection, and the request need not be partial. The request answers
/// <see cref="IRequireAccess.RequiredAccess"/> with that case wherever the property is read. Write the property instead
/// for a requirement computed from the request's values, for example a permission at a unit when a unit is given and for
/// the whole tenant otherwise.
/// </para>
/// <para>
/// Beside other requirement attributes and markers, the attribute is one more requirement. The generator combines all of
/// them with <c>AccessRequirement.All(...)</c>, in the order they are written, the attributes on the request before the
/// markers on its members. When this is the request's only permission, <see cref="OrOwnAttribute"/> on the member that
/// names the caller's own makes the permission an alternative:
/// <c>AccessRequirement.Any(AccessRequirement.Own(request.Seat), ...)</c>.
/// </para>
/// <para>
/// On the module's interface, <c>[AccessRequests, RequiresPermission(BillingPermissions.Read)]</c> is the module's
/// default. A request of the module that states no requirement of its own then requires the permission for the whole
/// tenant.
/// </para>
/// </remarks>
/// <param name="permission">The permission the caller holds: a constant, one of the catalogue's.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    /// <summary>The permission the caller holds.</summary>
    public string Permission { get; } = permission;
}
