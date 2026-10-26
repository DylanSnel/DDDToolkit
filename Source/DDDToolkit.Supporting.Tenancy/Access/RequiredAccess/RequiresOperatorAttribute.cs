using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Says on a query that its caller is one of the application's operators, and nobody else is: no seat, whatever it
/// holds, and no system work. What <see cref="TenancyAccess.RequiresOperator"/> says, written where the query is
/// declared:
/// <code>
/// [RequiresOperator]
/// public sealed record AllTenants : IQuery&lt;IReadOnlyList&lt;TenantListing&gt;&gt;, ITenantsRequest;
/// </code>
/// </summary>
/// <remarks>
/// For queries only: an operator changes nothing itself, and what an operator asks for is carried out by system work
/// that names the operator, <c>TenancyUseCases.BeginOperator(identity)</c>. On a message of the Mediator library that
/// is no query, a command or a plain request, it is DDD00082. The toolkit's generator turns it into <c>TenancyAccess.RequiresOperator()</c> at compile time, in the
/// module's request interface (<c>[AccessRequests]</c>, declared <c>partial</c>). For the same reason it is no module's
/// default on that interface, which a command that says nothing would take as well (DDD00089).
/// </remarks>
[StatesRequirement(typeof(TenancyAccess), nameof(TenancyAccess.RequiresOperator), QueriesOnly = true, Implies = [typeof(SignedInAttribute)])]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class RequiresOperatorAttribute : Attribute;
