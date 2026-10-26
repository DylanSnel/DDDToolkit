using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Containment in the database, with every policy forced on the tables' owner. The export writes the catalogue's
/// setting into <c>key_is_contained</c>, which the policies on the grants and the invitations and the trigger on a
/// seat's status ask: on by default, a seat gives, changes, takes away, offers and stops, by a query of its own as
/// through the use cases, only what it holds the keys that manage access for; written with containment off, a role
/// that manages access goes as one that manages none, as the use cases then let it. Either way the database keeps a
/// tenant's administrator and a seat's identity, and system work in a tenant, a handler that checked a quiz of the
/// application's own say, gives what it gives.
/// </summary>
/// <remarks>
/// Hiro holds Harbor's Grants desk, <c>tenancy.grants.manage</c> alone, at North. Seth is a Supervisor at North,
/// which manages units, seats and grants there. Oli is placed at North Pier, below North.
/// </remarks>
public sealed class ContainmentTests(TenancyPostgres postgres)
{
    private const string Give =
        """
        INSERT INTO tenancy."SeatRoleGrants" ("SeatId", "UnitId", "RoleId", "StartsAt", "EndsAt", "GrantedBy", "Reason")
        VALUES ($1, $2, $3, now(), NULL, $4, NULL)
        """;

    private const string TakeAway = "DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = $1 AND \"UnitId\" = $2 AND \"RoleId\" = $3";

    private const string Shorten =
        "UPDATE tenancy.\"SeatRoleGrants\" SET \"EndsAt\" = now() + interval '1 day' WHERE \"SeatId\" = $1 AND \"UnitId\" = $2 AND \"RoleId\" = $3";

    private const string Invite =
        """
        INSERT INTO tenancy."Invitations" ("Id", "TenantId", "Address", "UnitId", "RoleId", "State", "IssuedAt", "ExpiresAt", "IssuedBy", "IssuedAsSystem", "Version")
        VALUES ($1, $2, 'lark@example.test', $3, $4, 'Open', now(), now() + interval '7 days', $5, false, 0)
        """;

    private const string Suspend = "UPDATE tenancy.\"Seats\" SET \"Status\" = 'Suspended' WHERE \"Id\" = $1";

    /// <summary>The application's part of the catalogue with containment turned off.</summary>
    private static readonly ApplicationCatalogue Off = HostCatalogue.Application with { ContainAccessManagingKeys = false };

    /// <summary>A person with no seat anywhere, who accepts an invitation.</summary>
    private static readonly Guid Wren = Guid.Parse("d0000000-0000-4000-8000-000000000096");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_containment_on_a_grants_manager_gives_neither_anyone_else_nor_itself_a_role_whose_keys_that_manage_access_it_lacks()
    {
        var database = await ForcedAsync(contained: true);

        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            var toOli = () => hiro.AttemptAsync(Give, Cancellation, Oli.Seat.Value, NorthPier.Value, HarborRoles.Supervisor.Value, Hiro.Seat.Value);
            (await toOli.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            var toHimself = () => hiro.AttemptAsync(Give, Cancellation, Hiro.Seat.Value, North.Value, HarborRoles.Supervisor.Value, Hiro.Seat.Value);
            (await toHimself.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

            // Seth's Supervisor grant at North is not his to change or take away: the policy hides it from the statement.
            (await hiro.ExecuteAsync(Shorten, Cancellation, Seth.Seat.Value, North.Value, HarborRoles.Supervisor.Value)).Should().Be(0);
            (await hiro.ExecuteAsync(TakeAway, Cancellation, Seth.Seat.Value, North.Value, HarborRoles.Supervisor.Value)).Should().Be(0);
        }

        // The use cases refuse it first, in their own words.
        await using var services = new TenancyServices(database);
        await FluentActions.Awaiting(() => services.BySeat(Hiro.Identity, Harbor, Hiro.Seat, scoped => scoped.Seats().GrantAsync(Oli.Seat, NorthPier, HarborRoles.Supervisor, null, null, Cancellation)))
            .Should().ThrowAsync<RefusalException>().Where(refusal => refusal.Code == TenancyRefusals.GrantExceedsOwn);
    }

    [Fact]
    public async Task With_containment_off_the_export_lets_a_grants_manager_give_and_take_away_every_role_where_it_manages_grants()
    {
        var database = await ForcedAsync(contained: false);

        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            (await hiro.ExecuteAsync(Give, Cancellation, Oli.Seat.Value, NorthPier.Value, HarborRoles.Supervisor.Value, Hiro.Seat.Value)).Should().Be(1);
            (await hiro.ExecuteAsync(Shorten, Cancellation, Seth.Seat.Value, North.Value, HarborRoles.Supervisor.Value)).Should().Be(1);
            (await hiro.ExecuteAsync(TakeAway, Cancellation, Seth.Seat.Value, North.Value, HarborRoles.Supervisor.Value)).Should().Be(1);
            await hiro.CommitAsync(Cancellation);
        }

        // Through the use cases of an application that runs with it off, he gives himself the Supervisor at North.
        await using (var services = new TenancyServices(database, catalogue: Off))
        {
            await services.BySeat(Hiro.Identity, Harbor, Hiro.Seat, scoped => scoped.Seats().GrantAsync(Hiro.Seat, North, HarborRoles.Supervisor, null, null, Cancellation));
        }

        // Read as Tenancy's system work in Harbor, which reads every seat's rights there: the login role owns nothing here.
        await using var system = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation);
        (await system.ScalarAsync<long>(
                "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"Key\" = 'tenancy.units.manage'",
                Cancellation,
                Hiro.Seat.Value))
            .Should().Be(1, "with a key that manages access he gave himself what that key reaches");
        (await system.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"Key\" = 'tenancy.units.manage'", Cancellation, Oli.Seat.Value))
            .Should().Be(1);
        (await system.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, Seth.Seat.Value, HarborRoles.Supervisor.Value))
            .Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_seats_manager_stops_a_seat_whose_roles_manage_access_it_does_not_hold_by_a_query_only_with_containment_off(bool contained)
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await HoldAtAsync(database, Eve, HarborRoot, [TenancyKeys.SeatsManage], Cancellation);
        await TenancyPostgres.ForceAsync(database, Cancellation, catalogue: Catalogue(contained));

        // Eve manages seats for the whole tenant, and holds none of the Supervisor's other keys.
        await using var eve = await AsCaller.PersonAsync(database, Eve.Identity, Harbor, Cancellation);
        if (contained)
        {
            var refused = (await FluentActions.Awaiting(() => eve.ExecuteAsync(Suspend, Cancellation, Seth.Seat.Value)).Should().ThrowAsync<PostgresException>()).Which;
            (refused.SqlState, refused.ConstraintName).Should().Be((PostgresErrorCodes.InsufficientPrivilege, "tenancy_seat_status_is_managed"));
            return;
        }

        (await eve.ExecuteAsync(Suspend, Cancellation, Seth.Seat.Value)).Should().Be(1);
        await eve.CommitAsync(Cancellation);

        // The seats key for the whole tenant is asked either way: Hiro, who may write Oli's row, may not stop him.
        await using var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation);
        var stopping = (await FluentActions.Awaiting(() => hiro.ExecuteAsync(Suspend, Cancellation, Oli.Seat.Value)).Should().ThrowAsync<PostgresException>()).Which;
        stopping.ConstraintName.Should().Be("tenancy_seat_status_is_managed");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_invitation_offers_a_role_that_manages_access_its_issuer_does_not_hold_by_a_query_or_the_use_case_only_with_containment_off(bool contained)
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await HoldAtAsync(database, Eve, HarborRoot, [TenancyKeys.SeatsManage, TenancyKeys.GrantsManage], Cancellation);
        await TenancyPostgres.ForceAsync(database, Cancellation, catalogue: Catalogue(contained));

        // Eve manages seats and grants for the whole tenant, and does not hold the Supervisor's tenancy.units.manage.
        await using (var eve = await AsCaller.PersonAsync(database, Eve.Identity, Harbor, Cancellation))
        {
            var offering = () => eve.AttemptAsync(Invite, Cancellation, Guid.NewGuid(), Harbor.Value, North.Value, HarborRoles.Supervisor.Value, Eve.Seat.Value);
            if (contained)
            {
                (await offering.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            }
            else
            {
                (await offering()).Should().Be(1);
            }
        }

        // Through the use cases of an application that runs with the same setting, issued and accepted.
        await using var services = new TenancyServices(database, catalogue: contained ? null : Off);
        var issuing = () => services.BySeat(Eve.Identity, Harbor, Eve.Seat, scoped => scoped.Invitations().IssueAsync("wren@example.test", North, HarborRoles.Supervisor, grantUntil: null, lifetime: null, Cancellation));
        if (contained)
        {
            await issuing.Should().ThrowAsync<RefusalException>().Where(refusal => refusal.Code == TenancyRefusals.GrantExceedsOwn);
            return;
        }

        var issued = await issuing();
        var accepted = await services.BySignedInUser(Wren, scoped => scoped.Invitations().AcceptAsync(issued.Token, verifiedAddress: null, Cancellation, configure: seat => seat.Rename("Wren")));

        await using var system = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation);
        (await system.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"Key\" = 'tenancy.units.manage' AND \"UnitId\" = $2", Cancellation, accepted.Seat.Value, North.Value))
            .Should().Be(1, "accepting asks the issuer again, by the same setting, and the database lets the grant through");
    }

    [Fact]
    public async Task With_containment_off_the_database_still_keeps_a_tenants_administrator_and_what_a_seat_is()
    {
        var database = await ForcedAsync(contained: false);

        // Ada takes away her own administrators' role, which a seat may: the last administrator stays, at commit.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ExecuteAsync(TakeAway, Cancellation, Ada.Seat.Value, HarborRoot.Value, HarborRoles.Administrator.Value)).Should().Be(1);
            var committing = (await FluentActions.Awaiting(() => ada.CommitAsync(Cancellation)).Should().ThrowAsync<PostgresException>()).Which;
            (committing.SqlState, committing.ConstraintName).Should().Be((PostgresErrorCodes.CheckViolation, "tenancy_administrator_remains"));
        }

        // Hiro may write Oli's row, and hands it to no other account.
        await using var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation);
        var linking = (await FluentActions.Awaiting(() => hiro.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Identity\" = gen_random_uuid() WHERE \"Id\" = $1", Cancellation, Oli.Seat.Value))
            .Should().ThrowAsync<PostgresException>()).Which;
        (linking.SqlState, linking.ConstraintName).Should().Be((PostgresErrorCodes.InsufficientPrivilege, "tenancy_seat_identity_is_fixed"));
    }

    [Fact]
    public async Task A_handler_that_checked_a_quiz_itself_gives_a_role_that_manages_access_as_system_work_with_containment_on()
    {
        var database = await ForcedAsync(contained: true);
        await using var services = new TenancyServices(database);

        // Hiro gives himself nothing that manages access.
        await FluentActions.Awaiting(() => services.BySeat(Hiro.Identity, Harbor, Hiro.Seat, scoped => scoped.Seats().GrantAsync(Hiro.Seat, North, HarborRoles.Supervisor, null, null, Cancellation)))
            .Should().ThrowAsync<RefusalException>().Where(refusal => refusal.Code == TenancyRefusals.SelfAppointment);

        // He passed the supervisors' quiz, which the application checked itself: its handler, which takes his seat
        // from the caller and the role and the unit from the quiz, never from the request, gives him the role as
        // Tenancy's system work in Harbor, for his seat, which the policies let write the tenant's grants.
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor, Hiro.Seat))
        {
            await services.InScopeAsync(scoped => scoped.Seats().GrantAsync(Hiro.Seat, North, HarborRoles.Supervisor, until: null, "passed the supervisors' quiz", Cancellation));
        }

        await using var system = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation);
        (await system.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"Key\" = 'tenancy.units.manage' AND \"UnitId\" = $2", Cancellation, Hiro.Seat.Value, North.Value))
            .Should().Be(1);
    }

    /// <summary>The TestHost's catalogue, with containment as asked.</summary>
    private static TenancyCatalogue Catalogue(bool contained) => TenancyCatalogue.Build(contained ? HostCatalogue.Application : Off, []);

    /// <summary>A copy of the secured template, its access files written again from the catalogue with containment as asked, forced.</summary>
    private async Task<TestDatabase> ForcedAsync(bool contained)
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await TenancyPostgres.ForceAsync(database, Cancellation, catalogue: Catalogue(contained));
        return database;
    }
}
