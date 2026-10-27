using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Marks the member of a request marked <see cref="OperatorAttribute"/> that names the tenant the request is about:
/// <code>
/// [Operator]
/// public sealed record SuspendTenant([ForTenant] TenantId Tenant, string Reason) : ICommand&lt;Tenant&gt;, ITenancyRequest;
/// </code>
/// is <c>TenancyAccess.Operator(request.Tenant)</c>. On a command, the operator work acts in that tenant.
/// </summary>
/// <remarks>
/// A request that names another tenant beside the command, as tenant selection reads it, is refused with
/// <c>tenancy.tenant-mismatch</c>. A request that names none is let through.
/// <para>
/// It goes on a positional parameter of a record as it is, or on a property. The member is the application's tenant id,
/// the id its class declared with <c>[TenantAggregate&lt;TTenantId&gt;]</c> is declared over. On another id it is
/// DDD00081. Without <see cref="OperatorAttribute"/> on the request it is DDD00102, and beside
/// <c>[Operator(Tenant = OperatorTenant.FromRequest)]</c> it is DDD00103.
/// </para>
/// </remarks>
[RequirementArgument(typeof(OperatorAttribute), IdOf = typeof(TenantAggregateAttribute<>))]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
public sealed class ForTenantAttribute : Attribute;
