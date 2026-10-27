using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Security;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.Managers;

/// <summary>
/// Invitations into a tenant: issuing one for an address, listing the open ones, cancelling one, and accepting
/// one, which is what makes the seat. Your code takes it as <c>InvitationManager</c>, a class the generator writes,
/// closed over your classes and ids, in a project that declares an invitation class; the container makes it per scope.
/// <para>
/// An invitation offers what adding a seat, placing it and granting it a role would give, so issuing one takes
/// what those take: <see cref="TenancyPermissions.SeatsManage"/> for the whole tenant, as adding a seat does, and
/// <see cref="TenancyPermissions.GrantsManage"/> at the unit, with the rule for giving a role there. A role that
/// manages no access is offered freely, and may outlast the issuer's own grants. A role that manages access is
/// offered only by a seat that holds each of its permissions that do at the unit, until at least the end of the grant,
/// unless the application turns containment off (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>),
/// and then as freely as any other. Nobody invites themself into one: an identity that has a seat in the tenant
/// cannot accept.
/// </para>
/// <para>
/// An invitation never gives more than its issuer could give at the moment it is used. Accepting asks the
/// issuer's rights again, as they are then, with the role as it is then, and an invitation whose issuer has
/// lost them is refused with one answer that says nothing of which. System work is not held to a seat, and
/// neither is an invitation it issued.
/// </para>
/// <para>
/// The token is returned once, by <see cref="IssueAsync(string, TUnitId, TRoleId, DateTimeOffset?, TimeSpan?, CancellationToken, TInvitationId?)"/>,
/// and kept nowhere: the store holds its digest. It is in no event, no refusal and no log of the package. Sending it to
/// the address is the application's to do.
/// </para>
/// </summary>
/// <param name="store">Where seats, roles and the rest are loaded, and where the unit of work is saved.</param>
/// <param name="invitations">Where invitations are loaded and kept, in that unit of work.</param>
/// <param name="catalogue">The permissions asked for, and which roles manage access.</param>
/// <param name="options">Which signed-in users may hold a seat: whom an acceptance seats.</param>
/// <param name="invitationOptions">How long an invitation stays open.</param>
/// <param name="callers">Who is calling, as the toolkit says: the signed-in identity that accepts.</param>
/// <param name="clock">What "now" is.</param>
public class InvitationManager<
    [TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant,
    [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
    [TemplateType(typeof(OrganizationAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TOrganization,
    [TemplateType(typeof(OrganizationUnitAttribute<>), Take = TemplateArgumentKind.Type)] TUnit,
    [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
    [TemplateType(typeof(SeatAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TSeat,
    [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
    [TemplateType(typeof(RoleAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TRole,
    [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId,
    [TemplateType(typeof(InvitationAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TInvitation,
    [TemplateType(typeof(InvitationAggregateAttribute<>))] TInvitationId>(
    ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> store,
    IInvitationStore<TInvitation, TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId> invitations,
    TenancyCatalogue catalogue,
    TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> options,
    TenancyInvitationOptions<TInvitationId> invitationOptions,
    ICallerAccessor callers,
    TimeProvider clock)
    where TTenant : TenantAggregate<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
    where TInvitationId : struct, ICreatableEntityId<TInvitationId>, IEquatable<TInvitationId>
{
    /// <summary>The reason recorded on the grant an accepted invitation makes.</summary>
    private const string AcceptedReason = "invitation";

    /// <summary>The claim an identity provider marks a sign-in without an account with.</summary>
    private const string AnonymousClaim = "is_anonymous";

    // ------------------------------------------------------------------ commands

    /// <summary>
    /// Issues an invitation for an address: a seat placed in <paramref name="unit"/> as its primary placement,
    /// with <paramref name="role"/> there until <paramref name="grantUntil"/>, for whoever accepts it with the
    /// token this returns. Nothing is made but the invitation.
    /// </summary>
    /// <param name="address">The address the invitation is for. The package sends nothing to it.</param>
    /// <param name="unit">The unit the seat is placed in, which must be active.</param>
    /// <param name="role">The role the seat is granted there, which must be active.</param>
    /// <param name="grantUntil">When the grant ends, later than the invitation itself, or <see langword="null"/> for no end.</param>
    /// <param name="lifetime">
    /// How long the invitation stays open, within <see cref="TenancyInvitationOptions{TInvitationId}.MinLifetime"/>
    /// and <see cref="TenancyInvitationOptions{TInvitationId}.MaxLifetime"/>, or <see langword="null"/> for
    /// <see cref="TenancyInvitationOptions{TInvitationId}.DefaultLifetime"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="id">Its id, for imports and seeding; a new one otherwise.</param>
    /// <returns>The new invitation, and its token, which is shown this once.</returns>
    /// <exception cref="RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.SeatsManage"/> for the whole tenant or
    /// <see cref="TenancyPermissions.GrantsManage"/> at the unit, <c>tenancy.invitation-lifetime</c>,
    /// <c>tenancy.tenant-inactive</c>, <c>tenancy.role-not-found</c>, <c>tenancy.grant-exceeds-own</c> for a
    /// role that manages access the caller could not give there for that long, while containment is on
    /// (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>), <c>tenancy.unit-not-found</c>,
    /// <c>tenancy.unit-not-active</c>, <c>tenancy.role-not-active</c>, <c>tenancy.address-invalid</c>,
    /// <c>tenancy.invitation-grant-ends-first</c>.
    /// </exception>
    /// <example>
    /// <code>
    /// var issued = await invitations.IssueAsync(command.Address, command.Unit, command.Role, command.Until, lifetime: null, cancellationToken);
    /// // Send issued.Token to the address. Tenancy keeps only its digest.
    /// </code>
    /// </example>
    public async Task<IssuedInvitation> IssueAsync(
        string address,
        TUnitId unit,
        TRoleId role,
        DateTimeOffset? grantUntil,
        TimeSpan? lifetime,
        CancellationToken cancellationToken,
        TInvitationId? id = null)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.RequireAtTenantAsync(TenancyPermissions.SeatsManage, cancellationToken).ConfigureAwait(false);
        await gate.RequireAtAsync(TenancyPermissions.GrantsManage, unit, cancellationToken).ConfigureAwait(false);

        var settings = invitationOptions.Checked();
        var open = lifetime ?? settings.DefaultLifetime;
        if (open < settings.MinLifetime || open > settings.MaxLifetime)
        {
            throw TenancyRefusals.Refuse(
                TenancyRefusals.InvitationLifetime,
                ("Min", (long)settings.MinLifetime.TotalMinutes),
                ("Max", (long)settings.MaxLifetime.TotalMinutes));
        }

        // A seat acts in an active tenant only; system work is asked here.
        var tenant = await gate.LoadTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (!tenant.IsActive)
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.TenantInactive);
        }

        var offered = await gate.LoadRoleAsync(role, cancellationToken).ConfigureAwait(false);
        await gate.RequireGrantableAsync(to: null, unit, offered, grantUntil, cancellationToken).ConfigureAwait(false);
        await gate.RequireActiveUnitAsync(tenantId, unit, cancellationToken).ConfigureAwait(false);
        if (!offered.Facts.IsActive)
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.RoleNotActive);
        }

        var token = BearerTokens.New();
        var invitation = TenancyInstances.NewInvitation<TInvitation, TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>(
            id ?? TInvitationId.Create(),
            tenantId,
            address,
            unit,
            role,
            grantUntil,
            issuedAt: gate.Now,
            expiresAt: gate.Now + open,
            issuedBy: gate.Caller.Seat,
            issuedAsSystem: gate.BySystem,
            gate.By);

        invitations.Add(invitation, token.Digest);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return new IssuedInvitation(invitation, token.Token);
    }

    /// <summary>
    /// Issues an invitation for an address, as
    /// <see cref="IssueAsync(string, TUnitId, TRoleId, DateTimeOffset?, TimeSpan?, CancellationToken, TInvitationId?)"/> does
    /// with the ids of the unit and the role.
    /// </summary>
    /// <inheritdoc cref="IssueAsync(string, TUnitId, TRoleId, DateTimeOffset?, TimeSpan?, CancellationToken, TInvitationId?)"/>
    /// <example>
    /// Tove is invited as Surveyor at North Coast. The invitation stays open for a week:
    /// <code>
    /// var issued = await invitations.IssueAsync("tove@example.com", northCoast, surveyor, grantUntil: null, lifetime: TimeSpan.FromDays(7), cancellationToken);
    /// </code>
    /// </example>
    public Task<IssuedInvitation> IssueAsync(
        string address,
        TUnit unit,
        TRole role,
        DateTimeOffset? grantUntil,
        TimeSpan? lifetime,
        CancellationToken cancellationToken,
        TInvitationId? id = null)
        => IssueAsync(
            address,
            (unit ?? throw new ArgumentNullException(nameof(unit))).Id,
            (role ?? throw new ArgumentNullException(nameof(role))).Id,
            grantUntil,
            lifetime,
            cancellationToken,
            id);

    /// <summary>
    /// Cancels an open invitation, so its token no longer works. A seat needs
    /// <see cref="TenancyPermissions.SeatsManage"/> at the invitation's unit, whoever issued it; an invitation into a
    /// unit where it does not manage seats is not found, as it is not among the ones it lists.
    /// </summary>
    /// <param name="invitation">The invitation.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The invitation, cancelled.</returns>
    /// <exception cref="RefusalException">
    /// <c>tenancy.invitation-not-found</c>, <c>tenancy.invitation-state</c> for one that was accepted or
    /// cancelled already.
    /// </exception>
    /// <example>
    /// <code>
    /// var cancelled = await invitations.CancelAsync(command.Invitation, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TInvitation> CancelAsync(TInvitationId invitation, CancellationToken cancellationToken)
    {
        var gate = Gate();
        gate.RequireTenant();

        var cancelled = await invitations.FindAsync(invitation, cancellationToken).ConfigureAwait(false);
        if (cancelled is null
            || (!gate.BySystem && !await gate.Queries.HoldsAtAsync(TenancyPermissions.SeatsManage, cancelled.UnitId, cancellationToken).ConfigureAwait(false)))
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.InvitationNotFound);
        }

        cancelled.Cancel(gate.Now, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return cancelled;
    }

    /// <summary>Cancels an open invitation, as <see cref="CancelAsync(TInvitationId, CancellationToken)"/> does with its id.</summary>
    /// <inheritdoc cref="CancelAsync(TInvitationId, CancellationToken)"/>
    /// <example>
    /// <code>
    /// var cancelled = await invitations.CancelAsync(issued.Invitation, cancellationToken);
    /// </code>
    /// </example>
    public Task<TInvitation> CancelAsync(TInvitation invitation, CancellationToken cancellationToken)
        => CancelAsync((invitation ?? throw new ArgumentNullException(nameof(invitation))).Id, cancellationToken);

    /// <summary>
    /// Accepts the invitation <paramref name="token"/> is for, as the signed-in identity that is calling: in
    /// one save it makes that identity's seat in the invitation's tenant, places it in the invitation's unit as
    /// its primary placement, grants it the invitation's role there, and marks the invitation as accepted.
    /// <para>
    /// The caller is the toolkit's own, read here and never handed in: a signed-in user with a verified
    /// identity, whose token role may hold a seat, and who did not sign in anonymously. It names no tenant and
    /// needs no seat: the token says which invitation, and so which tenant. Nothing is found by an address.
    /// </para>
    /// <para>
    /// From there the work is the application's own, inside that tenant alone, for the seat that issued the
    /// invitation: that seat is who the placement and the grant keep as their giver, and the events name the
    /// system acting for it.
    /// </para>
    /// <para>
    /// Accepting again as the same identity answers the same seat. Two acceptances at the same moment are kept
    /// apart by the invitation's own version and by the tenant's access revision: one is saved, and the other
    /// fails as any save that came second does, and is answered what the first made of the invitation when it
    /// is sent again.
    /// </para>
    /// </summary>
    /// <param name="token">The token, as <c>IssueAsync</c> returned it.</param>
    /// <param name="verifiedAddress">
    /// The address the application knows the calling identity to have, verified by its identity provider, or
    /// <see langword="null"/> when the application does not hold an invitation to its address. Given, it only
    /// narrows: an invitation for another address is refused. It never finds an invitation or a person.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="seat">The new seat's id, for imports and seeding; a new one otherwise.</param>
    /// <param name="configure">
    /// Sets the fields the application added to its seat class, such as the name it is shown by, on the new
    /// seat once the invitation is found to hold, and the seat is placed and holds the role, and before the
    /// invitation is marked as accepted and the seat is handed to the store: so they are saved in the same
    /// transaction, and the class's own rules judge them there. When it throws, nothing is accepted: no seat is
    /// saved, by this call or by a later save in the same scope, and the invitation stays open. It is not run
    /// for an invitation the same identity accepted before, which answers the seat it made then.
    /// </param>
    /// <returns>The tenant, whose slug the next request selects it by, and the seat the caller now has in it.</returns>
    /// <exception cref="RefusalException">
    /// <c>tenancy.identity-required</c> for a caller that is no signed-in identity that may hold a seat;
    /// <c>tenancy.invitation-not-found</c>, for a text that is no token too; <c>tenancy.invitation-cancelled</c>;
    /// <c>tenancy.invitation-used</c> when somebody else accepted it; <c>tenancy.invitation-lapsed</c> when its
    /// time ran out; <c>tenancy.address-mismatch</c>; <c>tenancy.tenant-inactive</c>;
    /// <c>tenancy.identity-has-seat</c>; <c>tenancy.unit-not-found</c> and <c>tenancy.unit-not-active</c>;
    /// <c>tenancy.role-not-found</c> and <c>tenancy.role-not-active</c>; <c>tenancy.invitation-unbacked</c> when
    /// its issuer may no longer give what it offers.
    /// </exception>
    /// <exception cref="ConcurrencyConflictException">Another change of access in the tenant, another acceptance included, was saved first.</exception>
    /// <example>
    /// The handler of a request marked <c>[SignedIn]</c>, which names no tenant:
    /// <code>
    /// var accepted = await invitations.AcceptAsync(command.Token, verifiedAddress: null, cancellationToken, configure: seat =&gt; seat.Rename(command.DisplayName));
    /// // accepted.Tenant.Slug selects the tenant on the next request
    /// </code>
    /// </example>
    public async Task<AcceptedInvitation> AcceptAsync(
        string token,
        string? verifiedAddress,
        CancellationToken cancellationToken,
        TSeatId? seat = null,
        Action<TSeat>? configure = null)
    {
        var identity = AcceptingIdentity(callers.Current);

        // A text that is no token has no digest, and is answered as a token nobody was given.
        if (!BearerTokens.TryDigest(token, out var digest)
            || await invitations.FindByDigestAsync(digest, cancellationToken).ConfigureAwait(false) is not { } found)
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.InvitationNotFound);
        }

        // From here on the work acts in the invitation's tenant and in no other, for the seat that issued it.
        using (TenancyWork.BeginSystemIn(found.Tenant, found.IssuedBy))
        {
            return await AcceptInAsync(found.Invitation, identity, verifiedAddress, seat, configure, cancellationToken).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ reads

    /// <summary>
    /// The tenant's invitations that can still be accepted, the soonest to end first, the application's own class: for
    /// a seat, those into the units where it holds <see cref="TenancyPermissions.SeatsManage"/>, which may be none; for
    /// system work in the tenant, all of them. They are read for the answer, and tracked by nobody. An invitation holds
    /// no token: the store keeps its digest apart.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The open invitations the caller manages, the soonest to end first.</returns>
    /// <exception cref="RefusalException">The caller is nobody.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// var open = await invitations.ListOpenAsync(cancellationToken);
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<TInvitation>> ListOpenAsync(CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();

        var open = await invitations.ListOpenAsync(tenantId, gate.Now, cancellationToken).ConfigureAwait(false);
        if (!gate.BySystem)
        {
            var managed = (await store.Queries.ListAsync(gate.Queries.UnitsWhereIHold(TenancyPermissions.SeatsManage), cancellationToken).ConfigureAwait(false)).ToHashSet();
            open = [.. open.Where(invitation => managed.Contains(invitation.UnitId))];
        }

        return [.. open.OrderBy(invitation => invitation.ExpiresAt)];
    }

    // ------------------------------------------------------------------ the records it answers

    /// <summary>An invitation as <c>IssueAsync</c> returns it: the one time its token is shown.</summary>
    /// <param name="Invitation">The new invitation, the application's own class: its id, its address, and when it ends.</param>
    /// <param name="Token">
    /// The token whoever accepts needs. Send it to the person, in the fragment of a link or in a message's text,
    /// and keep it nowhere.
    /// </param>
    public sealed record IssuedInvitation(TInvitation Invitation, string Token)
    {
        /// <summary>Says which invitation and not its token: a record prints its members, and this one must not end up in a log.</summary>
        public override string ToString() => nameof(IssuedInvitation) + " " + Invitation.Id;
    }

    /// <summary>What accepting an invitation gives the person who accepted.</summary>
    /// <param name="Tenant">The tenant they now have a seat in, whose slug the next request selects it by.</param>
    /// <param name="Seat">Their seat, the application's own class.</param>
    public sealed record AcceptedInvitation(TTenant Tenant, TSeat Seat);

    // ------------------------------------------------------------------ helpers

    /// <summary>Everything of <see cref="AcceptAsync"/> after the invitation is found, run as system work in its tenant.</summary>
    private async Task<AcceptedInvitation> AcceptInAsync(
        TInvitationId id,
        Guid identity,
        string? verifiedAddress,
        TSeatId? seatId,
        Action<TSeat>? configure,
        CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();

        var invitation = await invitations.FindAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw TenancyRefusals.Refuse(TenancyRefusals.InvitationNotFound);
        var tenant = await gate.LoadTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        switch (invitation.State)
        {
            case InvitationState.Cancelled:
                throw TenancyRefusals.Refuse(TenancyRefusals.InvitationCancelled);

            case InvitationState.Accepted:
                // The same person, sending it again: the answer they were given, or did not get, the first time.
                return invitation.AcceptedAs is { } made
                       && await store.FindSeatAsync(made, cancellationToken).ConfigureAwait(false) is { } theirs
                       && theirs.Identity == identity
                    ? new AcceptedInvitation(tenant, theirs)
                    : throw TenancyRefusals.Refuse(TenancyRefusals.InvitationUsed);
        }

        // Decided now, from the clock: nothing has to have marked the invitation as run out. The grant it
        // offers ends after the invitation does, so while the invitation is open the grant is not over.
        if (!invitation.IsOpenAt(gate.Now))
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.InvitationLapsed);
        }

        if (verifiedAddress is not null && !invitation.IsFor(verifiedAddress))
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.AddressMismatch);
        }

        // It changes who holds what: taken before anything about rights is read.
        await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (!tenant.IsActive)
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.TenantInactive);
        }

        if (await store.IdentityHasSeatAsync(tenantId, identity, cancellationToken).ConfigureAwait(false))
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.IdentityHasSeat);
        }

        await gate.RequireActiveUnitAsync(tenantId, invitation.UnitId, cancellationToken).ConfigureAwait(false);
        var role = await gate.LoadRoleAsync(invitation.RoleId, cancellationToken).ConfigureAwait(false);
        if (!role.Facts.IsActive)
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.RoleNotActive);
        }

        if (!invitation.IssuedAsSystem)
        {
            await RequireStillBackedAsync(invitation, role, cancellationToken).ConfigureAwait(false);
        }

        var seat = TenancyInstances.NewSeat<TSeat, TSeatId, TTenantId, TUnitId, TRoleId>(
            seatId ?? TSeatId.Create(),
            tenantId,
            identity,
            gate.By);
        seat.Place(invitation.UnitId, primary: true, gate.Now, gate.ActorFor(seat.Id), gate.By);
        seat.Grant(invitation.UnitId, role.Id, role.Facts, GrantPeriod.Between(gate.Now, invitation.GrantUntil), gate.ActorFor(seat.Id), AcceptedReason, gate.By);

        // The application's own fields, while the invitation is still open and before the store has the seat:
        // a callback that throws leaves the invitation, which the store tracks, open, and no seat for a later
        // save in the same scope to write.
        configure?.Invoke(seat);
        invitation.Accept(seat.Id, gate.Now, gate.By);

        store.Add(seat);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return new AcceptedInvitation(tenant, seat);
    }

    /// <summary>
    /// Whether the seat that issued <paramref name="invitation"/> could issue it again now: it is active, holds
    /// <see cref="TenancyPermissions.SeatsManage"/> for the whole tenant and <see cref="TenancyPermissions.GrantsManage"/> at
    /// the unit, and may give <paramref name="role"/>, as the role is now, there until the grant's end. Asked
    /// as that seat, with the rule every grant is held to.
    /// </summary>
    /// <exception cref="RefusalException">
    /// <c>tenancy.invitation-unbacked</c>, whatever the issuer lacks: the person who accepts is no member of
    /// the tenant, and is told neither the issuer's keys nor which of them is missing.
    /// </exception>
    private async Task RequireStillBackedAsync(TInvitation invitation, TRole role, CancellationToken cancellationToken)
    {
        if (invitation.IssuedBy is not { } issuer
            || await store.FindSeatAsync(issuer, cancellationToken).ConfigureAwait(false) is not { Status: SeatStatus.Active })
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.InvitationUnbacked);
        }

        // The toolkit's caller stays the system work this runs as: only who Tenancy asks about changes.
        using (TenancyCallers.Begin(TenancyCaller<TTenantId, TSeatId>.InSeat(invitation.TenantId, issuer)))
        {
            var asIssuer = Gate();
            try
            {
                await asIssuer.RequireAtTenantAsync(TenancyPermissions.SeatsManage, cancellationToken).ConfigureAwait(false);
                await asIssuer.RequireAtAsync(TenancyPermissions.GrantsManage, invitation.UnitId, cancellationToken).ConfigureAwait(false);
                await asIssuer.RequireGrantableAsync(to: null, invitation.UnitId, role, invitation.GrantUntil, cancellationToken).ConfigureAwait(false);
            }
            catch (RefusalException refusal) when (refusal.Code is TenancyRefusals.NotPermitted or TenancyRefusals.GrantExceedsOwn)
            {
                throw TenancyRefusals.Refuse(TenancyRefusals.InvitationUnbacked);
            }
        }
    }

    /// <summary>
    /// The verified identity that accepts: a signed-in user, whose token role may hold a seat, and whose
    /// sign-in was not an anonymous one.
    /// </summary>
    /// <exception cref="RefusalException"><c>tenancy.identity-required</c>.</exception>
    private Guid AcceptingIdentity(Caller caller)
        => caller is { Kind: CallerKind.User, UserId: { } identity }
           && identity != Guid.Empty
           && options.TenantSelection.Seats(caller.Role)
           && !string.Equals(caller.Claim(AnonymousClaim), "true", StringComparison.OrdinalIgnoreCase)
            ? identity
            : throw TenancyRefusals.Refuse(TenancyRefusals.IdentityRequired);

    /// <summary>What every command asks before it acts, for one call: the caller and "now" are fixed when it starts.</summary>
    private TenancyGate<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> Gate() => new(store, catalogue, clock);
}
