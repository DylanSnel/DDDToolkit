namespace DDDToolkit.Access;

/// <summary>
/// Says on a request that its caller holds a permission that is held without a marker: for the whole tenant, where Tenancy
/// says so.
/// <code>
/// [RequiresPermission(BillingPermissions.CloseTheBooks)]                                   // for the whole tenant
/// public sealed record CloseTheBooks(int Year) : IBillingRequest;
/// </code>
/// A permission held at a place the request names goes on the marker of that place, which carries it:
/// <code>
/// public sealed record OpenLedger(string Name, [AtUnit(BillingPermissions.OpenLedger)] OrganizationUnitId Unit) : IBillingRequest;
///
/// public sealed record ShareDocument(
///     [OnResource(DocumentPermissions.Share)] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
/// </code>
/// </summary>
/// <remarks>
/// What a permission without a marker means is the package's to say, with <see cref="PermissionWithoutMarkerAttribute"/>.
/// Tenancy says it is <c>TenancyAccess.AtTenant(permission)</c>. A project where no package says it, or where two do,
/// cannot tell where such a permission is held, and there the attribute is DDD00080. The toolkit knows no package's
/// marker by name: a package declares its markers with <see cref="PermissionMarkerAttribute"/>.
/// <para>
/// The toolkit's generator turns the attribute into one line of plain C# at compile time, in the module's request
/// interface (<c>[AccessRequests]</c>, declared <c>partial</c>): <c>CloseTheBooks =&gt; TenancyAccess.AtTenant("billing.close-the-books")</c>.
/// So nothing is read by reflection, the request need not be partial, and it answers
/// <see cref="IRequireAccess.RequiredAccess"/> with that case wherever that is read. A requirement computed from the
/// request's values, a permission at a unit where one is given and for the whole tenant otherwise say, is written as that
/// property instead.
/// </para>
/// <para>
/// Beside other requirement attributes and markers it is one more requirement, and all of them hold:
/// <c>AccessRequirement.All(...)</c>, in the order they are written, the attributes on the request before the markers on
/// its members. <see cref="OrOwnAttribute"/> on the member that names the caller's own makes the permission an
/// alternative where it is the request's one permission: <c>AccessRequirement.Any(AccessRequirement.Own(request.Seat), ...)</c>.
/// </para>
/// <para>
/// On the module's interface, <c>[AccessRequests, RequiresPermission(BillingPermissions.Read)]</c>, it is the module's
/// default: a request of it that states nothing of its own requires the permission for the whole tenant.
/// </para>
/// </remarks>
/// <param name="permission">The permission the caller holds: a constant, one of the catalogue's.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    /// <summary>The permission the caller holds.</summary>
    public string Permission { get; } = permission;
}
