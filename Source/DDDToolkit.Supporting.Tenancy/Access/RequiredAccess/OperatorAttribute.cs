using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Says on a request that its caller is one of the application's operators, and nobody else is: no seat, whatever it
/// holds, and no system work. On a command, the handler runs as operator work, recorded as the operator. What
/// <see cref="TenancyAccess.Operator(OperatorTenant, string)"/> says, written where the request is declared:
/// <code>
/// [Operator]
/// public sealed record SuspendTenant([ForTenant] TenantId Tenant, string Reason) : ICommand&lt;Tenant&gt;, ITenancyRequest;
///
/// [Operator]
/// public sealed record AllTenants(string? After) : IQuery&lt;TenantPage&gt;, ITenancyRequest;
/// </code>
/// </summary>
/// <remarks>
/// The access check refuses every other caller with <c>tenancy.operators-only</c> before the handler runs. An operator is
/// a signed-in user whose token carries one of <c>TenancyOptions.OperatorTokenRoles</c>.
/// <para>
/// On a command, the pipeline runs the handler as operator work. The work is system work, recorded as the operator. The
/// operator's identity is the current user's id, never an id a caller sends. With a <see cref="ForTenantAttribute"/> member, the work acts in the
/// tenant the command names, and a request that names another tenant is refused with <c>tenancy.tenant-mismatch</c>.
/// With <c>Tenant = OperatorTenant.FromRequest</c>, it acts in the tenant the request names beside the command, as tenant
/// selection reads it. With neither, it acts in no tenant, which is what provisioning needs, and a tenant the request
/// names is ignored. <see cref="Scope"/> is the scope the work runs in.
/// </para>
/// <para>
/// The whole handler holds every permission in that tenant. Keep an operator's handler to the operator's act.
/// </para>
/// <para>
/// On a query nothing begins. An operator reads across tenants through the database role of its token. A message
/// answered with a stream begins nothing either.
/// </para>
/// <para>
/// The toolkit's generator turns it into <c>TenancyAccess.Operator(...)</c> at compile time, in the module's request
/// interface (<c>[AccessRequests]</c>, declared <c>partial</c>). Beside it, <c>[InTenant]</c> and <c>[RequiresPermission]</c>
/// refuse every operator (DDD00104). <c>FromRequest</c> beside a <c>[ForTenant]</c> member names two tenants (DDD00103).
/// <c>Tenant</c> or <c>Scope</c> on a query says nothing (DDD00105).
/// </para>
/// </remarks>
[StatesRequirement(typeof(TenancyAccess), nameof(TenancyAccess.Operator), Implies = [typeof(SignedInAttribute)])]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class OperatorAttribute : Attribute
{
    /// <summary>
    /// Where a command's operator work acts. <see cref="OperatorTenant.FromCommand"/>, the default, is the tenant the command
    /// names with a <see cref="ForTenantAttribute"/> member, or no tenant where it names none.
    /// <see cref="OperatorTenant.FromRequest"/> is the tenant the request names beside the command.
    /// </summary>
    public OperatorTenant Tenant { get; set; }

    /// <summary>
    /// The scope a command's operator work runs in. It is Tenancy's own, <c>tenancy</c>, by default. A module's own command
    /// names its module's scope, <c>projects</c> say. A scope has lower case letters, digits, <c>_</c> and <c>-</c>.
    /// </summary>
    public string Scope { get; set; } = TenancyWork.SystemScope;
}
