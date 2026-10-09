using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The database keeps the rights: a trigger writes them as a grant, a seat or a role is written, in the same
/// statement, so they are what the use cases' own projection says after every command, whoever wrote and however; it
/// writes only what differs; a key the catalogue has retired gets none; no caller writes a right; and Tenancy's own
/// system work writes a tenant's rights again where rows got past the trigger.
/// </summary>
public abstract class DatabaseKeepsRightsTests(TenancyPostgres postgres, TenancyNaming names)
{
    /// <summary>Every right there is, with the transaction that last wrote the row: a row written again shows.</summary>
    private const string EveryRight =
        "SELECT \"SeatId\" || ' ' || \"UnitId\" || ' ' || \"RoleId\" || ' ' || \"Key\" || ' ' || \"StartsAt\" || ' ' || coalesce(\"EndsAt\"::text, 'no end') || ' ' || xmin::text FROM tenancy.\"SeatRights\" ORDER BY 1";

    /// <summary>A grant of a role to a seat at a unit, for good, written by a query.</summary>
    private const string GivingHiroAtNorth =
        "INSERT INTO tenancy.\"SeatRoleGrants\" (\"RoleId\", \"SeatId\", \"UnitId\", \"StartsAt\", \"EndsAt\", \"GrantedBy\", \"Reason\") VALUES ($1, $2, $3, now(), NULL, NULL, NULL)";

    /// <summary>The keys a seat holds by a role, in order.</summary>
    private const string HirosKeysBy = "SELECT \"Key\" FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2 ORDER BY 1";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task After_every_use_case_the_rights_are_what_the_projection_says()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        await EveryUseCase.RunAsync(services, Cancellation);

        // Read as system work in each tenant, which reads every right of it: what is stored is what the projection
        // works out from the seats and the roles as they are now.
        var catalogue = services.Provider.GetRequiredService<TenancyCatalogue>();
        var compared = 0;
        foreach (var tenant in new[] { Harbor, Orchard, Quay, EveryUseCase.Estuary })
        {
            var (stored, expected) = await services.BySystemIn(tenant, async scoped =>
            {
                var seats = await scoped.Tenancy().Set<HostSeat>().AsNoTracking().ToListAsync(Cancellation);
                var roles = await scoped.Tenancy().Set<HostRole>().AsNoTracking().ToDictionaryAsync(role => role.Id, Cancellation);
                var rights = await scoped.Tenancy().Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>().AsNoTracking().ToListAsync(Cancellation);

                var projected = seats.SelectMany(seat => TenancyProjection.RightsOf(
                    seat.TenantId,
                    seat.Id,
                    seat.Status,
                    TenancyProjection.GrantsOf(seat),
                    role => roles.TryGetValue(role, out var found) && found.TenantId == seat.TenantId ? found.Facts : null,
                    catalogue));
                return (rights.Select(Described).ToList(), projected.Select(Described).ToList());
            });

            stored.Should().BeEquivalentTo(expected, "the rights of tenant {0} are what its grants, seats and roles give", tenant);
            compared += stored.Count;
        }

        compared.Should().BeGreaterThan(20, "there was something to compare");

        // And the trigger that backs every right with a grant agrees, asked of every row at once.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ExecuteAsync("UPDATE tenancy.\"SeatRights\" SET \"Key\" = \"Key\"", Cancellation)).Should().Be(compared, "every tenant was compared");
        await owner.CommitAsync(Cancellation);
    }

    [Fact]
    public async Task Giving_a_grant_writes_its_rights_before_the_next_statement()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Hiro gives himself the Watcher role at North, by a query: the statement after it, in the same transaction,
        // reads the right and is answered by it.
        await using (var hiro = await AsCaller.PersonAsync(database, Hiro.Identity, Harbor, Cancellation))
        {
            (await hiro.ScalarAsync<bool>("SELECT tenancy.holds_key('widget.read')", Cancellation)).Should().BeFalse();
            (await hiro.ExecuteAsync(
                    "INSERT INTO tenancy.\"SeatRoleGrants\" (\"RoleId\", \"SeatId\", \"UnitId\", \"StartsAt\", \"EndsAt\", \"GrantedBy\", \"Reason\") VALUES ($1, $2, $3, now(), now() + interval '2 days', $2, NULL)",
                    Cancellation,
                    HarborRoles.Watcher.Value,
                    Hiro.Seat.Value,
                    North.Value))
                .Should().Be(1);

            (await hiro.ListAsync<string>("SELECT \"Key\" || ' ' || (\"EndsAt\" - \"StartsAt\") FROM tenancy.\"SeatRights\" WHERE \"RoleId\" = $1", Cancellation, HarborRoles.Watcher.Value))
                .Should().Equal("widget.read 2 days");
            (await hiro.ScalarAsync<bool>("SELECT tenancy.holds_key('widget.read')", Cancellation)).Should().BeTrue();
            (await hiro.ListAsync<Guid>("SELECT tenancy.units_where_i_hold('widget.read')", Cancellation)).Should().BeEquivalentTo([North.Value, NorthPier.Value]);

            // The grant's period changes, and the right's with it.
            (await hiro.ExecuteAsync("UPDATE tenancy.\"SeatRoleGrants\" SET \"EndsAt\" = NULL WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, Hiro.Seat.Value, HarborRoles.Watcher.Value)).Should().Be(1);
            (await hiro.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"RoleId\" = $1 AND \"EndsAt\" IS NULL", Cancellation, HarborRoles.Watcher.Value)).Should().Be(1);
            await hiro.CommitAsync(Cancellation);
        }

        // Through the use case it is the same: the command's save leaves the rights written.
        await using var services = new TenancyServices(database);
        await services.BySeat(Hiro.Identity, Harbor, Hiro.Seat, scoped => scoped.Seats().GrantAsync(Oli.Seat, NorthPier, HarborRoles.Watcher, until: null, reason: null, Cancellation));
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ListAsync<string>("SELECT \"Key\" FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, Oli.Seat.Value, HarborRoles.Watcher.Value))
            .Should().Equal("widget.read");
    }

    [Fact]
    public async Task Revoking_a_grant_removes_its_rights()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);
        var before = await RightsAsync(database);

        await services.BySeat(Hiro.Identity, Harbor, Hiro.Seat, scoped => scoped.Seats().RevokeAsync(Oli.Seat, NorthPier, HarborRoles.Operator, Cancellation));

        var after = await RightsAsync(database);
        after.Should().NotContain(right => right.StartsWith(Oli.Seat.Value.ToString(), StringComparison.Ordinal));
        after.Should().BeEquivalentTo(before.Where(right => !right.StartsWith(Oli.Seat.Value.ToString(), StringComparison.Ordinal)), "every other right is the row it was");
        before.Should().HaveCount(after.Count + 3);
    }

    [Fact]
    public async Task Suspending_a_seat_removes_its_rights_and_reactivating_restores_them()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);
        var before = await RightsAsync(database, withTransaction: false);
        before.Should().Contain(right => right.StartsWith(Seth.Seat.Value.ToString(), StringComparison.Ordinal));

        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Seats().SuspendAsync(Seth.Seat, Cancellation));
        (await RightsAsync(database, withTransaction: false)).Should().BeEquivalentTo(before.Where(right => !right.StartsWith(Seth.Seat.Value.ToString(), StringComparison.Ordinal)));

        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Seats().ReactivateAsync(Seth.Seat, Cancellation));
        (await RightsAsync(database, withTransaction: false)).Should().BeEquivalentTo(before, "each right is back, with its unit, its role, its key and its period");
    }

    [Fact]
    public async Task Changing_a_roles_keys_rewrites_every_holders_rights_and_nothing_else()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);
        var before = await RightsAsync(database);
        bool OfTheRole(string right) => right.Contains(HarborRoles.Operator.Value.ToString(), StringComparison.Ordinal);

        // The Operator role of Harbor: Oli holds it at North Pier, Eve from the day after tomorrow at South, and Sue,
        // who is suspended, holds nothing by it. It loses two keys and keeps one.
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Roles().SetKeysAsync(HarborRoles.Operator, [HostCatalogue.WidgetRead], Cancellation));

        var after = await RightsAsync(database);
        after.Where(OfTheRole).Select(right => right.Split(' ')[0] + " " + right.Split(' ')[3])
            .Should().BeEquivalentTo([Oli.Seat.Value + " widget.read", Eve.Seat.Value + " widget.read"]);
        after.Where(right => !OfTheRole(right)).Should().BeEquivalentTo(before.Where(right => !OfTheRole(right)), "no row of another role was written, the same values again included");
        after.Where(OfTheRole).Should().BeSubsetOf(before, "and the rows the role keeps are the rows they were");

        // Archived, it gives nothing; in use again, what it gave.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ExecuteAsync("UPDATE tenancy.\"Roles\" SET \"Status\" = 'Archived' WHERE \"Id\" = $1", Cancellation, HarborRoles.Operator.Value)).Should().Be(1);
            await ada.CommitAsync(Cancellation);
        }

        (await RightsAsync(database)).Should().BeEquivalentTo(before.Where(right => !OfTheRole(right)));

        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ExecuteAsync("UPDATE tenancy.\"Roles\" SET \"Status\" = 'Active' WHERE \"Id\" = $1", Cancellation, HarborRoles.Operator.Value)).Should().Be(1);
            await ada.CommitAsync(Cancellation);
        }

        (await RightsAsync(database, withTransaction: false)).Should().BeEquivalentTo(WithoutTransaction(after));
    }

    [Fact]
    public async Task A_key_the_catalogue_retired_gets_no_rights()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        const string creating = "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"Key\" = 'widget.create'";

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ScalarAsync<long>(creating, Cancellation)).Should().BePositive();
            (await owner.ScalarAsync<bool>("SELECT tenancy.key_is_live('widget.create')", Cancellation)).Should().BeTrue();
            (await owner.ScalarAsync<bool>("SELECT tenancy.key_is_live('widget.polish')", Cancellation)).Should().BeFalse("a key the catalogue never had is not live either");
        }

        // While the key is live, every question answers for it: Oli operates widgets at North Pier, and Ada holds
        // every key at the root.
        (await AnswersForTheKeyAsync(Oli, Harbor)).Should().Equal("units_where_i_hold", "holds_key", "roles_with_key", "units_where_i_hold_in_tenant", "holds_key_in_tenant", "roles_with_key_in_tenant");
        (await AnswersForTheKeyAsync(Ada, Harbor)).Should().Contain("holds_tenant_wide");

        // The application retires the key: the next export writes the function that says which keys are live again,
        // and that access file is the migration. The roles keep the key, as they do in C#.
        var catalogue = HostCatalogue.Application with
        {
            Permissions = [.. HostCatalogue.Permissions.Select(permission => permission.Key == HostCatalogue.WidgetCreate ? permission with { Retired = true } : permission)],
            Packs = [.. HostCatalogue.Application.Packs.Select(pack => pack with { Keys = [.. pack.Keys.Where(key => key != HostCatalogue.WidgetCreate)] })],
        };
        foreach (var script in TenancyPostgres.AccessScripts(TenancyCatalogue.Build(catalogue, []), names: names))
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await using var services = new TenancyServices(database, catalogue: catalogue);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);

        // A role given from now on gives no right for it, though the role still holds it.
        await services.BySeat(Hiro.Identity, Harbor, Hiro.Seat, scoped => scoped.Seats().GrantAsync(Hiro.Seat, North, HarborRoles.Operator, until: null, reason: null, Cancellation));
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ScalarAsync<bool>("SELECT 'widget.create' = ANY (\"Keys\") FROM tenancy.\"Roles\" WHERE \"Id\" = $1", Cancellation, HarborRoles.Operator.Value)).Should().BeTrue();
            (await owner.ListAsync<string>("SELECT \"Key\" FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2 ORDER BY 1", Cancellation, Hiro.Seat.Value, HarborRoles.Operator.Value))
                .Should().Equal("widget.change", "widget.read");
            (await owner.ScalarAsync<long>(creating, Cancellation)).Should().BePositive("the rights written before stay until they are written again");
        }

        // No question answers for it meanwhile, of the caller's tenant or of a tenant given, though the rights rows
        // and the roles' keys still say it: a policy on a stored file or a channel has no C# in front of it.
        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ListAsync<Guid>("SELECT \"SeatId\" FROM tenancy.seats_holding_at('widget.create', $1)", Cancellation, NorthPier.Value)).Should().BeEmpty();
        }

        (await AnswersForTheKeyAsync(Oli, Harbor)).Should().BeEmpty();
        (await AnswersForTheKeyAsync(Ada, Harbor)).Should().BeEmpty();
        (await AnswersForTheKeyAsync(Odette, Orchard)).Should().BeEmpty();

        foreach (var tenant in new[] { Harbor, Orchard, Quay })
        {
            await using var system = await AsCaller.SystemInAsync(database, tenant, TenancyWork.SystemScope, Cancellation);
            await system.ScalarAsync<int>("SELECT tenancy.rewrite_tenant_rights()", Cancellation);
            await system.CommitAsync(Cancellation);
        }

        await using var after = await AsCaller.OwnerAsync(database, Cancellation);
        (await after.ScalarAsync<long>(creating, Cancellation)).Should().Be(0);

        // The questions about the caller that answer something for widget.create, asked as that person: in the tenant
        // the connection names, and with the tenant as an argument on a connection that names none.
        async Task<List<string>> AnswersForTheKeyAsync(Person person, TenantId tenant)
        {
            var answering = new List<string>();
            await using (var seated = await AsCaller.PersonAsync(database, person.Identity, tenant, Cancellation))
            {
                if ((await seated.ListAsync<Guid>("SELECT tenancy.units_where_i_hold('widget.create')", Cancellation)).Count > 0)
                {
                    answering.Add("units_where_i_hold");
                }

                foreach (var asked in new[] { "holds_key", "holds_tenant_wide" })
                {
                    if (await seated.ScalarAsync<bool>($"SELECT tenancy.{asked}('widget.create')", Cancellation))
                    {
                        answering.Add(asked);
                    }
                }

                if ((await seated.ListAsync<Guid>("SELECT tenancy.roles_with_key('widget.create')", Cancellation)).Count > 0)
                {
                    answering.Add("roles_with_key");
                }
            }

            await using var elsewhere = await AsCaller.PersonAsync(database, person.Identity, tenant: null, Cancellation);
            if ((await elsewhere.ListAsync<Guid>("SELECT tenancy.units_where_i_hold_in_tenant($1, 'widget.create')", Cancellation, tenant.Value)).Count > 0)
            {
                answering.Add("units_where_i_hold_in_tenant");
            }

            if (await elsewhere.ScalarAsync<bool>("SELECT tenancy.holds_key_in_tenant($1, 'widget.create')", Cancellation, tenant.Value))
            {
                answering.Add("holds_key_in_tenant");
            }

            if ((await elsewhere.ListAsync<Guid>("SELECT tenancy.roles_with_key_in_tenant($1, 'widget.create')", Cancellation, tenant.Value)).Count > 0)
            {
                answering.Add("roles_with_key_in_tenant");
            }

            return answering;
        }
    }

    [Fact]
    public async Task A_save_that_changes_nothing_about_access_writes_no_rights_row()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);
        var before = await RightsAsync(database);

        // Commands that change no grant, no seat's status and no role's keys.
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.RenameSeatAsync(Hiro.Seat, "Hiro B.", Cancellation));
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Roles().RenameAsync(HarborRoles.Operator, "Operators", "Work with widgets", Cancellation));
        await services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => scoped.Seats().PlaceAsync(Eve.Seat, North, primary: false, Cancellation));
        await services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => scoped.Organization().RenameUnitAsync(North, "North Coast", Cancellation));
        (await RightsAsync(database)).Should().Equal(before);

        // Statements that fire the trigger and change nothing it writes from: a grant's reason, and a seat's status, a
        // role's keys and a role's status set to what they were.
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ExecuteAsync("UPDATE tenancy.\"SeatRoleGrants\" SET \"Reason\" = 'Standing in'", Cancellation)).Should().BePositive();
            (await owner.ExecuteAsync("UPDATE tenancy.\"Seats\" SET \"Status\" = \"Status\"", Cancellation)).Should().BePositive();
            (await owner.ExecuteAsync("UPDATE tenancy.\"Roles\" SET \"Keys\" = \"Keys\", \"Status\" = \"Status\"", Cancellation)).Should().BePositive();
            await owner.CommitAsync(Cancellation);
        }

        (await RightsAsync(database)).Should().Equal(before, "a right that already says what it should is left as it is, so no trigger on the rights fires for it");
    }

    [Fact]
    public async Task A_seat_cannot_write_rights_by_a_query()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var before = await RightsAsync(database);

        // Ada holds every key of Harbor, Seth the grants and seats keys at North, Hiro gives roles there, Oli holds none
        // that manages access: none of them adds a right, to anyone, and none changes or removes one, its own included.
        foreach (var person in new[] { Ada, Seth, Hiro, Oli })
        {
            await using var caller = await AsCaller.PersonAsync(database, person.Identity, Harbor, Cancellation);
            foreach (var seat in new[] { person.Seat, Oli.Seat })
            {
                var adding = await FluentActions.Awaiting(() => caller.AttemptAsync(
                        "INSERT INTO tenancy.\"SeatRights\" (\"SeatId\", \"UnitId\", \"RoleId\", \"Key\", \"TenantId\", \"StartsAt\", \"EndsAt\") VALUES ($1, $2, $3, 'tenancy.roles.manage', 1, now(), NULL)",
                        Cancellation,
                        seat.Value,
                        NorthPier.Value,
                        HarborRoles.Operator.Value))
                    .Should().ThrowAsync<PostgresException>();
                adding.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, "{0} adds no right", person.Name);
            }

            (await caller.ExecuteAsync("UPDATE tenancy.\"SeatRights\" SET \"EndsAt\" = NULL, \"StartsAt\" = now() - interval '1 year'", Cancellation)).Should().Be(0, "{0} changes no right", person.Name);
            (await caller.ExecuteAsync("DELETE FROM tenancy.\"SeatRights\"", Cancellation)).Should().Be(0, "{0} removes no right", person.Name);
            await caller.CommitAsync(Cancellation);
        }

        (await RightsAsync(database)).Should().Equal(before);
    }

    [Fact]
    public async Task System_in_cannot_write_rights_by_a_query_either()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var before = await RightsAsync(database);

        // Tenancy's own system work in the tenant writes every other table of Tenancy's there, and no right.
        foreach (var scope in new[] { TenancyWork.SystemScope, "widgets" })
        {
            await using var system = await AsCaller.SystemInAsync(database, Harbor, scope, Cancellation);
            var adding = await FluentActions.Awaiting(() => system.AttemptAsync(
                    "INSERT INTO tenancy.\"SeatRights\" (\"SeatId\", \"UnitId\", \"RoleId\", \"Key\", \"TenantId\", \"StartsAt\", \"EndsAt\") VALUES ($1, $2, $3, 'widget.read', 1, now(), NULL)",
                    Cancellation,
                    Oli.Seat.Value,
                    South.Value,
                    HarborRoles.Watcher.Value))
                .Should().ThrowAsync<PostgresException>();
            adding.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

            (await system.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\"", Cancellation)).Should().BePositive("it reads them");
            (await system.ExecuteAsync("UPDATE tenancy.\"SeatRights\" SET \"EndsAt\" = now()", Cancellation)).Should().Be(0);
            (await system.ExecuteAsync("DELETE FROM tenancy.\"SeatRights\"", Cancellation)).Should().Be(0);
            await system.CommitAsync(Cancellation);
        }

        (await RightsAsync(database)).Should().Equal(before);
    }

    [Fact]
    public async Task Rewriting_a_tenants_rights_repairs_a_wrong_row_and_counts_it()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var before = await RightsAsync(database, withTransaction: false);
        await BreakHarborsRightsAsync(database);
        (await RightsAsync(database, withTransaction: false)).Should().NotBeEquivalentTo(before);

        // Tenancy's own system work in Harbor writes its rights again: the right that was missing, the one that was not
        // Harbor's to have, the one whose period was wrong, and the ones of a grant written past the trigger.
        await using (var harbor = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            (await harbor.ScalarAsync<int>("SELECT tenancy.rewrite_tenant_rights()", Cancellation)).Should().Be(4, "one added back, one removed, one corrected, one added for the grant");
            (await harbor.ScalarAsync<int>("SELECT tenancy.rewrite_tenant_rights()", Cancellation)).Should().Be(0, "and nothing is left to write");
            await harbor.CommitAsync(Cancellation);
        }

        var after = await RightsAsync(database, withTransaction: false);
        after.Should().HaveCount(before.Count + 1);
        after.Where(right => !right.Contains(HarborRoles.Watcher.Value.ToString(), StringComparison.Ordinal) || !right.StartsWith(Hiro.Seat.Value.ToString(), StringComparison.Ordinal))
            .Should().BeEquivalentTo(before, "every right is back as it was, next to the one of the grant");
    }

    [Fact]
    public async Task Rewriting_rights_answers_nothing_outside_tenancys_scope()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await BreakHarborsRightsAsync(database);
        var broken = await RightsAsync(database);

        // Another module's system work in Harbor, Tenancy's own outside any tenant, and Tenancy's own in another tenant:
        // each is answered zero, and Harbor's rights are as they were.
        foreach (var (tenant, scope) in new (TenantId?, string)[] { (Harbor, "widgets"), (null, TenancyWork.SystemScope), (Orchard, TenancyWork.SystemScope) })
        {
            await using var system = await AsCaller.SystemInAsync(database, tenant, scope, Cancellation);
            (await system.ScalarAsync<int>("SELECT tenancy.rewrite_tenant_rights()", Cancellation)).Should().Be(0);
            await system.CommitAsync(Cancellation);
        }

        (await RightsAsync(database)).Should().Equal(broken);

        // A signed-in user may not ask it at all, an administrator included, and neither may an anonymous caller.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await FluentActions.Awaiting(() => ada.ScalarAsync<int>("SELECT tenancy.rewrite_tenant_rights()", Cancellation)).Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        await using var anonymous = await AsCaller.AnonymousAsync(database, Cancellation);
        (await FluentActions.Awaiting(() => anonymous.ScalarAsync<int>("SELECT tenancy.rewrite_tenant_rights()", Cancellation)).Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_writers_at_once_past_the_use_cases_leave_no_right_its_role_does_not_give(bool theRoleCommitsFirst)
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // One transaction takes a key from the Operator role while another gives the role to Hiro at North. Neither sees
        // what the other wrote, so the grant's rights are written with the key the role is losing. The use cases never get
        // here, since each command waits for the one before it; two statements past them can.
        await using var changingTheRole = await AsCaller.OwnerAsync(database, Cancellation);
        await using var givingTheRole = await AsCaller.OwnerAsync(database, Cancellation);
        (await changingTheRole.ExecuteAsync("UPDATE tenancy.\"Roles\" SET \"Keys\" = array_remove(\"Keys\", 'widget.create') WHERE \"Id\" = $1", Cancellation, HarborRoles.Operator.Value)).Should().Be(1);
        (await givingTheRole.ExecuteAsync(GivingHiroAtNorth, Cancellation, HarborRoles.Operator.Value, Hiro.Seat.Value, North.Value)).Should().Be(1);
        (await givingTheRole.ListAsync<string>(HirosKeysBy, Cancellation, Hiro.Seat.Value, HarborRoles.Operator.Value)).Should().Contain("widget.create");

        // The check that every right comes from a grant runs when each commits and reads what the other committed: the
        // second one to commit is refused, whichever it is, and the right the role does not give is never stored.
        var (first, second) = theRoleCommitsFirst ? (changingTheRole, givingTheRole) : (givingTheRole, changingTheRole);
        await first.CommitAsync(Cancellation);
        var refused = await FluentActions.Awaiting(() => second.CommitAsync(Cancellation)).Should().ThrowAsync<PostgresException>();
        refused.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        refused.Which.ConstraintName.Should().Be("tenancy_rights_backed_by_grants");

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>(
                "SELECT count(*) FROM tenancy.\"SeatRights\" r JOIN tenancy.\"Roles\" ro ON ro.\"Id\" = r.\"RoleId\" WHERE NOT r.\"Key\" = ANY (ro.\"Keys\")",
                Cancellation))
            .Should().Be(0);
    }

    [Fact]
    public async Task A_right_two_writers_at_once_left_unwritten_is_added_by_rewriting_the_tenants_rights()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // The other way round: one transaction adds a key to the Watcher role while another gives the role to Hiro at
        // North. The grant's rights are written without the key, and nothing is refused, since every right stored has its
        // grant: Hiro holds a key less than his role gives.
        await using (var changingTheRole = await AsCaller.OwnerAsync(database, Cancellation))
        await using (var givingTheRole = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await changingTheRole.ExecuteAsync("UPDATE tenancy.\"Roles\" SET \"Keys\" = array_append(\"Keys\", 'widget.create') WHERE \"Id\" = $1", Cancellation, HarborRoles.Watcher.Value)).Should().Be(1);
            (await givingTheRole.ExecuteAsync(GivingHiroAtNorth, Cancellation, HarborRoles.Watcher.Value, Hiro.Seat.Value, North.Value)).Should().Be(1);
            await givingTheRole.CommitAsync(Cancellation);
            await changingTheRole.CommitAsync(Cancellation);
        }

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ListAsync<string>(HirosKeysBy, Cancellation, Hiro.Seat.Value, HarborRoles.Watcher.Value)).Should().Equal("widget.read");
        }

        // Writing the tenant's rights again adds it, and it alone.
        await using (var harbor = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            (await harbor.ScalarAsync<int>("SELECT tenancy.rewrite_tenant_rights()", Cancellation)).Should().Be(1);
            await harbor.CommitAsync(Cancellation);
        }

        await using var after = await AsCaller.OwnerAsync(database, Cancellation);
        (await after.ListAsync<string>(HirosKeysBy, Cancellation, Hiro.Seat.Value, HarborRoles.Watcher.Value)).Should().Equal("widget.create", "widget.read");
    }

    /// <summary>
    /// Makes Harbor's rights wrong past every trigger, as rows loaded straight into the tables would be: one of Ada's
    /// rights gone, a right Oli has no grant for, Seth's right to manage seats ending tomorrow, and a grant of the Watcher
    /// role to Hiro at North with no right written for it.
    /// </summary>
    private static async Task BreakHarborsRightsAsync(TestDatabase database)
    {
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        await owner.ExecuteAsync("ALTER TABLE tenancy.\"SeatRights\" DISABLE TRIGGER USER", Cancellation);
        await owner.ExecuteAsync("ALTER TABLE tenancy.\"SeatRoleGrants\" DISABLE TRIGGER USER", Cancellation);
        (await owner.ExecuteAsync("DELETE FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"Key\" = 'widget.create'", Cancellation, Ada.Seat.Value)).Should().Be(1);
        (await owner.ExecuteAsync(
                "INSERT INTO tenancy.\"SeatRights\" (\"SeatId\", \"UnitId\", \"RoleId\", \"Key\", \"TenantId\", \"StartsAt\", \"EndsAt\") VALUES ($1, $2, $3, 'tenancy.roles.manage', 1, now(), NULL)",
                Cancellation,
                Oli.Seat.Value,
                South.Value,
                HarborRoles.Administrator.Value))
            .Should().Be(1);
        (await owner.ExecuteAsync("UPDATE tenancy.\"SeatRights\" SET \"EndsAt\" = now() + interval '1 day' WHERE \"SeatId\" = $1 AND \"Key\" = 'tenancy.seats.manage'", Cancellation, Seth.Seat.Value)).Should().Be(1);
        (await owner.ExecuteAsync(
                "INSERT INTO tenancy.\"SeatRoleGrants\" (\"RoleId\", \"SeatId\", \"UnitId\", \"StartsAt\", \"EndsAt\", \"GrantedBy\", \"Reason\") VALUES ($1, $2, $3, now(), NULL, NULL, 'Loaded')",
                Cancellation,
                HarborRoles.Watcher.Value,
                Hiro.Seat.Value,
                North.Value))
            .Should().Be(1);
        await owner.ExecuteAsync("ALTER TABLE tenancy.\"SeatRights\" ENABLE TRIGGER USER", Cancellation);
        await owner.ExecuteAsync("ALTER TABLE tenancy.\"SeatRoleGrants\" ENABLE TRIGGER USER", Cancellation);
        await owner.CommitAsync(Cancellation);
    }

    /// <summary>Every right there is, as the owner reads it: with the transaction that last wrote each row, or without.</summary>
    private static async Task<List<string>> RightsAsync(TestDatabase database, bool withTransaction = true)
    {
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        var rights = await owner.ListAsync<string>(EveryRight, Cancellation);
        return withTransaction ? rights : WithoutTransaction(rights);
    }

    /// <summary>The rights without the transaction that last wrote each: what they say, whenever they were written.</summary>
    private static List<string> WithoutTransaction(IEnumerable<string> rights) => [.. rights.Select(right => right[..right.LastIndexOf(' ')])];

    private static string Described(SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId> right)
        => $"{right.TenantId} {right.SeatId} {right.UnitId} {right.RoleId} {right.Key} {right.StartsAt.UtcDateTime:O} {right.EndsAt?.UtcDateTime.ToString("O")}";
}

/// <summary>The database keeps the rights, under the names Entity Framework gives the tables and columns.</summary>
public sealed class DatabaseKeepsRightsTestsOnDefaultNames(TenancyPostgres postgres) : DatabaseKeepsRightsTests(postgres, TenancyNaming.Default);

/// <summary>The database keeps the rights, under snake_case names with enums stored as snake_case text.</summary>
public sealed class DatabaseKeepsRightsTestsOnSnakeCase(TenancyPostgres postgres) : DatabaseKeepsRightsTests(postgres, TenancyNaming.SnakeCase);
