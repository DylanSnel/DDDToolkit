using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Says on a request that its caller works in the tenant the request was sent for: a seat there, or system work
/// there. What <see cref="TenancyAccess.InTenant"/> says, written where the request is declared:
/// <code>
/// [InTenant]
/// public sealed record TenantRoles : IQuery&lt;IReadOnlyList&lt;RoleListing&gt;&gt;, ITenantsRequest;
/// </code>
/// </summary>
/// <remarks>
/// The toolkit's generator turns it into <c>TenancyAccess.InTenant()</c> at compile time, in the module's request
/// interface (<c>[AccessRequests]</c>, declared <c>partial</c>), and the check that
/// <c>services.AddTenancyAccess&lt;TRequests, TContext&gt;()</c> adds holds the caller to it. A request handed to a use
/// case of the package that asks more than this, a key the use case reads where it acts, says this much and the use
/// case asks the rest. Beside a key of Tenancy's, <c>[RequiresKey]</c> with or without <c>[AtUnit]</c>, it says nothing
/// more, since only a caller in the tenant holds one, and the generator warns of it (DDD00084).
/// </remarks>
[StatesRequirement(typeof(TenancyAccess), nameof(TenancyAccess.InTenant))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class InTenantAttribute : Attribute;
