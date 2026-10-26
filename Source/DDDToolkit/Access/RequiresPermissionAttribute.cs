namespace DDDToolkit.Access;

/// <summary>
/// Says on a request that its caller holds a permission, and, with a marker on one of the request's members, where:
/// <code>
/// [RequiresPermission(BillingPermissions.CloseTheBooks)]                                   // for the whole tenant
/// public sealed record CloseTheBooks(int Year) : IBillingRequest;
///
/// [RequiresPermission(BillingPermissions.OpenLedger)]                                      // at that unit, or above it
/// public sealed record OpenLedger(string Name, [AtUnit] OrganizationUnitId Unit) : IBillingRequest;
///
/// [RequiresPermission(DocumentPermissions.Share)]                                          // on that resource, through its members
/// public sealed record ShareDocument(
///     [OnResource] DocumentId Document, UserId With, [ExpectedVersion] long? ExpectedVersion = null) : IDocumentsRequest;
/// </code>
/// </summary>
/// <remarks>
/// Where the permission is held is the marker's to say, and the case it gives belongs to the package that ships the marker:
/// <list type="table">
/// <item><term>no marker</term><description>for the whole tenant: Tenancy's <c>TenancyAccess.AtTenant(permission)</c></description></item>
/// <item><term><c>[AtUnit]</c></term><description>at that unit or above it: Tenancy's <c>TenancyAccess.AtUnit(permission, unit)</c></description></item>
/// <item><term><c>[OnResource]</c></term><description>on that resource, through its members: Membership's <c>MemberAccess.On(permission, resource)</c>, with the version the caller read where <c>[ExpectedVersion]</c> marks it</description></item>
/// </list>
/// A package says which marker is its own with <see cref="PermissionMarkerAttribute"/>, and what a permission without one means with
/// <see cref="PermissionWithoutMarkerAttribute"/>; the toolkit knows none of them by name. A marker goes on a positional
/// parameter of a record as it is, or on a property, and <c>[property: AtUnit]</c> is read as well.
/// <para>
/// The toolkit's generator turns the attribute and its marker into one line of plain C# at compile time, in the
/// module's request interface (<c>[AccessRequests]</c>, declared <c>partial</c>):
/// <c>OpenLedger request =&gt; TenancyAccess.AtUnit("billing.open-ledger", request.Unit)</c>. So nothing is read by
/// reflection, the request need not be partial, and it answers <see cref="IRequireAccess.RequiredAccess"/> with that
/// case wherever that is read. A requirement computed from the request's values, a permission at a unit where one is given
/// and for the whole tenant otherwise say, is written as that property instead.
/// </para>
/// <para>
/// <see cref="OrOwnAttribute"/> on the member that names the caller's own makes the permission an alternative: the request is
/// about the caller's own, or the caller holds the permission, <c>AccessRequirement.Any(AccessRequirement.Own(request.Seat), ...)</c>.
/// Stacked beside another requirement attribute both hold, <c>AccessRequirement.All(...)</c>, in the order they are
/// written; a second permission is not stacked, since its marker could not tell which permission it says where of, and two permissions are
/// written as the property, <c>AccessRequirement.All(TenancyAccess.AtUnit(permission, From), TenancyAccess.AtUnit(permission, To))</c>.
/// </para>
/// <para>
/// On the module's interface, <c>[AccessRequests, RequiresPermission(BillingPermissions.Read)]</c>, it is the module's default: a
/// request of it that states nothing of its own requires the permission for the whole tenant, since a default reads no member
/// of a request and so has no marker.
/// </para>
/// </remarks>
/// <param name="permission">The permission the caller holds: a constant, one of the catalogue's.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    /// <summary>The permission the caller holds.</summary>
    public string Permission { get; } = permission;
}
