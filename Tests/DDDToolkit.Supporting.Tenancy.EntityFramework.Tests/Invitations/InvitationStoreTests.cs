using System.Buffers.Text;
using System.Security.Cryptography;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// Invitations stored with Entity Framework: an invitation and the digest of its token are two rows of two
/// tables, written by one save; a token is found by its digest across tenants, on a context of its own, as ids
/// alone; and accepting writes the seat, its placement, its grant and the invitation in one transaction, of which
/// two at once commit one.
/// <para>
/// Harbor and Meadow are two tenants; Ada administers Harbor, where North hangs under the root.
/// </para>
/// </summary>
public abstract class InvitationStoreTests(TestDatabases databases) : IAsyncLifetime
{
    private const string Address = "wren@example.test";

    private static readonly Guid Wren = Guid.NewGuid();

    private TestServices _services = null!;
    private HostTenancy.ProvisionedTenant _harbor = null!;
    private HostTenancy.ProvisionedTenant _meadow = null!;
    private OrganizationUnitId _north;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private RoleId Watcher => _harbor.RolesByPack[HostCatalogue.WatcherPack];

    public async ValueTask InitializeAsync()
    {
        _services = await databases.ServicesAsync();
        _harbor = await _services.ProvisionAsync("harbor");
        _meadow = await _services.ProvisionAsync("meadow");
        _north = await _services.AddUnitAsync(_harbor.Tenant, _harbor.RootUnit, "North");
    }

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Issues an invitation into North with the watchers' role, as Harbor's administrator.</summary>
    private Task<HostTenancy.IssuedInvitation<InvitationId>> IssueAsync(string address = Address, DateTimeOffset? grantUntil = null)
        => _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, services =>
            services.Invitations().IssueAsync(address, _north, Watcher, grantUntil, "Wren", lifetime: null, Cancellation));

    /// <summary>Reads through Tenancy's context as system work in a tenant, in a scope of its own.</summary>
    private Task<T> InAsync<T>(TenantId tenant, Func<TestTenancyContext, Task<T>> read)
        => _services.BySystemIn(tenant, services => read(services.Tenancy()));

    [Fact]
    public async Task An_invitation_and_the_digest_of_its_token_are_two_rows_written_by_one_save()
    {
        _services.Commands.Reset();

        var issued = await IssueAsync(grantUntil: DateTimeOffset.UtcNow.AddDays(90));

        var writes = _services.Commands.Sent.Where(command => command.Writes).ToList();
        writes.Select(command => command.Transaction).Distinct().Should().ContainSingle("the invitation, its digest and its event are one transaction")
            .Which.Should().NotBeNull();

        // The invitation holds what it offers, and nothing of the token.
        var invitation = await InAsync(_harbor.Tenant, context => context.Set<HostInvitation>().AsNoTracking().SingleAsync(Cancellation));
        invitation.Id.Should().Be(issued.Id);
        invitation.Address.Should().Be(Address);
        invitation.UnitId.Should().Be(_north);
        invitation.RoleId.Should().Be(Watcher);
        invitation.State.Should().Be(InvitationState.Open);
        invitation.IssuedBy.Should().Be(_harbor.AdminSeat);
        invitation.ExpiresAt.Should().BeCloseTo(issued.ExpiresAt, TimeSpan.FromMilliseconds(1));

        // The digest is of the token's bytes, in the table next to it; the token itself is in no column of either.
        var kept = await InAsync(_harbor.Tenant, context => context.Set<TenancyInvitationDigest<InvitationId, TenantId>>().AsNoTracking().SingleAsync(Cancellation));
        kept.InvitationId.Should().Be(issued.Id);
        kept.TenantId.Should().Be(_harbor.Tenant);
        kept.Digest.Should().Equal(SHA256.HashData(Base64Url.DecodeFromChars(issued.Token)));
        _services.Commands.Sent.Should().NotContain(command => command.Text.Contains(issued.Token, StringComparison.Ordinal), "the token is no part of any statement");

        // Its event is in the outbox, with ids and no address; an invitation changes nobody's access, so the access history has no row of it.
        var stored = await InAsync(_harbor.Tenant, context => context.Set<OutboxMessage>().AsNoTracking().Where(message => message.EventName == "tenancy.invitation-issued").SingleAsync(Cancellation));
        stored.Payload.Should().Contain(issued.Id.Value.ToString()).And.NotContain(Address).And.NotContain(issued.Token).And.NotContain("Wren");
        (await InAsync(_harbor.Tenant, context => context.Set<EventLogEntry>().AsNoTracking().CountAsync(entry => entry.EventName == "tenancy.invitation-issued", Cancellation))).Should().Be(0);
    }

    [Fact]
    public async Task Accepting_writes_the_seat_its_placement_its_grant_and_the_invitation_in_one_transaction()
    {
        var issued = await IssueAsync();
        _services.Commands.Reset();

        var accepted = await _services.AcceptAsync(Wren, issued.Token, displayName: "Wren Marsh", verifiedAddress: "WREN@example.test");

        accepted.Tenant.Should().Be(_harbor.Tenant);
        accepted.Slug.Should().Be("harbor");
        var writes = _services.Commands.Sent.Where(command => command.Writes).ToList();
        writes.Should().NotBeEmpty();
        writes.Select(command => command.Transaction).Distinct().Should().ContainSingle("everything an acceptance writes is one transaction").Which.Should().NotBeNull();
        writes.Should().OnlyContain(command => command.TenancyCaller!.Kind == TenancyCallerKind.SystemInTenant && Equals(command.TenancyCaller.TenantId, _harbor.Tenant),
            "it is written as system work in the invitation's tenant");

        var seat = await InAsync(_harbor.Tenant, context => context.Set<HostSeat>().AsNoTracking().AsSingleQuery().SingleAsync(row => row.Id == accepted.Seat, Cancellation));
        seat.Identity.Should().Be(Wren);
        seat.DisplayName.Should().Be("Wren Marsh");
        var placement = seat.Placements.Should().ContainSingle().Which;
        placement.UnitId.Should().Be(_north);
        placement.IsPrimary.Should().BeTrue();
        placement.PlacedBy.Should().Be(_harbor.AdminSeat);
        var grant = placement.Grants.Should().ContainSingle().Which;
        grant.RoleId.Should().Be(Watcher);
        grant.GrantedBy.Should().Be(_harbor.AdminSeat);
        grant.Reason.Should().Be("invitation");
        (await _services.StoredRightsAsync(accepted.Seat)).Should().ContainSingle(right => right.Key == HostCatalogue.WidgetRead && right.UnitId == _north);

        var invitation = await InAsync(_harbor.Tenant, context => context.Set<HostInvitation>().AsNoTracking().SingleAsync(Cancellation));
        invitation.State.Should().Be(InvitationState.Accepted);
        invitation.AcceptedAs.Should().Be(accepted.Seat);
        invitation.Address.Should().BeNull("the address is forgotten with the save that accepts");
        invitation.DisplayName.Should().BeNull();

        // The seat's own events are what the access history keeps of it; the invitation's is in the outbox alone.
        var history = await InAsync(_harbor.Tenant, context => context.Set<EventLogEntry>().AsNoTracking()
            .Where(entry => entry.ActedByKind == TenancyActorKinds.System).OrderByDescending(entry => entry.RecordedAt).Select(entry => entry.EventName).Take(3).ToListAsync(Cancellation));
        history.Should().BeEquivalentTo(["tenancy.seat-added", "tenancy.seat-placed", "tenancy.organization-role-granted"]);
        (await InAsync(_harbor.Tenant, context => context.Set<OutboxMessage>().AsNoTracking().CountAsync(message => message.EventName == "tenancy.invitation-accepted", Cancellation))).Should().Be(1);

        // Sent again, it is answered the same seat, and writes nothing.
        _services.Commands.Reset();
        (await _services.AcceptAsync(Wren, issued.Token)).Should().Be(accepted);
        _services.Commands.Sent.Should().NotContain(command => command.Writes);
        await Refused.WithCodeAsync(TenancyRefusals.InvitationUsed, () => _services.AcceptAsync(Guid.NewGuid(), issued.Token));
    }

    [Fact]
    public async Task The_digest_lookup_reads_ids_only_across_tenants_on_a_context_of_its_own()
    {
        var issued = await IssueAsync();
        var elsewhere = await _services.BySystemIn(_meadow.Tenant, services =>
            services.Invitations().IssueAsync("lark@example.test", _meadow.RootUnit, _meadow.RolesByPack[HostCatalogue.WatcherPack], null, "Lark", null, Cancellation));

        DbContext? acceptors = null;
        await using (var scope = _services.Scope())
        using (Callers.Begin(Caller.User(Wren)))
        using (TenancyCallers.BeginNone())
        {
            acceptors = scope.ServiceProvider.Tenancy();
            _services.Commands.Reset();
            await scope.ServiceProvider.Invitations().AcceptAsync(elsewhere.Token, "Wren", verifiedAddress: null, Cancellation);
        }

        // The first statement finds the invitation by the digest, whichever tenant it is in: no tenant was named.
        var lookup = _services.Commands.Sent[0];
        lookup.Text.Should().Contain("\"InvitationDigests\"").And.Contain("\"Digest\" =");
        lookup.Text.Should().NotContain("\"Address\"").And.NotContain("\"DisplayName\"").And.NotContain("\"Digest\",", "it selects the tenant, the invitation and its issuer, and nothing else");
        lookup.Context.Should().NotBeNull().And.NotBeSameAs(acceptors, "it runs on a context of its own");
        lookup.Caller.Should().BeSameAs(Caller.System, "as the application itself, where nothing but the save keeps tenants apart");
        lookup.TenancyCaller.Should().BeNull();
        lookup.Writes.Should().BeFalse();

        // And it found Meadow's: the seat is there, and Harbor's invitation is as it was.
        (await InAsync(_meadow.Tenant, context => context.Set<HostSeat>().AsNoTracking().CountAsync(seat => seat.Identity == Wren, Cancellation))).Should().Be(1);
        (await InAsync(_harbor.Tenant, context => context.Set<HostSeat>().AsNoTracking().CountAsync(seat => seat.Identity == Wren, Cancellation))).Should().Be(0);
        (await InAsync(_harbor.Tenant, context => context.Set<HostInvitation>().AsNoTracking().SingleAsync(row => row.Id == issued.Id, Cancellation))).State.Should().Be(InvitationState.Open);

        // A token nobody was given, and a text that is none, are answered alike.
        await Refused.WithCodeAsync(TenancyRefusals.InvitationNotFound, () => _services.AcceptAsync(Guid.NewGuid(), DDDToolkit.Security.BearerTokens.New().Token));
        await Refused.WithCodeAsync(TenancyRefusals.InvitationNotFound, () => _services.AcceptAsync(Guid.NewGuid(), issued.Token + "="));
    }

    [Fact]
    public async Task Invitations_are_kept_to_their_tenant()
    {
        var harbors = await IssueAsync();
        var meadows = await _services.BySystemIn(_meadow.Tenant, services =>
            services.Invitations().IssueAsync("lark@example.test", _meadow.RootUnit, _meadow.RolesByPack[HostCatalogue.WatcherPack], null, null, null, Cancellation));

        var forAda = await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, services => services.Invitations().ListOpenAsync(Cancellation));
        var forMeadow = await _services.BySystemIn(_meadow.Tenant, services => services.Invitations().ListOpenAsync(Cancellation));

        forAda.Should().ContainSingle().Which.Should().BeEquivalentTo(new { harbors.Id, Address, UnitId = _north, RoleId = Watcher, IssuedBy = (SeatId?)_harbor.AdminSeat, IssuedAsSystem = false });
        forMeadow.Should().ContainSingle().Which.Should().BeEquivalentTo(new { meadows.Id, Address = "lark@example.test", IssuedBy = (SeatId?)null, IssuedAsSystem = true });

        // Another tenant's invitation is not found, by its administrator or by system work there.
        await Refused.WithCodeAsync(TenancyRefusals.InvitationNotFound,
            () => _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, services => services.Invitations().CancelAsync(meadows.Id, Cancellation)));
        await Refused.WithCodeAsync(TenancyRefusals.InvitationNotFound,
            () => _services.BySystemIn(_meadow.Tenant, services => services.Invitations().CancelAsync(harbors.Id, Cancellation)));

        // Cancelled by whoever manages seats at its unit, it is listed no more and its token is of no use.
        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, services => services.Invitations().CancelAsync(harbors.Id, Cancellation));
        (await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, services => services.Invitations().ListOpenAsync(Cancellation))).Should().BeEmpty();
        await Refused.WithCodeAsync(TenancyRefusals.InvitationCancelled, () => _services.AcceptAsync(Wren, harbors.Token));

        // A row of another tenant is refused by the save check, whoever made it.
        await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => _services.BySystemIn(_harbor.Tenant, async services =>
        {
            services.Tenancy().Add(TenancyInstances.NewInvitation<HostInvitation, InvitationId, TenantId, OrganizationUnitId, RoleId, SeatId>(
                InvitationId.CreateSequential(), _meadow.Tenant, Address, _meadow.RootUnit, Watcher, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)));
            await services.Tenancy().SaveChangesAsync(Cancellation);
        }));
    }

    [Fact]
    public async Task Two_accepts_at_once_make_one_seat()
    {
        var issued = await IssueAsync();
        var other = Guid.NewGuid();

        // Wren's acceptance has decided, and is about to save, when somebody else's commits.
        await using (var scope = _services.Scope())
        using (Callers.Begin(Caller.User(Wren)))
        using (TenancyCallers.BeginNone())
        {
            _services.Hook.BeforeSave(scope.ServiceProvider.Tenancy(), () => _services.AcceptAsync(other, issued.Token, displayName: "Lark"));

            var conflict = await FluentActions.Awaiting(() => scope.ServiceProvider.Invitations().AcceptAsync(issued.Token, "Wren", verifiedAddress: null, Cancellation))
                .Should().ThrowAsync<ConcurrencyConflictException>();
            new Type?[] { typeof(HostInvitation), typeof(TenancyAccessRevision<TenantId>) }.Should().Contain(
                conflict.Which.AggregateType,
                "both changed the invitation, and both took the tenant's access revision: whichever row the save found changed first");
        }

        _services.Hook.Fired.Should().BeTrue("the second acceptance committed while the first was saving");
        var seats = await InAsync(_harbor.Tenant, context => context.Set<HostSeat>().AsNoTracking().Where(seat => seat.Id != _harbor.AdminSeat).ToListAsync(Cancellation));
        seats.Should().ContainSingle().Which.Identity.Should().Be(other, "one acceptance made a seat, and the one that came second made none");

        // Sent again, the one that lost is told what became of the invitation.
        await Refused.WithCodeAsync(TenancyRefusals.InvitationUsed, () => _services.AcceptAsync(Wren, issued.Token));
    }

    [Fact]
    public async Task What_an_invitation_offers_cannot_be_changed_by_a_save()
    {
        var issued = await IssueAsync();

        await _services.BySystemIn(_harbor.Tenant, async services =>
        {
            var context = services.Tenancy();
            var invitation = await context.Set<HostInvitation>().SingleAsync(row => row.Id == issued.Id, Cancellation);

            // The class has no method that changes it; a change made past the class is refused by the model.
            context.Entry(invitation).Property(nameof(HostInvitation.UnitId)).CurrentValue = _harbor.RootUnit;
            await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*UnitId*");
        });

        (await InAsync(_harbor.Tenant, context => context.Set<HostInvitation>().AsNoTracking().SingleAsync(Cancellation))).UnitId.Should().Be(_north);
    }
}

/// <summary>Invitations stored with Entity Framework, on SQLite in memory.</summary>
public sealed class InvitationStoreTestsOnSqlite() : InvitationStoreTests(TestDatabases.Sqlite);

/// <summary>Invitations stored with Entity Framework, on Postgres, as the tables' owner with no row level security.</summary>
public sealed class InvitationStoreTestsOnPostgres(PostgresDatabases postgres) : InvitationStoreTests(postgres);
