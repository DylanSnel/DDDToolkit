using DDDToolkit.Exceptions;
using DDDToolkit.Security;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Invitations under the policies. An invitation is read by the seats that manage seats at its unit, added by a
/// seat that could add the seat and make the grant itself, and changed by a seat to cancelled and nothing else;
/// what it offers changes for no role. The digest of its token is in a table nobody's role reads: whoever issues
/// an invitation adds its row, and a function answers which invitation a digest is for, to Tenancy's own work
/// alone. Accepting is that work, in the invitation's tenant, and the use cases run end to end under all of it.
/// <para>
/// Harbor as seeded: Ada administers it, Seth supervises North (units, seats and grants there), Hiro gives roles at
/// North, Oli operates widgets at North Pier; Odette administers Orchard. Wren has no seat anywhere.
/// </para>
/// </summary>
public abstract class InvitationPolicyTests(TenancyPostgres postgres, TenancyNaming names)
{
    private const string Address = "wren@example.test";

    private static readonly Guid Wren = Guid.Parse("d0000000-0000-4000-8000-000000000096");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private Task<TestDatabase> DatabaseAsync() => postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

    /// <summary>Issues an invitation through the use case, as Ada, Harbor's administrator.</summary>
    private static Task<HostTenancy.IssuedInvitation<InvitationId>> IssueAsync(TenancyServices services, OrganizationUnitId unit, RoleId? role = null, string address = Address)
        => services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped =>
            scoped.Invitations().IssueAsync(address, unit, role ?? HarborRoles.Watcher, grantUntil: null, "Wren", lifetime: null, Cancellation));

    /// <summary>An invitation somebody would add past the use cases: a row of <paramref name="tenant"/>, said to be issued by one seat.</summary>
    private static string Insert(Guid id, long tenant, OrganizationUnitId unit, RoleId role, SeatId? by, bool asSystem = false, string state = "Open")
        => "INSERT INTO tenancy.\"Invitations\" (\"Id\", \"TenantId\", \"Address\", \"UnitId\", \"RoleId\", \"State\", \"IssuedAt\", \"ExpiresAt\", \"IssuedBy\", \"IssuedAsSystem\", \"Version\") "
           + $"VALUES ('{id}', {tenant}, 'lark@example.test', '{unit.Value}', '{role.Value}', '{state}', now(), now() + interval '7 days', "
           + $"{(by is { } seat ? $"'{seat.Value}'" : "NULL")}, {(asSystem ? "true" : "false")}, 0)";

    /// <summary>The digest of a token somebody would add past the use cases.</summary>
    private static string Keep(Guid invitation, long tenant)
        => $"INSERT INTO tenancy.\"InvitationDigests\" (\"InvitationId\", \"TenantId\", \"Digest\") VALUES ('{invitation}', {tenant}, pg_catalog.sha256('{invitation}'::bytea))";

    /// <summary>Runs <paramref name="command"/>, expecting a use case to refuse it with <paramref name="code"/>.</summary>
    private static async Task RefusedWithAsync(string code, Func<Task> command)
    {
        var refusal = (await command.Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(code, refusal.Message);
        refusal.Kind.Should().Be(TenancyRefusals.KindOf(code));
    }

    /// <summary>Runs <paramref name="sql"/> as <paramref name="caller"/>, expecting a policy or a missing privilege to refuse it with 42501.</summary>
    private static async Task RefusedAsync(AsCaller caller, string sql, params object[] parameters)
    {
        var refusal = await FluentActions.Awaiting(() => caller.AttemptAsync(sql, Cancellation, parameters)).Should().ThrowAsync<PostgresException>(sql);
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, sql);
    }

    /// <summary>Runs <paramref name="sql"/> as <paramref name="caller"/>, expecting the trigger that keeps what an invitation offers to refuse it.</summary>
    private static async Task FixedAsync(AsCaller caller, string sql)
    {
        var refusal = await FluentActions.Awaiting(() => caller.AttemptAsync(sql, Cancellation)).Should().ThrowAsync<PostgresException>(sql);
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation, sql);
        refusal.Which.ConstraintName.Should().Be("tenancy_invitation_terms_are_fixed", sql);
    }

    [Fact]
    public async Task An_invitation_is_issued_listed_cancelled_and_accepted_under_the_policies()
    {
        var database = await DatabaseAsync();
        await using var services = new TenancyServices(database);

        var issued = await IssueAsync(services, NorthPier, HarborRoles.Operator);
        var withdrawn = await IssueAsync(services, South, address: "lark@example.test");

        // Listed for the seats that manage seats at its unit, and for nobody else.
        Task<IReadOnlyList<HostTenancy.OpenInvitation<InvitationId>>> ListedFor(Person person, TenantId tenant, SeatId seat)
            => services.BySeat(person.Identity, tenant, seat, scoped => scoped.Invitations().ListOpenAsync(Cancellation));
        (await ListedFor(Ada, Harbor, Ada.Seat)).Select(invitation => invitation.Id).Should().BeEquivalentTo(new[] { issued.Id, withdrawn.Id });
        (await ListedFor(Seth, Harbor, Seth.Seat)).Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { issued.Id, Address, UnitId = NorthPier, RoleId = HarborRoles.Operator, IssuedBy = (SeatId?)Ada.Seat, IssuedAsSystem = false });
        (await ListedFor(Hiro, Harbor, Hiro.Seat)).Should().BeEmpty("managing grants is not managing seats");
        (await ListedFor(Oli, Harbor, Oli.Seat)).Should().BeEmpty();
        (await ListedFor(Odette, Orchard, Odette.Seat)).Should().BeEmpty("another tenant's invitations are not Orchard's");

        // Cancelled where the caller manages seats; elsewhere it is not found.
        await RefusedWithAsync(TenancyRefusals.InvitationNotFound,
            () => services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => scoped.Invitations().CancelAsync(withdrawn.Id, Cancellation)));
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Invitations().CancelAsync(withdrawn.Id, Cancellation));
        await RefusedWithAsync(TenancyRefusals.InvitationCancelled,
            () => services.BySignedInUser(Wren, scoped => scoped.Invitations().AcceptAsync(withdrawn.Token, "Wren", verifiedAddress: null, Cancellation)));

        // Accepted by a person who has no seat and names no tenant, at the address it was sent to.
        await RefusedWithAsync(TenancyRefusals.AddressMismatch,
            () => services.BySignedInUser(Wren, scoped => scoped.Invitations().AcceptAsync(issued.Token, "Wren", verifiedAddress: "lark@example.test", Cancellation)));
        var accepted = await services.BySignedInUser(Wren, scoped => scoped.Invitations().AcceptAsync(issued.Token, "Wren Marsh", verifiedAddress: "Wren@Example.Test", Cancellation));
        accepted.Tenant.Should().Be(Harbor);
        accepted.Slug.Should().Be("harbor");

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ScalarAsync<string>("SELECT \"DisplayName\" FROM tenancy.\"Seats\" WHERE \"Identity\" = $1 AND \"TenantId\" = 1", Cancellation, Wren)).Should().Be("Wren Marsh");
            (await owner.ScalarAsync<Guid>("SELECT \"UnitId\" FROM tenancy.\"SeatPlacements\" WHERE \"SeatId\" = $1 AND \"IsPrimary\"", Cancellation, accepted.Seat.Value)).Should().Be(NorthPier.Value);
            (await owner.ScalarAsync<Guid>("SELECT \"GrantedBy\" FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, accepted.Seat.Value, HarborRoles.Operator.Value))
                .Should().Be(Ada.Seat.Value, "the issuer is who the grant keeps as its giver");
            (await owner.ListAsync<string>("SELECT \"Key\" FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 ORDER BY 1", Cancellation, accepted.Seat.Value))
                .Should().Equal([HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead], "the database wrote the new seat's rights, with the save that made it");
            (await owner.ScalarAsync<string>("SELECT \"State\" || ' ' || coalesce(\"Address\", 'forgotten') FROM tenancy.\"Invitations\" WHERE \"Id\" = $1", Cancellation, issued.Id.Value))
                .Should().Be(names.Stored(InvitationState.Accepted) + " forgotten");
            (await owner.ListAsync<string>(
                    "SELECT \"EventName\" FROM ddd.\"EventLog\" WHERE \"TenantId\" = 1 AND \"ActedByKind\" = 'system' AND \"Payload\"::text LIKE '%' || $1 || '%' ORDER BY 1",
                    Cancellation,
                    accepted.Seat.Value.ToString()))
                .Should().Equal(["tenancy.organization-role-granted", "tenancy.seat-added", "tenancy.seat-placed"], "the access history keeps what the acceptance changed, as the system's work");
        }

        // The person is a seat like any other from here on, and the invitation is used.
        var wren = await services.BySeat(Wren, Harbor, accepted.Seat, scoped => scoped.Directory().WhoAmIAsync(Cancellation));
        wren.Keys.Select(key => key.Key).Should().BeEquivalentTo([HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead]);
        (await services.BySignedInUser(Wren, scoped => scoped.Invitations().AcceptAsync(issued.Token, null, null, Cancellation))).Should().Be(accepted);
        await RefusedWithAsync(TenancyRefusals.InvitationUsed,
            () => services.BySignedInUser(Guid.NewGuid(), scoped => scoped.Invitations().AcceptAsync(issued.Token, "Lark", null, Cancellation)));
        await RefusedWithAsync(TenancyRefusals.IdentityHasSeat,
            () => services.BySignedInUser(Ada.Identity, async scoped => await scoped.Invitations().AcceptAsync((await IssueAsync(services, North)).Token, "Ada", null, Cancellation)));
    }

    [Fact]
    public async Task Nobodys_role_reads_the_digest_of_a_token()
    {
        var database = await DatabaseAsync();
        await using var services = new TenancyServices(database);
        var issued = await IssueAsync(services, North);
        BearerTokens.TryDigest(issued.Token, out var digest).Should().BeTrue();

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ScalarAsync<byte[]>("SELECT \"Digest\" FROM tenancy.\"InvitationDigests\"", Cancellation)).Should().Equal(digest, "the row is there, as the tables' owner reads it");
        }

        // Whoever asks, with whatever keys, in the invitation's tenant or not: the table answers no row, and gives none up.
        var callers = new (string Who, Func<Task<AsCaller>> As)[]
        {
            ("Ada, who issued it", () => AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation)),
            ("Seth, who manages seats there", () => AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation)),
            ("Odette, of another tenant", () => AsCaller.PersonAsync(database, Odette.Identity, Orchard, Cancellation)),
            ("Tenancy's own system work in the tenant", () => AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation)),
            ("a module's system work in the tenant", () => AsCaller.SystemInAsync(database, Harbor, "widgets", Cancellation)),
            ("system work in no tenant", () => AsCaller.SystemInAsync(database, tenant: null, TenancyWork.SystemScope, Cancellation)),
            ("an anonymous caller", () => AsCaller.AnonymousAsync(database, Cancellation)),
        };
        foreach (var (who, begin) in callers)
        {
            await using var caller = await begin();
            (await caller.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"InvitationDigests\"", Cancellation)).Should().Be(0, "{0} reads no digest", who);
            (await caller.AttemptAsync("UPDATE tenancy.\"InvitationDigests\" SET \"Digest\" = pg_catalog.sha256('mine'::bytea)", Cancellation)).Should().Be(0, "{0} changes none", who);
            (await caller.AttemptAsync("DELETE FROM tenancy.\"InvitationDigests\"", Cancellation)).Should().Be(0, "{0} removes none", who);
        }

        // Which invitation a digest is for is answered to Tenancy's own work, as ids, and to nobody else.
        const string Asked = "SELECT \"InvitationId\" FROM tenancy.invitation_of_digest($1)";
        await using (var tenancys = await AsCaller.SystemInAsync(database, tenant: null, TenancyWork.SystemScope, Cancellation))
        {
            (await tenancys.ListAsync<Guid>(Asked, Cancellation, digest!)).Should().Equal(issued.Id.Value);
            (await tenancys.ListAsync<string>("SELECT \"TenantId\"::text || ' ' || \"IssuedBy\"::text FROM tenancy.invitation_of_digest($1)", Cancellation, digest!))
                .Should().Equal("1 " + Ada.Seat.Value);
            (await tenancys.ListAsync<Guid>(Asked, Cancellation, new byte[32])).Should().BeEmpty("a digest nobody's token has is nobody's invitation");
        }

        await using (var modules = await AsCaller.SystemInAsync(database, Harbor, "widgets", Cancellation))
        {
            (await modules.ListAsync<Guid>(Asked, Cancellation, digest!)).Should().BeEmpty("system work in another scope than Tenancy's is answered nothing");
        }

        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            await RefusedAsync(ada, Asked, digest!);
        }

        await using var anonymous = await AsCaller.AnonymousAsync(database, Cancellation);
        await RefusedAsync(anonymous, Asked, digest!);
    }

    [Fact]
    public async Task An_invitation_is_read_by_the_seats_that_manage_seats_at_its_unit()
    {
        var database = await DatabaseAsync();
        await using var services = new TenancyServices(database);
        await IssueAsync(services, NorthPier, address: "pier@example.test");
        await IssueAsync(services, South, address: "south@example.test");

        const string Read = "SELECT \"Address\" FROM tenancy.\"Invitations\" ORDER BY 1";
        async Task<List<string>> ReadAsAsync(Person person, TenantId tenant)
        {
            await using var caller = await AsCaller.PersonAsync(database, person.Identity, tenant, Cancellation);
            return await caller.ListAsync<string>(Read, Cancellation);
        }

        (await ReadAsAsync(Ada, Harbor)).Should().Equal("pier@example.test", "south@example.test");
        (await ReadAsAsync(Seth, Harbor)).Should().Equal(["pier@example.test"], "Seth manages seats at North, and North Pier is below it");
        (await ReadAsAsync(Hiro, Harbor)).Should().BeEmpty("a grants manager is no seat manager");
        (await ReadAsAsync(Oli, Harbor)).Should().BeEmpty();
        (await ReadAsAsync(Odette, Orchard)).Should().BeEmpty();
        (await ReadAsAsync(Ada, Orchard)).Should().BeEmpty("Ada has no seat in Orchard, so she is nobody there");

        await using (var harbor = await AsCaller.SystemInAsync(database, Harbor, "widgets", Cancellation))
        {
            (await harbor.ListAsync<string>(Read, Cancellation)).Should().HaveCount(2, "system work reads its tenant, in any scope");
            (await harbor.AttemptAsync("UPDATE tenancy.\"Invitations\" SET \"DisplayName\" = 'Nobody'", Cancellation)).Should().Be(0, "and writes Tenancy's tables in Tenancy's own scope alone");
        }

        await using var orchard = await AsCaller.SystemInAsync(database, Orchard, TenancyWork.SystemScope, Cancellation);
        (await orchard.ListAsync<string>(Read, Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_a_seat_that_could_add_the_seat_and_make_the_grant_adds_an_invitation_by_a_query()
    {
        // Eve is given seats and grants for the whole tenant, and no other key that manages access.
        var database = await DatabaseAsync();
        await HoldAtAsync(database, Eve, HarborRoot, [TenancyKeys.SeatsManage, TenancyKeys.GrantsManage], Cancellation);

        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            await RefusedAsync(seth, Insert(Guid.NewGuid(), 1, North, HarborRoles.Watcher, Seth.Seat));
        }

        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            await RefusedAsync(hiro, Insert(Guid.NewGuid(), 1, North, HarborRoles.Watcher, Hiro.Seat));
        }

        await using (var eve = await AsCaller.PersonAsync(database, Eve.Identity, Harbor, Cancellation))
        {
            (await eve.AttemptAsync(Insert(Guid.NewGuid(), 1, North, HarborRoles.Watcher, Eve.Seat), Cancellation)).Should().Be(1, "a role that manages no access is offered without its keys");
            await RefusedAsync(eve, Insert(Guid.NewGuid(), 1, North, HarborRoles.Supervisor, Eve.Seat));
        }

        await using var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation);
        (await ada.AttemptAsync(Insert(Guid.NewGuid(), 1, North, HarborRoles.Supervisor, Ada.Seat), Cancellation)).Should().Be(1, "she holds every key the supervisors' role has that manages access");

        // What accepting an invitation trusts is not a seat's to say: that system work issued it, or another seat.
        await RefusedAsync(ada, Insert(Guid.NewGuid(), 1, North, HarborRoles.Watcher, Ada.Seat, asSystem: true));
        await RefusedAsync(ada, Insert(Guid.NewGuid(), 1, North, HarborRoles.Watcher, Seth.Seat));
        await RefusedAsync(ada, Insert(Guid.NewGuid(), 1, North, HarborRoles.Watcher, by: null));
        await RefusedAsync(ada, Insert(Guid.NewGuid(), 1, North, HarborRoles.Watcher, Ada.Seat, state: "Accepted"));

        // Nor another tenant's row, another tenant's unit, or another tenant's role.
        await RefusedAsync(ada, Insert(Guid.NewGuid(), 2, OrchardRoot, OrchardRoles.Watcher, Ada.Seat));
        await RefusedAsync(ada, Insert(Guid.NewGuid(), 1, OrchardRoot, HarborRoles.Watcher, Ada.Seat));
        await RefusedAsync(ada, Insert(Guid.NewGuid(), 1, North, OrchardRoles.Watcher, Ada.Seat));
    }

    [Fact]
    public async Task A_seat_changes_an_invitation_to_cancelled_and_nothing_else_by_a_query()
    {
        var database = await DatabaseAsync();
        await using var services = new TenancyServices(database);
        var issued = await IssueAsync(services, NorthPier);
        var id = issued.Id.Value;

        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.AttemptAsync("UPDATE tenancy.\"Invitations\" SET \"State\" = 'Cancelled'", Cancellation)).Should().Be(0, "an invitation he cannot read he cannot change");
        }

        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            // Accepting is the application's own work: a seat neither marks an invitation as accepted nor names the seat it made.
            await RefusedAsync(seth, $"UPDATE tenancy.\"Invitations\" SET \"State\" = 'Accepted', \"AcceptedAt\" = now(), \"AcceptedAs\" = '{Seth.Seat.Value}', \"Address\" = NULL WHERE \"Id\" = '{id}'");
            await RefusedAsync(seth, $"UPDATE tenancy.\"Invitations\" SET \"AcceptedAs\" = '{Seth.Seat.Value}' WHERE \"Id\" = '{id}'");
            (await seth.AttemptAsync("DELETE FROM tenancy.\"Invitations\"", Cancellation)).Should().Be(0, "no seat removes an invitation");

            (await seth.AttemptAsync($"UPDATE tenancy.\"Invitations\" SET \"State\" = 'Cancelled', \"ClosedAt\" = now(), \"Address\" = NULL, \"DisplayName\" = NULL WHERE \"Id\" = '{id}'", Cancellation))
                .Should().Be(1, "whoever manages seats at its unit cancels it");
            (await seth.AttemptAsync($"UPDATE tenancy.\"Invitations\" SET \"ClosedAt\" = NULL WHERE \"Id\" = '{id}'", Cancellation)).Should().Be(0, "and once it is over, a seat changes nothing of it");
        }

        // Cancelled through the use case, it stays cancelled, for the tables' owner too.
        await services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => scoped.Invitations().CancelAsync(issued.Id, Cancellation));
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        await FixedAsync(owner, $"UPDATE tenancy.\"Invitations\" SET \"State\" = 'Open' WHERE \"Id\" = '{id}'");
        await FixedAsync(owner, $"UPDATE tenancy.\"Invitations\" SET \"State\" = 'Accepted' WHERE \"Id\" = '{id}'");
    }

    [Fact]
    public async Task What_an_invitation_offers_cannot_change_for_any_role()
    {
        var database = await DatabaseAsync();
        await using var services = new TenancyServices(database);
        var open = await IssueAsync(services, NorthPier);
        var taken = await IssueAsync(services, North, address: "lark@example.test");
        var accepted = await services.BySignedInUser(Wren, scoped => scoped.Invitations().AcceptAsync(taken.Token, "Wren", null, Cancellation));

        string[] changes =
        [
            $"\"UnitId\" = '{South.Value}'",
            $"\"RoleId\" = '{HarborRoles.Administrator.Value}'",
            "\"GrantUntil\" = now() + interval '90 days'",
            "\"IssuedAt\" = now() - interval '1 day'",
            "\"ExpiresAt\" = now() + interval '300 days'",
            "\"IssuedBy\" = NULL",
            "\"IssuedAsSystem\" = true",
            "\"TenantId\" = 2",
            "\"Address\" = 'somebody.else@example.test'",
        ];

        // The tables' owner, whom no policy holds; Tenancy's own system work; and the seat that issued it.
        var callers = new Func<Task<AsCaller>>[]
        {
            () => AsCaller.OwnerAsync(database, Cancellation),
            () => AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation),
            () => AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation),
        };
        foreach (var begin in callers)
        {
            await using var caller = await begin();
            foreach (var change in changes)
            {
                await FixedAsync(caller, $"UPDATE tenancy.\"Invitations\" SET {change} WHERE \"Id\" = '{open.Id.Value}'");
            }

            // A statement that changes none of it is no change.
            (await caller.AttemptAsync($"UPDATE tenancy.\"Invitations\" SET \"UnitId\" = \"UnitId\", \"ExpiresAt\" = \"ExpiresAt\" WHERE \"Id\" = '{open.Id.Value}'", Cancellation)).Should().Be(1);
        }

        // One that was accepted keeps the seat it made, and stays accepted.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        await FixedAsync(owner, $"UPDATE tenancy.\"Invitations\" SET \"AcceptedAs\" = '{Ada.Seat.Value}' WHERE \"Id\" = '{taken.Id.Value}'");
        await FixedAsync(owner, $"UPDATE tenancy.\"Invitations\" SET \"State\" = 'Open' WHERE \"Id\" = '{taken.Id.Value}'");
        (await owner.ScalarAsync<Guid>($"SELECT \"AcceptedAs\" FROM tenancy.\"Invitations\" WHERE \"Id\" = '{taken.Id.Value}'", Cancellation)).Should().Be(accepted.Seat.Value);
    }

    [Fact]
    public async Task A_digest_is_added_only_to_an_invitation_the_same_transaction_wrote()
    {
        var database = await DatabaseAsync();
        await using var services = new TenancyServices(database);
        var issued = await IssueAsync(services, North);

        // Its digest taken away by the tables' owner, the invitation is one whose token nobody holds.
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ExecuteAsync("DELETE FROM tenancy.\"InvitationDigests\"", Cancellation)).Should().Be(1);
            await owner.CommitAsync(Cancellation);
        }

        // Nobody gives an invitation that was there already a token of their own: not its issuer, and not system work.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            await RefusedAsync(ada, Keep(issued.Id.Value, 1));

            // With an invitation of her own, in the transaction that adds it, she adds its digest: what issuing does.
            var own = Guid.NewGuid();
            (await ada.AttemptAsync(Insert(own, 1, North, HarborRoles.Watcher, Ada.Seat), Cancellation)).Should().Be(1);
            await RefusedAsync(ada, Keep(own, 2));
            (await ada.AttemptAsync(Keep(own, 1), Cancellation)).Should().Be(1);
            (await ada.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"InvitationDigests\"", Cancellation)).Should().Be(0, "and does not read it back");
        }

        await using (var tenancys = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            await RefusedAsync(tenancys, Keep(issued.Id.Value, 1));

            var own = Guid.NewGuid();
            (await tenancys.AttemptAsync(Insert(own, 1, North, HarborRoles.Watcher, by: null, asSystem: true), Cancellation)).Should().Be(1);
            (await tenancys.AttemptAsync(Keep(own, 1), Cancellation)).Should().Be(1);
        }

        // A module's system work adds neither.
        await using var modules = await AsCaller.SystemInAsync(database, Harbor, "widgets", Cancellation);
        await RefusedAsync(modules, Insert(Guid.NewGuid(), 1, North, HarborRoles.Watcher, by: null, asSystem: true));
    }

    [Fact]
    public async Task An_invitation_whose_time_ran_out_is_refused_and_nothing_is_written()
    {
        // Whether an invitation is still open is the application's clock against the row, with no SQL in it: the
        // one case where a test on Postgres moves the application's clock rather than the data, which the trigger
        // that keeps an invitation's end would refuse to move.
        var database = await DatabaseAsync();
        var clock = new MovedClock(await DatabaseNowAsync(database.ConnectionString, Cancellation));
        await using var services = new TenancyServices(database, configure: collection => collection.AddSingleton<TimeProvider>(clock));
        var issued = await IssueAsync(services, North);

        clock.Advance(TimeSpan.FromDays(7));
        await RefusedWithAsync(TenancyRefusals.InvitationLapsed,
            () => services.BySignedInUser(Wren, scoped => scoped.Invitations().AcceptAsync(issued.Token, "Wren", null, Cancellation)));
        (await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Invitations().ListOpenAsync(Cancellation))).Should().BeEmpty();

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Seats\" WHERE \"Identity\" = $1", Cancellation, Wren)).Should().Be(0);
        (await owner.ScalarAsync<string>("SELECT \"State\" FROM tenancy.\"Invitations\"", Cancellation)).Should().Be(names.Stored(InvitationState.Open), "nothing marks it: it is the clock that says it is over");
    }

    [Fact]
    public async Task An_invitation_whose_issuer_may_no_longer_give_it_is_refused_and_nothing_is_written()
    {
        // Eve manages seats and grants for the whole tenant, and holds no other key that manages access. What she may
        // give is asked again when her invitation is used: as her, while the work and its connection are system
        // work's in the tenant, which reads every seat's rights there. Hers alone are to count, and not Ada's, who
        // holds every key.
        var database = await DatabaseAsync();
        await HoldAtAsync(database, Eve, HarborRoot, [TenancyKeys.SeatsManage, TenancyKeys.GrantsManage], Cancellation);
        await using var services = new TenancyServices(database);

        Task<HostTenancy.IssuedInvitation<InvitationId>> ByEve(string address)
            => services.BySeat(Eve.Identity, Harbor, Eve.Seat, scoped =>
                scoped.Invitations().IssueAsync(address, North, HarborRoles.Watcher, grantUntil: null, "Wren", lifetime: null, Cancellation));
        Task<HostTenancy.AcceptedInvitation> Accept(Guid identity, string token)
            => services.BySignedInUser(identity, scoped => scoped.Invitations().AcceptAsync(token, "Wren", verifiedAddress: null, Cancellation));

        var first = await ByEve(Address);
        var second = await ByEve("lark@example.test");

        // The role she offered comes to manage access, by a key she does not hold.
        await services.BySystemIn(Harbor, scoped => scoped.Roles().SetKeysAsync(HarborRoles.Watcher, [HostCatalogue.WidgetRead, TenancyKeys.UnitsManage], Cancellation));
        await RefusedWithAsync(TenancyRefusals.InvitationUnbacked, () => Accept(Wren, first.Token));

        // The role as it was again, her invitation is good: it was what she holds that refused it.
        await services.BySystemIn(Harbor, scoped => scoped.Roles().SetKeysAsync(HarborRoles.Watcher, [HostCatalogue.WidgetRead], Cancellation));
        (await Accept(Wren, first.Token)).Tenant.Should().Be(Harbor);

        // Her desk taken away, the other invitation gives nothing, whoever else in the tenant could still give it.
        var desk = await services.BySystemIn(Harbor, async scoped =>
            (await scoped.GetRequiredService<HostTenancy.IStore>().FindSeatAsync(Eve.Seat, Cancellation))!
                .Placements.Single(placement => placement.UnitId == HarborRoot).Grants.Single().RoleId);
        await services.BySystemIn(Harbor, scoped => scoped.Seats().RevokeAsync(Eve.Seat, HarborRoot, desk, Cancellation));

        var lark = Guid.NewGuid();
        await RefusedWithAsync(TenancyRefusals.InvitationUnbacked, () => Accept(lark, second.Token));

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Seats\" WHERE \"Identity\" = $1", Cancellation, lark)).Should().Be(0);
        (await owner.ScalarAsync<string>("SELECT \"State\" FROM tenancy.\"Invitations\" WHERE \"Id\" = $1", Cancellation, second.Id.Value))
            .Should().Be(names.Stored(InvitationState.Open), "a refused acceptance leaves the invitation as it was");
    }

    [Fact]
    public async Task The_start_up_checks_cover_the_invitations()
    {
        var database = await DatabaseAsync();
        await using var services = new TenancyServices(database);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);
        await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services.Provider, Cancellation);
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(services.Provider, Cancellation);

        // The table of the digests left open, or the function that reads it gone: each is named.
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            await owner.ExecuteAsync("ALTER TABLE tenancy.\"InvitationDigests\" DISABLE ROW LEVEL SECURITY", Cancellation);
            await owner.CommitAsync(Cancellation);
        }

        (await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"Row level security is off on Tenancy's tables tenancy.{names.Shown("InvitationDigests")}*");

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            await owner.ExecuteAsync("ALTER TABLE tenancy.\"InvitationDigests\" ENABLE ROW LEVEL SECURITY", Cancellation);
            await owner.CommitAsync(Cancellation);
        }

        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "DROP FUNCTION tenancy.invitation_of_digest(bytea)", Cancellation);
        (await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("The functions of tenancy that read across tenants are not as Tenancy's contribution writes them: invitation_of_digest (missing)*");
        (await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services.Provider, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Tenancy's reads across tenants would not answer as they should: - invitation_of_digest is missing:*");

        // And an acceptance then fails, rather than find nothing in silence.
        await FluentActions.Awaiting(() => services.BySignedInUser(Wren, scoped => scoped.Invitations().AcceptAsync(BearerTokens.New().Token, "Wren", null, Cancellation)))
            .Should().ThrowAsync<PostgresException>();
    }

    /// <summary>A clock a test moves on: for the one rule of invitations that is the application's clock alone.</summary>
    private sealed class MovedClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

/// <summary>Invitations under the policies, under the names Entity Framework gives the tables and columns.</summary>
public sealed class InvitationPolicyTestsOnDefaultNames(TenancyPostgres postgres) : InvitationPolicyTests(postgres, TenancyNaming.Default);

/// <summary>Invitations under the policies, under snake_case names with enums stored as snake_case text.</summary>
public sealed class InvitationPolicyTestsOnSnakeCase(TenancyPostgres postgres) : InvitationPolicyTests(postgres, TenancyNaming.SnakeCase);
