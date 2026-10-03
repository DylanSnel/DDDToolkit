using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// The only ways to system power in Tenancy. A request is never system work, whatever the toolkit's own
/// caller says: a person reaches Tenancy through a seat, and system work is begun here, on purpose.
/// <code>
/// using (TenancyWork.BeginSystemIn&lt;TenantId, SeatId&gt;(tenant, actingSeat))
/// {
///     await seats.PlaceAsync(seat, unit, primary: true, cancellationToken);
/// }
/// </code>
/// <para>
/// Each method begins two callers, and ends both when its result is disposed: the Tenancy caller, which says
/// in which tenant the work acts, and the toolkit's scoped system caller, <c>Caller.SystemIn(scope)</c>, which
/// says what kind of work it is. Keeping them in one scope keeps them from drifting apart. Neither begins the
/// toolkit's <c>Caller.System</c>: that one stands for the application itself, which row level security lets
/// past every policy, while system work in a tenant must stay inside that tenant. On Postgres the scoped
/// caller runs as a role that cannot bypass the policies, and the tenant reaches them next to it; Tenancy's
/// own policies come with <c>DDDToolkit.Supporting.Tenancy.Postgres</c>.
/// </para>
/// <para>
/// Each also says who the work is recorded as (<see cref="TenancyCaller{TTenantId, TSeatId}.Actor"/>): the system
/// in its scope, unless the work is carried out for an operator (<see cref="BeginOperator"/>,
/// <see cref="BeginOperatorIn"/>) or answers a link that carries a seat's token (<see cref="BeginTokenIn"/>).
/// These are the one way an operator's act is named: an operator holds no seat and changes nothing as itself,
/// so what it asked for is done by system work that names it.
/// </para>
/// </summary>
public static class TenancyWork
{
    /// <summary>
    /// The scope of the toolkit's scoped system caller for Tenancy's own work. Under the policies of
    /// <c>DDDToolkit.Supporting.Tenancy.Postgres</c>, only work in this scope writes Tenancy's tables; work in
    /// another scope may read them.
    /// </summary>
    public const string SystemScope = "tenancy";

    /// <summary>
    /// Begins system work outside any tenant, for provisioning a tenant. It reads nothing inside one: every
    /// tenant filter matches nothing for it. The toolkit's caller is the scoped system caller in
    /// <see cref="SystemScope"/>, which under the policies of <c>DDDToolkit.Supporting.Tenancy.Postgres</c>
    /// reads and writes nothing until provisioning narrows it to the tenant it makes.
    /// </summary>
    public static IDisposable BeginSystem<TTenantId, TSeatId>()
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        => Both(Caller.SystemIn(SystemScope), TenancyCaller<TTenantId, TSeatId>.System);

    /// <summary>
    /// Begins system work inside one tenant, such as an import, seeding or an operator's command: it holds
    /// every key there, and nothing in any other tenant. The last-administrator rule still applies to it.
    /// <para>
    /// Work that calls Tenancy's use cases keeps the default scope. A module's own system work in a tenant,
    /// that only reads Tenancy's rows next to its own, passes the module's name instead, and then cannot write
    /// Tenancy's tables under the policies of <c>DDDToolkit.Supporting.Tenancy.Postgres</c>.
    /// </para>
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="actingSeat">The seat the work is done for, recorded as who placed or granted; it adds no rights.</param>
    /// <param name="scope">The scope of the toolkit's scoped system caller: lower case letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope; nothing is begun then.</exception>
    public static IDisposable BeginSystemIn<TTenantId, TSeatId>(TTenantId tenant, TSeatId? actingSeat = null, string scope = SystemScope)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        => Both(Caller.SystemIn(scope), TenancyCaller<TTenantId, TSeatId>.SystemIn(tenant, TenancyActor<TSeatId>.OfSystem(scope, actingSeat)));

    /// <summary>
    /// Begins system work outside any tenant that provisions a tenant for an operator: the callers of
    /// <see cref="BeginSystem"/>, recorded as the operator rather than as the system. Provisioning then makes the
    /// tenant as that operator's act.
    /// </summary>
    /// <param name="operatorIdentity">The operator's verified identity: the <c>sub</c> of the token they signed in with.</param>
    /// <exception cref="ArgumentException"><paramref name="operatorIdentity"/> is empty; nothing is begun then.</exception>
    public static IDisposable BeginOperator<TTenantId, TSeatId>(Guid operatorIdentity)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        => Both(
            Caller.SystemIn(SystemScope),
            TenancyCaller<TTenantId, TSeatId>.SystemBy(TenancyActor<TSeatId>.OfOperator(operatorIdentity, SystemScope)));

    /// <summary>
    /// Begins system work inside one tenant that carries out what an operator asked for, such as suspending the
    /// tenant: the callers of <see cref="BeginSystemIn"/>, recorded as the operator rather than as the system.
    /// The operator gets nothing by it: the work holds what system work in the tenant holds, and the operator is
    /// only who it is recorded as.
    /// <para>
    /// The identity is the verified one of the request that asked, never one a caller sends: whoever begins this
    /// took it from the token, or from a record written by the request that did.
    /// </para>
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="operatorIdentity">The operator's verified identity: the <c>sub</c> of the token they signed in with.</param>
    /// <param name="scope">The scope of the toolkit's scoped system caller: lower case letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="operatorIdentity"/> is empty, or <paramref name="scope"/> is not a scope; nothing is begun then.</exception>
    public static IDisposable BeginOperatorIn<TTenantId, TSeatId>(TTenantId tenant, Guid operatorIdentity, string scope = SystemScope)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        => Both(
            Caller.SystemIn(scope),
            TenancyCaller<TTenantId, TSeatId>.SystemIn(tenant, TenancyActor<TSeatId>.OfOperator(operatorIdentity, scope)));

    /// <summary>
    /// Begins system work inside one tenant that answers a link carrying a token a seat made, such as a feed only
    /// its owner knows the address of: the callers of <see cref="BeginSystemIn"/> in the module's own scope,
    /// recorded as the token and the seat it stands for.
    /// <para>
    /// It is system work, not the seat: it holds what system work in the tenant holds, so the module that begins it
    /// narrows what it reads to that seat itself. Begin it only once the token was checked and found to be that
    /// seat's, and the seat to be in use.
    /// </para>
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="seat">The seat the token stands for.</param>
    /// <param name="scope">The scope of the toolkit's scoped system caller, the module's name: lower case letters, digits, <c>_</c> and <c>-</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is not a scope; nothing is begun then.</exception>
    public static IDisposable BeginTokenIn<TTenantId, TSeatId>(TTenantId tenant, TSeatId seat, string scope)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        => Both(Caller.SystemIn(scope), TenancyCaller<TTenantId, TSeatId>.SystemIn(tenant, TenancyActor<TSeatId>.OfToken(seat, scope)));

    /// <summary>
    /// Begins system work inside <paramref name="tenant"/> in Tenancy's own scope, recorded as
    /// <paramref name="actor"/>: how provisioning goes on inside the tenant it makes, as whoever began it.
    /// </summary>
    internal static IDisposable BeginSystemInAs<TTenantId, TSeatId>(TTenantId tenant, TenancyActor<TSeatId> actor)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        => Both(Caller.SystemIn(SystemScope), TenancyCaller<TTenantId, TSeatId>.SystemIn(tenant, actor));

    /// <summary>Begins the toolkit's caller and then the Tenancy caller, and ends them in the opposite order.</summary>
    private static IDisposable Both(Caller core, ITenancyCaller tenancy)
    {
        var outer = Callers.Begin(core);
        return new Scopes(outer, TenancyCallers.Begin(tenancy));
    }

    /// <summary>Ends the Tenancy caller and then the toolkit's, once.</summary>
    private sealed class Scopes(IDisposable outer, IDisposable inner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            inner.Dispose();
            outer.Dispose();
        }
    }
}
