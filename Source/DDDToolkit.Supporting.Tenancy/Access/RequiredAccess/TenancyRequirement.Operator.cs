namespace DDDToolkit.Supporting.Tenancy.Access;

public abstract partial record TenancyRequirement
{
    /// <summary>
    /// The caller is one of the application's operators: a signed-in user whose token carries one of
    /// <see cref="TenancyOptions{TTenantId, TSeatId, TUnitId, TRoleId}.OperatorTokenRoles"/>. Anyone else is
    /// refused with <c>tenancy.operators-only</c>, a seat and system work included. A request declares it with
    /// <see cref="TenancyAccess.RequiresOperator"/>.
    /// </summary>
    /// <remarks>
    /// An operator works in no tenant and holds no key, so nothing is asked about either: the request names the
    /// tenant it reads. It is the toolkit's own caller that is asked, never the Tenancy caller, which is nobody
    /// for an operator in every tenant. For queries only; an operator changes nothing itself.
    /// </remarks>
    public sealed record Operator : TenancyRequirement;
}
