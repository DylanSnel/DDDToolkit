using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// A person's seat in one tenant: the verified identity it belongs to, the units it is placed in, and the
/// roles it holds at each. The application declares its own class with
/// <see cref="SeatAggregateAttribute{TSeatId}"/>.
/// <para>
/// The identity is the subject of a verified token, set when the seat is made and never changed. A seat is
/// never linked to a person by an e-mail address, and none is kept here; an application that wants contact
/// details adds them to its own class.
/// </para>
/// <para>
/// Nor does a seat have a name. No rule of Tenancy reads one, so what a person is shown by is the application's
/// to say: a name per tenant on its own seat class, set in the callback of the use case that makes the seat; the
/// person's own name, from the identity provider; or a profile of its own, found by <see cref="Identity"/>. The
/// directory answers the application's own seat, whole, and the application selects what a screen shows of it.
/// </para>
/// <para>
/// Placements and grants are the seat's own: it checks every change to them, and hands them out as
/// read-only views. What a role grants is not the seat's to know; the use case reads the role and passes its
/// <see cref="RoleFacts"/> in.
/// </para>
/// <para>
/// Every method that changes the seat takes who makes the change, <c>by</c>, and puts it on the event it
/// raises; the use cases pass the actor of their caller. It is a record of the change and decides nothing: the
/// rules about a seat acting on itself read the seat a placement or a grant keeps, <c>placedBy</c> and
/// <c>grantedBy</c>.
/// </para>
/// </summary>
/// <typeparam name="TSeatId">The application's seat id.</typeparam>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
/// <typeparam name="TUnitId">The application's unit id.</typeparam>
/// <typeparam name="TRoleId">The application's role id.</typeparam>
[AggregateRootBase]
public abstract partial class SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The longest reason a grant may give.</summary>
    public const int MaxReasonLength = 500;

    // Written by hand rather than as a generated collection: a placement is not a toolkit entity.
    private readonly List<Placement<TSeatId, TUnitId, TRoleId>> _placements = [];

    /// <summary>The tenant the seat is in.</summary>
    public TTenantId TenantId { get; private set; }

    /// <summary>The verified identity the seat belongs to: the subject of its token. Never changes.</summary>
    public Guid Identity { get; private set; }

    /// <summary>Whether the seat's grants count.</summary>
    public SeatStatus Status { get; private set; }

    /// <summary>The units the seat is placed in, with the roles it holds at each. A read-only view.</summary>
    public IReadOnlyList<Placement<TSeatId, TUnitId, TRoleId>> Placements => _placements.AsReadOnly();

    /// <summary>
    /// What a constructor would do: gives a new instance its id, tenant and identity, starts it active, and raises
    /// <see cref="SeatAdded{TTenantId, TSeatId}"/>. Called once, by <see cref="TenancyInstances"/>, right after the
    /// instance is made.
    /// </summary>
    internal void InitializeNew(TSeatId id, TTenantId tenantId, Guid identity, TenancyActor<TSeatId>? by)
    {
        if (identity == Guid.Empty)
        {
            throw TenancyRefusals.Of(TenancyRefusals.IdentityRequired);
        }

        Id = id;
        TenantId = tenantId;
        Identity = identity;
        Status = SeatStatus.Active;

        RaiseDomainEvent(new SeatAdded<TTenantId, TSeatId>(tenantId, id, by));
    }

    /// <summary>Stops an active seat for now. Its placements and grants stay, and give it nothing while it is suspended.</summary>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.seat-state</c>: it is not active.</exception>
    public void Suspend(TenancyActor<TSeatId>? by = null)
    {
        RequireStatus("suspend", SeatStatus.Active);

        Status = SeatStatus.Suspended;
        RaiseDomainEvent(new SeatSuspended<TTenantId, TSeatId>(TenantId, Id, by));
    }

    /// <summary>Makes a suspended seat active again.</summary>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.seat-state</c>: it is not suspended.</exception>
    public void Reactivate(TenancyActor<TSeatId>? by = null)
    {
        RequireStatus("reactivate", SeatStatus.Suspended);

        Status = SeatStatus.Active;
        RaiseDomainEvent(new SeatReactivated<TTenantId, TSeatId>(TenantId, Id, by));
    }

    /// <summary>Stops an active or suspended seat for good.</summary>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.seat-state</c>: it is already deactivated.</exception>
    public void Deactivate(TenancyActor<TSeatId>? by = null)
    {
        RequireStatus("deactivate", SeatStatus.Active, SeatStatus.Suspended);

        Status = SeatStatus.Deactivated;
        RaiseDomainEvent(new SeatDeactivated<TTenantId, TSeatId>(TenantId, Id, by));
    }

    /// <summary>
    /// Places the seat in a unit. Whether the unit is an active unit of the organization is the use case's to
    /// check: the organization is another aggregate.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <param name="primary">Whether this becomes the seat's primary placement.</param>
    /// <param name="at">When.</param>
    /// <param name="placedBy">The seat placing it, or <see langword="null"/> for system work with no seat acting.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException">
    /// <c>tenancy.seat-state</c> when the seat is not active, <c>tenancy.self-assignment</c> when it would place
    /// itself, <c>tenancy.duplicate-placement</c>, <c>tenancy.second-primary</c>.
    /// </exception>
    public Placement<TSeatId, TUnitId, TRoleId> Place(TUnitId unit, bool primary, DateTimeOffset at, TSeatId? placedBy, TenancyActor<TSeatId>? by = null)
    {
        RequireStatus("place", SeatStatus.Active);

        if (placedBy is { } placing && placing.Equals(Id))
        {
            throw TenancyRefusals.Of(TenancyRefusals.SelfAssignment);
        }

        if (FindPlacement(unit) is not null)
        {
            throw TenancyRefusals.Of(TenancyRefusals.DuplicatePlacement);
        }

        if (primary && _placements.Any(placement => placement.IsPrimary))
        {
            throw TenancyRefusals.Of(TenancyRefusals.SecondPrimary);
        }

        var added = new Placement<TSeatId, TUnitId, TRoleId>(unit, primary, at, placedBy);
        _placements.Add(added);

        RaiseDomainEvent(new SeatPlaced<TTenantId, TSeatId, TUnitId>(TenantId, Id, unit, primary, by));
        return added;
    }

    /// <summary>
    /// Withdraws the seat from a unit: every role it holds there is revoked first, each with its own event,
    /// and then the placement goes.
    /// </summary>
    /// <param name="unit">The unit of the placement.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.placement-not-found</c>.</exception>
    public void Withdraw(TUnitId unit, TenancyActor<TSeatId>? by = null)
    {
        var placement = RequirePlacement(unit);

        foreach (var roleId in placement.Grants.Select(grant => grant.RoleId).ToArray())
        {
            placement.RemoveGrant(roleId);
            RaiseDomainEvent(new OrganizationRoleRevoked<TTenantId, TSeatId, TUnitId, TRoleId>(TenantId, Id, unit, roleId, by));
        }

        _placements.Remove(placement);
        RaiseDomainEvent(new SeatWithdrawn<TTenantId, TSeatId, TUnitId>(TenantId, Id, unit, by));
    }

    /// <summary>Makes the placement in <paramref name="unit"/> the seat's primary one. Already primary changes nothing and raises nothing.</summary>
    /// <param name="unit">The unit of the placement.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.placement-not-found</c>.</exception>
    public void MakePrimary(TUnitId unit, TenancyActor<TSeatId>? by = null)
    {
        var placement = RequirePlacement(unit);
        if (placement.IsPrimary)
        {
            return;
        }

        foreach (var other in _placements)
        {
            if (other.IsPrimary)
            {
                other.SetPrimary(false);
            }
        }

        placement.SetPrimary(true);
        RaiseDomainEvent(new PrimaryPlacementChanged<TTenantId, TSeatId, TUnitId>(TenantId, Id, unit, by));
    }

    /// <summary>
    /// Grants the seat a role at a unit where it is placed. Whether the granting seat may give the role (its
    /// keys that manage access, held there long enough, and never a role that manages access to itself) is the
    /// use case's to check, because it needs the catalogue and another seat's rights. A host that calls
    /// <see cref="Grant"/> directly gets neither. A seat may be recorded as its own granter.
    /// </summary>
    /// <param name="unit">The unit of one of the seat's placements.</param>
    /// <param name="role">The role.</param>
    /// <param name="facts">What the use case read about the role.</param>
    /// <param name="period">When the grant applies.</param>
    /// <param name="grantedBy">The seat granting it, or <see langword="null"/> for system work with no seat acting.</param>
    /// <param name="reason">Why, at most <see cref="MaxReasonLength"/> characters, or <see langword="null"/>.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException">
    /// <c>tenancy.seat-state</c> when the seat is not active, <c>tenancy.placement-not-found</c>,
    /// <c>tenancy.role-not-active</c>, <c>tenancy.duplicate-grant</c>, <c>tenancy.name-invalid</c> for a reason
    /// that is too long.
    /// </exception>
    public void Grant(TUnitId unit, TRoleId role, RoleFacts facts, GrantPeriod period, TSeatId? grantedBy, string? reason, TenancyActor<TSeatId>? by = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        RequireStatus("grant", SeatStatus.Active);

        var placement = RequirePlacement(unit);
        if (!facts.IsActive)
        {
            throw TenancyRefusals.Of(TenancyRefusals.RoleNotActive);
        }

        if (placement.FindGrant(role) is not null)
        {
            throw TenancyRefusals.Of(TenancyRefusals.DuplicateGrant);
        }

        var why = TenancyNames.Optional(reason, TenancyNames.ReasonToken, MaxReasonLength);

        placement.AddGrant(role, period, grantedBy, why.Length == 0 ? null : why);
        RaiseDomainEvent(new OrganizationRoleGranted<TTenantId, TSeatId, TUnitId, TRoleId>(
            TenantId, Id, unit, role, period.Starts, period.Ends, grantedBy, by));
    }

    /// <summary>Revokes a role the seat holds at a unit.</summary>
    /// <param name="unit">The unit of the placement.</param>
    /// <param name="role">The role.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.grant-not-found</c>: the seat does not hold the role there.</exception>
    public void Revoke(TUnitId unit, TRoleId role, TenancyActor<TSeatId>? by = null)
    {
        var placement = FindPlacement(unit);
        if (placement is null || !placement.RemoveGrant(role))
        {
            throw TenancyRefusals.Of(TenancyRefusals.GrantNotFound);
        }

        RaiseDomainEvent(new OrganizationRoleRevoked<TTenantId, TSeatId, TUnitId, TRoleId>(TenantId, Id, unit, role, by));
    }

    /// <summary>
    /// Whether this seat, as it is in memory, is an administrator of its tenant: it is active, placed at the
    /// root, and holds there, with no end date and applying now, an active role that grants
    /// <see cref="TenancyKeys.AdministratorKey"/>. A grant with an end date does not count: counting it would
    /// let the tenant lose its last administrator on a date nobody picked for that.
    /// </summary>
    /// <param name="root">The organization's root.</param>
    /// <param name="roles">What is known about each role the seat holds, or <see langword="null"/> for a role that is not found.</param>
    /// <param name="now">The moment to ask about.</param>
    public bool IsAdministratorAt(TUnitId root, Func<TRoleId, RoleFacts?> roles, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(roles);

        if (Status != SeatStatus.Active || FindPlacement(root) is not { } placement)
        {
            return false;
        }

        foreach (var grant in placement.Grants)
        {
            if (grant.EndsAt is null
                && grant.AppliesAt(now)
                && roles(grant.RoleId) is { IsActive: true } facts
                && facts.Keys.Contains(TenancyKeys.AdministratorKey, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private Placement<TSeatId, TUnitId, TRoleId>? FindPlacement(TUnitId unit)
        => _placements.FirstOrDefault(placement => placement.UnitId.Equals(unit));

    private Placement<TSeatId, TUnitId, TRoleId> RequirePlacement(TUnitId unit)
        => FindPlacement(unit) ?? throw TenancyRefusals.Of(TenancyRefusals.PlacementNotFound);

    private void RequireStatus(string action, params ReadOnlySpan<SeatStatus> allowed)
    {
        foreach (var status in allowed)
        {
            if (Status == status)
            {
                return;
            }
        }

        throw TenancyRefusals.Of(TenancyRefusals.SeatState, ("Status", Status.ToString().ToLowerInvariant()), ("Action", action));
    }
}
