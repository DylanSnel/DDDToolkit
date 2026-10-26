using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// A tenant: the slug callers select it by, its status, and whether its organization is flat or
/// a tree. The application declares its own class with <see cref="TenantAggregateAttribute{TTenantId}"/>, and
/// the generator derives that class from this one, closed over the application's id.
/// <para>
/// Its status moves one way: provisioned, active, suspended and active again as often as needed, and closed
/// once. A suspension and a closure each need a reason, and the last one is kept.
/// </para>
/// <para>
/// Nothing here is virtual. A class declared with the template adds fields, methods, entities and rules of
/// its own, and reacts to this tenant's domain events; it does not change what the package decides.
/// </para>
/// <para>
/// Every method that changes the tenant takes who makes the change, <c>by</c>, and puts it on the event it
/// raises. The use cases pass the actor of their caller. A tenant is declared without the seat class and does
/// not know the seat id's type, so the methods take it: it follows from a caller's actor
/// (<c>tenant.Suspend(reason, caller.Actor)</c>), and code that names nobody, an import or a test, names the
/// type instead (<c>tenant.Activate&lt;SeatId&gt;()</c>).
/// </para>
/// </summary>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
[AggregateRootBase]
public abstract partial class TenantAggregate<TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
{
    /// <summary>The longest reason a suspension or closure may give.</summary>
    public const int MaxReasonLength = 500;

    /// <summary>The slug the tenant is selected by. Set when it is provisioned, and never changed.</summary>
    public TenantSlug Slug { get; private set; } = null!;

    /// <summary>Where the tenant is in its life.</summary>
    public TenantStatus Status { get; private set; }

    /// <summary>Whether its organization is its root alone or a tree.</summary>
    public TenantShape Shape { get; private set; }

    /// <summary>The reason given by the last suspension or closure, or <see langword="null"/> when there was none.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>Whether the tenant is in use.</summary>
    public bool IsActive => Status == TenantStatus.Active;

    /// <summary>
    /// What a constructor would do: gives a new instance its id, slug and shape, starts it in
    /// <see cref="TenantStatus.Provisioning"/>, and raises <see cref="TenantProvisioned{TTenantId, TSeatId}"/>.
    /// Called once, by <see cref="TenancyInstances"/>, right after the instance is made.
    /// </summary>
    internal void InitializeNew<TSeatId>(TTenantId id, ValidTenantSlug slug, TenantShape shape, TenancyActor<TSeatId>? by)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        ArgumentNullException.ThrowIfNull(slug);
        if (!Enum.IsDefined(shape))
        {
            throw new ArgumentOutOfRangeException(nameof(shape), shape, "A tenant is flat or hierarchical.");
        }

        Id = id;
        Slug = slug;
        Shape = shape;
        Status = TenantStatus.Provisioning;

        RaiseDomainEvent(new TenantProvisioned<TTenantId, TSeatId>(id, slug.Value, shape, by));
    }

    /// <summary>Puts a provisioned tenant in use.</summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: a tenant does not know it by itself.</typeparam>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.tenant-state</c>: it is not being provisioned.</exception>
    public void Activate<TSeatId>(TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        RequireStatus("activate", TenantStatus.Provisioning);

        Status = TenantStatus.Active;
        RaiseDomainEvent(new TenantActivated<TTenantId, TSeatId>(Id, by));
    }

    /// <summary>Stops an active tenant for now.</summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: a tenant does not know it by itself.</typeparam>
    /// <param name="reason">Why, kept as <see cref="StatusReason"/>. Required.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException">
    /// <c>tenancy.tenant-state</c> when it is not active, <c>tenancy.reason-required</c> without a reason,
    /// <c>tenancy.name-invalid</c> when the reason is longer than <see cref="MaxReasonLength"/>.
    /// </exception>
    public void Suspend<TSeatId>(string reason, TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        RequireStatus("suspend", TenantStatus.Active);
        var why = Reason(reason);

        Status = TenantStatus.Suspended;
        StatusReason = why;
        RaiseDomainEvent(new TenantSuspended<TTenantId, TSeatId>(Id, why, by));
    }

    /// <summary>Puts a suspended tenant back in use. The reason it was suspended stays as the last one given.</summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: a tenant does not know it by itself.</typeparam>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.tenant-state</c>: it is not suspended.</exception>
    public void Reactivate<TSeatId>(TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        RequireStatus("reactivate", TenantStatus.Suspended);

        Status = TenantStatus.Active;
        RaiseDomainEvent(new TenantReactivated<TTenantId, TSeatId>(Id, by));
    }

    /// <summary>Stops an active or suspended tenant for good. A tenant still being provisioned is not closed; it was never open.</summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: a tenant does not know it by itself.</typeparam>
    /// <param name="reason">Why, kept as <see cref="StatusReason"/>. Required.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException">
    /// <c>tenancy.tenant-state</c> when it is being provisioned or already closed, <c>tenancy.reason-required</c>
    /// without a reason, <c>tenancy.name-invalid</c> when the reason is too long.
    /// </exception>
    public void Close<TSeatId>(string reason, TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        RequireStatus("close", TenantStatus.Active, TenantStatus.Suspended);
        var why = Reason(reason);

        Status = TenantStatus.Closed;
        StatusReason = why;
        RaiseDomainEvent(new TenantClosed<TTenantId, TSeatId>(Id, why, by));
    }

    /// <summary>
    /// Changes a flat tenant into a hierarchical one. The way back is refused: the units below the root would
    /// have nowhere to go.
    /// </summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: a tenant does not know it by itself.</typeparam>
    /// <param name="to">The new shape.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException">
    /// <c>tenancy.tenant-state</c> when the tenant is closed, <c>tenancy.shape-change</c> for anything but flat
    /// to hierarchical.
    /// </exception>
    public void ChangeShape<TSeatId>(TenantShape to, TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        RequireShapeChange(to);

        var from = Shape;
        Shape = to;
        RaiseDomainEvent(new TenantShapeChanged<TTenantId, TSeatId>(Id, from, to, by));
    }

    /// <summary>Refuses what <see cref="ChangeShape{TSeatId}"/> refuses, and changes nothing: a use case checks this before it prepares the change.</summary>
    internal void RequireShapeChange(TenantShape to)
    {
        if (Status == TenantStatus.Closed)
        {
            throw StateRefusal("change-shape");
        }

        if (Shape != TenantShape.Flat || to != TenantShape.Hierarchical)
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.ShapeChange);
        }
    }

    private void RequireStatus(string action, params ReadOnlySpan<TenantStatus> allowed)
    {
        foreach (var status in allowed)
        {
            if (Status == status)
            {
                return;
            }
        }

        throw StateRefusal(action);
    }

    private RefusalException StateRefusal(string action)
        => TenancyRefusals.Refuse(TenancyRefusals.TenantState, ("Status", Status.ToString().ToLowerInvariant()), ("Action", action));

    private static string Reason(string? reason)
    {
        var why = reason?.Trim() ?? string.Empty;
        if (why.Length == 0)
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.ReasonRequired);
        }

        return TenancyNames.Optional(why, TenancyNames.ReasonToken, MaxReasonLength);
    }
}
