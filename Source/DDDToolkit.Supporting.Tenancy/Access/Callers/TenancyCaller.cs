using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Who is asking Tenancy, with the application's ids. Tenancy never takes this from what a caller sends: a
/// seat is found through the caller's verified identity, and system work is begun on purpose with
/// <see cref="TenancyWork"/>.
/// </summary>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
/// <typeparam name="TSeatId">The application's seat id.</typeparam>
public sealed record TenancyCaller<TTenantId, TSeatId> : ITenancyCaller
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
{
    private TenancyCaller(TenancyCallerKind kind, TTenantId? tenant, TSeatId? seat, string? refusal, TenancyActor<TSeatId>? actor)
    {
        Kind = kind;
        Tenant = tenant;
        Seat = seat;
        Refusal = refusal;
        Actor = actor;
    }

    /// <summary>System work outside any tenant: provisioning only. Recorded as the system, in Tenancy's own scope.</summary>
    public static TenancyCaller<TTenantId, TSeatId> System { get; }
        = new(TenancyCallerKind.System, null, null, null, TenancyActor<TSeatId>.OfSystem(TenancyWork.SystemScope));

    /// <inheritdoc />
    public TenancyCallerKind Kind { get; }

    /// <summary>The tenant a seat or system work in a tenant acts in.</summary>
    public TTenantId? Tenant { get; }

    /// <summary>
    /// The caller's seat; for system work in a tenant, the seat it acts for, if any, which is only recorded
    /// as who placed or granted something and gives no rights of its own.
    /// </summary>
    public TSeatId? Seat { get; }

    /// <inheritdoc />
    public string? Refusal { get; }

    /// <summary>
    /// Who a change this caller makes is recorded as: a seat as itself; system work as the system with its scope,
    /// or as the operator or the token <see cref="TenancyWork"/> began it for. <see langword="null"/> for nobody,
    /// who changes nothing. An event log's rows, the columns of <c>RecordsWhoChanged</c> and Tenancy's own
    /// records all read this one answer, so they cannot come to disagree about who acted.
    /// </summary>
    public TenancyActor<TSeatId>? Actor { get; }

    object? ITenancyCaller.TenantId => Tenant;

    object? ITenancyCaller.SeatId => Seat;

    ITenancyActor? ITenancyCaller.Actor => Actor;

    /// <summary>Nobody Tenancy lets in, and why.</summary>
    /// <param name="refusal">One of the codes of <see cref="TenancyRefusals"/>, such as <see cref="TenancyRefusals.NotSeated"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="refusal"/> is not one of Tenancy's codes.</exception>
    public static TenancyCaller<TTenantId, TSeatId> Nobody(string refusal)
    {
        // A refusal nobody can look up would reach the caller as a bug, so it is checked here, once.
        TenancyRefusals.KindOf(refusal);
        return new TenancyCaller<TTenantId, TSeatId>(TenancyCallerKind.Nobody, null, null, refusal, null);
    }

    /// <summary>A person, through their seat in a tenant.</summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="seat">The person's seat in it.</param>
    public static TenancyCaller<TTenantId, TSeatId> InSeat(TTenantId tenant, TSeatId seat)
        => new(TenancyCallerKind.Seat, tenant, seat, null, TenancyActor<TSeatId>.OfSeat(seat));

    /// <summary>
    /// System work inside one tenant, holding every key there. Recorded as the system, in Tenancy's own scope.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="actingSeat">The seat the work is done for, recorded as who placed or granted; it adds no rights.</param>
    public static TenancyCaller<TTenantId, TSeatId> SystemIn(TTenantId tenant, TSeatId? actingSeat = null)
        => SystemIn(tenant, TenancyActor<TSeatId>.OfSystem(TenancyWork.SystemScope, actingSeat));

    /// <summary>
    /// System work inside one tenant, holding every key there, recorded as <paramref name="actor"/>: the system in
    /// a scope of its own, an operator the work is carried out for, or the token a link carried. The seat the
    /// actor names, if any, is the seat the work is done for. <see cref="TenancyWork"/> begins such a caller
    /// together with the toolkit's own, which is how work is meant to begin it.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="actor">
    /// Who the work is recorded as: any kind but a seat, which acts through its own seat. Made with
    /// <see cref="TenancyActor{TSeatId}.OfSystem"/>, <see cref="TenancyActor{TSeatId}.OfOperator"/> or
    /// <see cref="TenancyActor{TSeatId}.OfToken"/>, which name each kind in full.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="actor"/> is a seat, or does not say who it is: an operator without an identity, a token
    /// without a seat, or any of them without a scope.
    /// </exception>
    public static TenancyCaller<TTenantId, TSeatId> SystemIn(TTenantId tenant, TenancyActor<TSeatId> actor)
        => new(TenancyCallerKind.SystemInTenant, tenant, actor.Seat, null, OfSystemWork(actor));

    /// <summary>
    /// System work outside any tenant, recorded as <paramref name="actor"/>: an operator a tenant is provisioned
    /// for. It only provisions, as <see cref="System"/> does.
    /// </summary>
    /// <param name="actor">Who the work is recorded as: any kind but a seat, which is in a tenant.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="actor"/> is a seat, or does not say who it is: an operator without an identity, a token
    /// without a seat, or any of them without a scope.
    /// </exception>
    public static TenancyCaller<TTenantId, TSeatId> SystemBy(TenancyActor<TSeatId> actor)
        => new(TenancyCallerKind.System, null, null, null, OfSystemWork(actor));

    /// <summary>
    /// The actor system work is recorded as. Never a seat: a person acts through their own seat, with that seat's
    /// keys. And always one that says who it is, as the factories of <see cref="TenancyActor{TSeatId}"/> make it:
    /// an actor put together by hand that names nobody would be written into records that are kept for good.
    /// </summary>
    private static TenancyActor<TSeatId> OfSystemWork(TenancyActor<TSeatId> actor)
    {
        if (actor.Kind == TenancyActorKind.Seat)
        {
            throw new ArgumentException(
                "System work is recorded as the system, an operator or a token, never as a seat: a seat acts through TenancyCaller.InSeat, with its own keys.",
                nameof(actor));
        }

        var named = !string.IsNullOrWhiteSpace(actor.Scope) && actor.Kind switch
        {
            TenancyActorKind.System => true,
            TenancyActorKind.Operator => actor.Operator is { } identity && identity != Guid.Empty,
            TenancyActorKind.Token => actor.Seat is not null,
            _ => false,
        };

        return named
            ? actor
            : throw new ArgumentException(
                "The actor does not say who acted: an operator has an identity, a token a seat, and each of them and the system the scope of the work. " +
                "Make it with TenancyActor.OfSystem, OfOperator or OfToken.",
                nameof(actor));
    }
}
