using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// An invitation into a tenant: an offer, to whoever holds its token, of a seat placed in one unit with one
/// role there. The application declares its own class with
/// <see cref="InvitationAggregateAttribute{TInvitationId}"/>.
/// <para>
/// No seat exists until the invitation is accepted. Accepting it makes the seat for the verified identity of
/// the person who accepts, places it in <see cref="UnitId"/> and grants it <see cref="RoleId"/> there until
/// <see cref="GrantUntil"/>, all in one save. Until then the invitation is the only thing there is, and
/// cancelling it leaves nothing behind.
/// </para>
/// <para>
/// It is for an address, the one it is sent to, and that is all the address is: where to send it, and what the
/// people who manage seats recognize it by. It never finds a person. What accepts an invitation is its token and
/// a verified identity; an application that knows the verified address of that identity may pass it along, and
/// an invitation sent elsewhere is then refused. The address is forgotten once the invitation is accepted or
/// cancelled, and so is the name suggested for the seat.
/// </para>
/// <para>
/// The token is not here, and neither is its digest: the store keeps the digest apart from the invitation, so
/// reading invitations never reads it.
/// </para>
/// <para>
/// What it offers does not change after it was issued: the tenant, the unit, the role, the end of the grant, when
/// it ends itself and who issued it. An issuer's rights are asked again when it is accepted, by the use case.
/// </para>
/// </summary>
/// <typeparam name="TInvitationId">The application's invitation id.</typeparam>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
/// <typeparam name="TUnitId">The application's unit id.</typeparam>
/// <typeparam name="TRoleId">The application's role id.</typeparam>
/// <typeparam name="TSeatId">The application's seat id.</typeparam>
[AggregateRootBase]
public abstract partial class InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
{
    /// <summary>The longest address an invitation may be for: the longest an e-mail address may be.</summary>
    public const int MaxAddressLength = 254;

    /// <summary>The longest name an invitation may suggest for the seat, which is the longest a seat may have.</summary>
    public const int MaxDisplayNameLength = 200;

    /// <summary>The tenant the invitation is into.</summary>
    public TTenantId TenantId { get; private set; }

    /// <summary>
    /// The address the invitation is for, as it was given, while the invitation is open; <see langword="null"/>
    /// once it was accepted or cancelled.
    /// </summary>
    public string? Address { get; private set; }

    /// <summary>The unit the seat is placed in, as its primary placement.</summary>
    public TUnitId UnitId { get; private set; }

    /// <summary>The role the seat is granted at that unit.</summary>
    public TRoleId RoleId { get; private set; }

    /// <summary>When the grant ends, or <see langword="null"/> for no end. Later than <see cref="ExpiresAt"/>.</summary>
    public DateTimeOffset? GrantUntil { get; private set; }

    /// <summary>
    /// A name suggested for the seat, which whoever accepts may replace, or <see langword="null"/>. Forgotten
    /// with the address.
    /// </summary>
    public string? DisplayName { get; private set; }

    /// <summary>Whether it is open, accepted or cancelled.</summary>
    public InvitationState State { get; private set; }

    /// <summary>When it was issued.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>The first moment it can no longer be accepted.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// The seat that issued it, or the seat system work issued it for; <see langword="null"/> when system work
    /// issued it with no seat acting. The seat a placement and a grant made from it keep as who made them.
    /// </summary>
    public TSeatId? IssuedBy { get; private set; }

    /// <summary>
    /// Whether system work issued it. An invitation a seat issued gives no more than that seat may still give
    /// when it is accepted; one system work issued is not held to a seat.
    /// </summary>
    public bool IssuedAsSystem { get; private set; }

    /// <summary>When it was accepted, or <see langword="null"/>.</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>The seat it made, or <see langword="null"/> while nobody accepted it.</summary>
    public TSeatId? AcceptedAs { get; private set; }

    /// <summary>When it was cancelled, or <see langword="null"/>.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>
    /// Whether the invitation can be accepted at <paramref name="moment"/>: it is open and its time has not run
    /// out. Decided from the clock every time it is asked, so nothing has to mark an invitation as run out
    /// for it to be so.
    /// </summary>
    /// <param name="moment">The moment to ask about, usually now.</param>
    public bool IsOpenAt(DateTimeOffset moment) => State == InvitationState.Open && ExpiresAt > moment;

    /// <summary>
    /// Whether the invitation is for <paramref name="address"/>: the same address, whatever the case of its
    /// letters and the space around it. False once the address is forgotten.
    /// </summary>
    /// <param name="address">An address, such as the verified address of the identity that accepts.</param>
    public bool IsFor(string? address)
        => Address is { } own && address is not null && string.Equals(own, address.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What a constructor would do: gives a new instance what it offers and to whom, opens it, and raises
    /// <see cref="InvitationIssued{TTenantId, TInvitationId, TUnitId, TRoleId, TSeatId}"/>. Called once, by
    /// <see cref="TenancyInstances"/>, right after the instance is made.
    /// </summary>
    /// <exception cref="RefusalException">
    /// <c>tenancy.address-invalid</c>, <c>tenancy.name-invalid</c> for a suggested name that is too long,
    /// <c>tenancy.invitation-grant-ends-first</c> for a grant that would end before the invitation does.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="expiresAt"/> is not after <paramref name="issuedAt"/>.</exception>
    internal void InitializeNew(
        TInvitationId id,
        TTenantId tenantId,
        string address,
        TUnitId unitId,
        TRoleId roleId,
        DateTimeOffset? grantUntil,
        string? displayName,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        TSeatId? issuedBy,
        bool issuedAsSystem,
        TenancyActor<TSeatId>? by)
    {
        if (expiresAt <= issuedAt)
        {
            throw new ArgumentException("An invitation ends after it was issued.", nameof(expiresAt));
        }

        var sentTo = ValidAddress(address) ?? throw TenancyRefusals.Of(TenancyRefusals.AddressInvalid, ("Max", MaxAddressLength));
        var suggested = TenancyNames.Optional(displayName, TenancyNames.DisplayNameToken, MaxDisplayNameLength);
        if (grantUntil is { } until && until <= expiresAt)
        {
            throw TenancyRefusals.Of(TenancyRefusals.InvitationGrantEndsFirst);
        }

        Id = id;
        TenantId = tenantId;
        Address = sentTo;
        UnitId = unitId;
        RoleId = roleId;
        GrantUntil = grantUntil;
        DisplayName = suggested.Length == 0 ? null : suggested;
        State = InvitationState.Open;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        IssuedBy = issuedBy;
        IssuedAsSystem = issuedAsSystem;

        RaiseDomainEvent(new InvitationIssued<TTenantId, TInvitationId, TUnitId, TRoleId, TSeatId>(tenantId, id, unitId, roleId, expiresAt, issuedBy, by));
    }

    /// <summary>
    /// Cancels an open invitation, one whose time ran out included: it can no longer be accepted, and forgets
    /// the address it was for and the name it suggested.
    /// </summary>
    /// <param name="at">When.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.invitation-state</c>: it was accepted or cancelled already.</exception>
    public void Cancel(DateTimeOffset at, TenancyActor<TSeatId>? by = null)
    {
        RequireOpen("cancel");

        State = InvitationState.Cancelled;
        ClosedAt = at;
        ForgetWhoItWasFor();
        RaiseDomainEvent(new InvitationCancelled<TTenantId, TInvitationId, TSeatId>(TenantId, Id, by));
    }

    /// <summary>
    /// Marks the invitation as accepted by the seat it made, and forgets the address it was for and the name it
    /// suggested. Whether it may be accepted, by whom, and making the seat are the use case's: this is its last
    /// step, in the same save.
    /// </summary>
    /// <param name="seat">The seat the acceptance made.</param>
    /// <param name="at">When.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.invitation-state</c>: it was accepted or cancelled already.</exception>
    internal void Accept(TSeatId seat, DateTimeOffset at, TenancyActor<TSeatId>? by)
    {
        RequireOpen("accept");

        State = InvitationState.Accepted;
        AcceptedAt = at;
        AcceptedAs = seat;
        ForgetWhoItWasFor();
        RaiseDomainEvent(new InvitationAccepted<TTenantId, TInvitationId, TSeatId>(TenantId, Id, seat, by));
    }

    /// <summary>An invitation that is over keeps what it offered and who took it, and nothing about the person it was sent to.</summary>
    private void ForgetWhoItWasFor()
    {
        Address = null;
        DisplayName = null;
    }

    private void RequireOpen(string action)
    {
        if (State != InvitationState.Open)
        {
            throw TenancyRefusals.Of(TenancyRefusals.InvitationState, ("State", State.ToString().ToLowerInvariant()), ("Action", action));
        }
    }

    /// <summary>
    /// <paramref name="address"/> without the space around it, when it is one address a message can be sent to:
    /// something before an <c>@</c> and something after it, at most <see cref="MaxAddressLength"/> characters,
    /// with no white space, no control character and nothing that would make it a list or a name with an
    /// address. <see langword="null"/> otherwise. It is not checked any further: whether the address exists
    /// is found out by sending to it.
    /// </summary>
    private static string? ValidAddress(string? address)
    {
        var trimmed = address?.Trim() ?? string.Empty;
        if (trimmed.Length is < 3 or > MaxAddressLength)
        {
            return null;
        }

        var at = trimmed.IndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1 || at != trimmed.LastIndexOf('@'))
        {
            return null;
        }

        foreach (var character in trimmed)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character) || character is ',' or ';' or '<' or '>')
            {
                return null;
            }
        }

        return trimmed;
    }
}
