namespace DDDToolkit.Access;

/// <summary>
/// Says, of the assembly of a package, what <see cref="RequiresKeyAttribute"/> means on a request that marks no member:
/// the call that makes the requirement from the key alone. Tenancy says that a key without a marker is held for the
/// whole tenant:
/// <code>
/// [assembly: KeyWithoutMarker(typeof(TenancyAccess), nameof(TenancyAccess.ForTheWholeTenant))]
/// </code>
/// so <c>[RequiresKey(BillingKeys.CloseTheBooks)]</c> on its own is <c>TenancyAccess.ForTheWholeTenant("billing.close-the-books")</c>.
/// </summary>
/// <remarks>
/// One package says it. A project that references none that does, or two that do, cannot tell where a key without a
/// marker is held, and there such a request is DDD00080, which asks for a marker.
/// </remarks>
/// <param name="factory">The class whose static method makes the requirement: <c>typeof(TenancyAccess)</c>.</param>
/// <param name="method">The method's name: <c>nameof(TenancyAccess.ForTheWholeTenant)</c>.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class KeyWithoutMarkerAttribute(Type factory, string method) : Attribute
{
    /// <summary>The class whose static method makes the requirement.</summary>
    public Type Factory { get; } = factory;

    /// <summary>The name of the method that makes it.</summary>
    public string Method { get; } = method;

    /// <summary>
    /// The requirement attributes a key without a marker already requires, as <see cref="KeyMarkerAttribute.Implies"/>
    /// says for a marker's: Tenancy's key for the whole tenant names <c>[InTenant]</c>. Empty by default.
    /// </summary>
    public Type[] Implies { get; set; } = [];
}
