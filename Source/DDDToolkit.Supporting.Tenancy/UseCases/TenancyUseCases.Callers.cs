using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

// System work and the current caller, closed over the application's ids. TenancyWork and TenancyCallers are generic
// over the ids, and C# infers no id that is not an argument: the system work that provisions a tenant takes none.
// Here they are once more, closed over the ids the use cases are, and so reached through the class the generator
// writes for the use cases: TenancyUseCases.BeginSystem(). C# finds a static member through every class derived
// from the one that declares it, as it finds a nested type, so every project that sees that class calls these
// without naming an id. The project that declares the classes does too: a call is bound by the compiler, which sees
// every generator's output; only another generator, reading a signature or an attribute, would not see the class.
// Each one does what its counterpart in TenancyWork or TenancyCallers does, which stays for code that sees only the
// ids, such as another module's.
public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// Begins system work outside any tenant, for provisioning a tenant:
    /// <see cref="TenancyWork.BeginSystem{TTenantId, TSeatId}"/> closed over the application's ids. Ends when the
    /// result is disposed.
    /// </summary>
    public static IDisposable BeginSystem() => TenancyWork.BeginSystem<TTenantId, TSeatId>();

    /// <summary>
    /// Begins system work inside one tenant, such as an import, seeding or an operator's command: it holds every key
    /// there, and nothing in any other tenant. <see cref="TenancyWork.BeginSystemIn{TTenantId, TSeatId}(TTenantId, TSeatId?, string)"/>
    /// closed over the application's ids. Ends when the result is disposed.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="actingSeat">The seat the work is done for, recorded as who placed or granted; it adds no rights.</param>
    /// <param name="scope">The scope of the toolkit's scoped system caller: Tenancy's own unless a module's own work passes its name.</param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope; nothing is begun then.</exception>
    public static IDisposable BeginSystemIn(TTenantId tenant, TSeatId? actingSeat = null, string scope = TenancyWork.SystemScope)
        => TenancyWork.BeginSystemIn<TTenantId, TSeatId>(tenant, actingSeat, scope);

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

    /// <summary>
    /// Begins system work inside one tenant that answers a link carrying a token a seat made, recorded as the token
    /// and that seat: <see cref="TenancyWork.BeginTokenIn{TTenantId, TSeatId}"/> closed over the application's ids.
    /// Begin it only once the token was checked and found to be that seat's, and the seat to be in use.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="seat">The seat the token stands for.</param>
    /// <param name="scope">The scope of the toolkit's scoped system caller, the module's name.</param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope; nothing is begun then.</exception>
    public static IDisposable BeginTokenIn(TTenantId tenant, TSeatId seat, string scope)
        => TenancyWork.BeginTokenIn<TTenantId, TSeatId>(tenant, seat, scope);

    /// <summary>
    /// The current Tenancy caller with the application's ids; nobody, refused as not seated, outside any scope:
    /// <see cref="TenancyCallers.Current{TTenantId, TSeatId}"/> closed over them.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current caller was made with other id types: two Tenancy registrations with different ids.</exception>
    public static TenancyCaller<TTenantId, TSeatId> CurrentCaller() => TenancyCallers.Current<TTenantId, TSeatId>();
}
