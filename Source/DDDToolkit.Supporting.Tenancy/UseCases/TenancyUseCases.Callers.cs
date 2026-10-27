using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

// Operator work, closed over the application's ids. TenancyWork is generic over the ids, and C# infers no id that is
// not an argument: the operator work that provisions a tenant takes none. Here it is once more, closed over the ids the
// use cases are, and so reached through the class the generator writes for the use cases: TenancyUseCases.BeginOperator.
// C# finds a static member through every class derived from the one that declares it, as it finds a nested type. The
// rest of the work that is not the caller's own, system work and a link's token, is begun on the access manager,
// TenancyAccessManager.
public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// Begins system work outside any tenant that provisions a tenant for an operator, recorded as that operator:
    /// <see cref="TenancyWork.BeginOperator{TTenantId, TSeatId}"/> closed over the application's ids.
    /// </summary>
    /// <param name="operatorIdentity">The operator's verified identity: the <c>sub</c> of the token they signed in with.</param>
    /// <exception cref="ArgumentException"><paramref name="operatorIdentity"/> is empty; nothing is begun then.</exception>
    public static IDisposable BeginOperator(Guid operatorIdentity) => TenancyWork.BeginOperator<TTenantId, TSeatId>(operatorIdentity);

    /// <summary>
    /// Begins system work inside one tenant that carries out what an operator asked for, recorded as that operator:
    /// <see cref="TenancyWork.BeginOperatorIn{TTenantId, TSeatId}"/> closed over the application's ids.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="operatorIdentity">The operator's verified identity: the <c>sub</c> of the token they signed in with.</param>
    /// <param name="scope">The scope of the toolkit's scoped system caller.</param>
    /// <exception cref="ArgumentException"><paramref name="operatorIdentity"/> is empty, or <paramref name="scope"/> is not a scope; nothing is begun then.</exception>
    public static IDisposable BeginOperatorIn(TTenantId tenant, Guid operatorIdentity, string scope = TenancyWork.SystemScope)
        => TenancyWork.BeginOperatorIn<TTenantId, TSeatId>(tenant, operatorIdentity, scope);
}
