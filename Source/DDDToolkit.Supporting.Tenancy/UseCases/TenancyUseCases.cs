using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

/// <summary>
/// Tenancy's use cases, closed over the application's classes and ids. They are the one door to the rules
/// that span aggregates: who may do what, where (the caller's keys at a unit); that a role that manages access
/// is given or taken away, and its seat suspended, deactivated or reactivated, only by someone who holds its
/// keys that manage access, there and for at least as long, and never given by a seat to itself, while other
/// roles are given by whoever holds <c>tenancy.grants.manage</c> where the seat is placed; that a move gains the
/// mover nothing, and gives or takes away from anyone else no key that manages access the mover could not; and
/// that a tenant always keeps an administrator. An application closes the class once, with an alias:
/// <code>
/// global using ShopTenancy = DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases&lt;
///     ShopTenant, TenantId, ShopOrganization, ShopUnit, OrganizationUnitId, ShopSeat, SeatId, ShopRole, RoleId&gt;;
/// </code>
/// <para>
/// They are plain services, not handlers: each method checks the Tenancy caller, loads what it needs
/// through <see cref="IStore"/>, calls the aggregates, and saves once. Every command starts by asking who is
/// calling. Nobody is refused, a seat is asked for its keys, and system work in a tenant holds every key
/// there. System work outside any tenant only provisions.
/// </para>
/// <para>
/// A command that changes rights, or what they reach, first takes the tenant's access revision, before it
/// reads anything, so that two such commands committed at the same time cannot both have decided on what
/// the other changed. Placing a seat and archiving a unit take it as well: a placement widens what a seat may
/// read, and an archived unit takes no new placement or grant, so a placement or a grant that found the unit
/// active cannot commit next to the archive that ended it. Every command decides everything before it
/// changes an aggregate, so a refused command leaves nothing behind that a later save in the same unit of
/// work would write.
/// </para>
/// <para>
/// The rules that span aggregates hold for what goes through these use cases, and only for that. The
/// aggregates' public methods check the aggregate's own state; code of the application that calls
/// <c>Seat.Revoke</c> or <c>Role.Archive</c> itself skips the key checks, containment and self-appointment,
/// and the last-administrator rule, and answers for them. A closed tenant is closed to its seats, because no seat
/// is selected in it; system work begun in it is not stopped.
/// </para>
/// </summary>
public static partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
    where TTenant : TenantAggregate<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
