namespace DDDToolkit.Access;

/// <summary>
/// Says, of the assembly of a package, what <see cref="RequiresPermissionAttribute"/> means on a request that marks no member:
/// the call that makes the requirement from the permission alone. Tenancy says that a permission without a marker is held for the
/// whole tenant:
/// <code>
/// [assembly: PermissionWithoutMarker(typeof(TenancyAccess), nameof(TenancyAccess.AtTenant))]
/// </code>
/// so <c>[RequiresPermission(BillingPermissions.CloseTheBooks)]</c> on its own is <c>TenancyAccess.AtTenant("billing.close-the-books")</c>.
/// </summary>
/// <remarks>
/// One package says it. A project that references none that does, or two that do, cannot tell where a permission without a
/// marker is held, and there such a request is DDD00080, which asks for a marker.
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
