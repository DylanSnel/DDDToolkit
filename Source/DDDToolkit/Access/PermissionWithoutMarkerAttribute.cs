namespace DDDToolkit.Access;

/// <summary>
/// Names, on the assembly of a package, the call that <see cref="RequiresPermissionAttribute"/> becomes. That attribute
/// names a permission without a marker, so the call takes the permission alone. Tenancy declares that such a permission
/// is held for the whole tenant:
/// <code>
/// [assembly: PermissionWithoutMarker(typeof(TenancyAccess), nameof(TenancyAccess.AtTenant))]
/// </code>
/// So <c>[RequiresPermission(BillingPermissions.CloseTheBooks)]</c> becomes <c>TenancyAccess.AtTenant("billing.close-the-books")</c>.
/// </summary>
/// <remarks>
/// One package of an application declares it. In a project that references no such package, or two, the generator cannot
/// tell where a permission without a marker is held. There <c>[RequiresPermission]</c> is DDD00080. The fix is to put the
/// permission on the marker of the place where it is held.
/// </remarks>
/// <param name="factory">The class whose static method makes the requirement: <c>typeof(TenancyAccess)</c>.</param>
/// <param name="method">The method's name: <c>nameof(TenancyAccess.AtTenant)</c>.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class PermissionWithoutMarkerAttribute(Type factory, string method) : Attribute
{
    /// <summary>The class whose static method makes the requirement.</summary>
    public Type Factory { get; } = factory;

    /// <summary>The name of the method that makes it.</summary>
    public string Method { get; } = method;

    /// <summary>
    /// The requirement attributes a permission without a marker already requires, as <see cref="PermissionMarkerAttribute.Implies"/>
    /// says for a marker's: Tenancy's permission for the whole tenant names <c>[InTenant]</c>. Empty by default.
    /// </summary>
    public Type[] Implies { get; set; } = [];
}
