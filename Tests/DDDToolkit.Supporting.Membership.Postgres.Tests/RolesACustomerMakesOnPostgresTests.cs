using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.TestHost.Access;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// A resource whose roles are kept, on Postgres: plots in gardens. The four functions of the plots read a
/// role's keys straight from the table of the host's role class, in the same context, and answer what the
/// access questions answer in C#, and what the rules say read over the aggregates in memory. No function of
/// the host's, or of anybody's, is asked for the roles.
/// </summary>
public sealed partial class RolesACustomerMakesOnPostgresTests(FilingPostgres postgres)
{
    private const string Gardens = GardenContext.Schema + ".";

    private static readonly RowAccessExport Export = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_functions_of_the_plots_answer_what_the_access_questions_answer_and_what_the_rules_say()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres);

        await AgreeForEverybodyAsync(garden);
    }

    [Fact]
    public async Task Two_kinds_of_resource_in_one_context_each_read_the_roles_kept_for_them()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres);
        var data = garden.Scenario;
        var functions = ShedMembership.Rules.Functions;

        // The sheds' functions, the questions about sheds, and the sheds' rules read in memory: for everybody and every key.
        foreach (var person in data.People)
        {
            await using var session = await garden.SessionAsync(person);

            // The sheds' rules name no key for seeing: a shed is seen by the hands in it, and that is all being a hand gives.
            var seen = data.ShedsSeenBy(person).Select(id => id.Value).ToList();
            (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.Seen}() AS id")).Should().BeEquivalentTo(seen, "of the sheds " + person + " sees");
            (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMember}() AS id")).Should().BeEquivalentTo(seen);

            foreach (var key in GardenScenario.ShedKeysAsked)
            {
                var heldInCSharp = await garden.Services.AsAsync(person, async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<ShedId>>().Reach(key);
                    return await provider.GetRequiredService<GardenContext>().Sheds.Within(reach).Select(shed => shed.Id.Value).ToListAsync(Cancellation);
                });
                var held = data.ShedsHeldBy(person, key).Select(id => id.Value).ToList();
                var because = "of the sheds " + person + " holds " + key + " on";

                (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", key)).Should().BeEquivalentTo(held, because);
                heldInCSharp.Should().BeEquivalentTo(held, because);
                (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", key))
                    .Should().BeEquivalentTo(data.ShedsWithARoleFor(person, key).Select(id => id.Value), because + " through a role");
            }
        }

        // Ben lends tools through the lender, a key of another module's, which the sheds' rules let a role give by not keeping it from one.
        await using var ben = await garden.SessionAsync(data.Ben);
        (await ben.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", ShedKeys.LendTools)).Should().Equal(data.ToolShed.Value);

        // Each resource's functions read its own role table, and each cuts by its own rules.
        using var context = PostgresGarden.Model();
        var sheds = new ShedMembershipFunctions().Contribute(context, Export)!.Functions[1].Body;
        var plots = new PlotMembershipFunctions().Contribute(context, Export)!.Functions[1].Body;
        sheds.Should().Contain("\n  AND $1 NOT IN ('sheds.sell')\n").And.Contain("JOIN \"gardens\".\"ShedRoles\" k ON k.\"Id\" = h.\"RoleId\"").And.NotContain("PlotRoles");
        plots.Should().Contain("JOIN \"gardens\".\"PlotRoles\" k ON k.\"Id\" = h.\"RoleId\"").And.NotContain("ShedRoles");
    }

    [Fact]
    public async Task What_a_garden_does_to_a_role_is_what_the_functions_answer_from_then_on()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres);
        var data = garden.Scenario;
        var functions = PlotMembership.Rules.Functions;

        // Eve fences the beans through the meadow's fencer, as the function for that key says.
        await using (var before = await garden.SessionAsync(data.Eve))
        {
            (await before.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", PlotKeys.Fence)).Should().Equal(data.Beans.Value);
        }

        // The meadow puts the fencer away. No access file is written anew: the functions read the role's row.
        await garden.Services.ChangeAsync(data.Fencer.Id, role => role.PutAway());
        data.Fencer.PutAway();
        await using (var after = await garden.SessionAsync(data.Eve))
        {
            (await after.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", PlotKeys.Fence)).Should().BeEmpty("an archived role gives nothing from then on");
            (await after.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotKeys.Fence)).Should().BeEmpty();
            (await after.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.Seen}() AS id")).Should().BeEquivalentTo([data.Beans.Value, data.Leeks.Value], "she is still on both plots");
        }

        await AgreeForEverybodyAsync(garden);

        // It has its tender fence instead of water: everybody who tends holds the one and no longer the other.
        await garden.Services.ChangeAsync(data.MeadowTender.Id, role => role.HaveItGive(PlotKeys.See, PlotKeys.Plant, PlotKeys.Fence));
        data.MeadowTender.HaveItGive(PlotKeys.See, PlotKeys.Plant, PlotKeys.Fence);
        await using (var ben = await garden.SessionAsync(data.Ben))
        {
            (await ben.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotKeys.Fence)).Should().BeEquivalentTo([data.Beans.Value, data.Leeks.Value]);
            (await ben.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotKeys.Water)).Should().Equal([data.Leeks.Value], "he waters the leeks through the owner's role, and the beans no more");
        }

        await AgreeForEverybodyAsync(garden);

        // A row from before the rules changed, which still holds a key no member's role can give now: the
        // functions cut what it gives as the rules say, as the questions do.
        string[] stale = [PlotKeys.Plant, PlotKeys.See, PlotKeys.Sell, PlotKeys.Water];
        await garden.Services.ChangeAsync(data.MeadowWaterer.Id, role => Break.Set(role, nameof(PlotRole.Keys), stale));
        Break.Set(data.MeadowWaterer, nameof(PlotRole.Keys), stale);
        await using (var ada = await garden.SessionAsync(data.Ada))
        {
            // Ada waters the leeks: planting came with the row, selling did not, and the beans are hers to sell by owning them.
            (await ada.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotKeys.Plant)).Should().BeEquivalentTo([data.Beans.Value, data.Leeks.Value]);
            (await ada.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotKeys.Sell)).Should().BeEmpty();
            (await ada.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", PlotKeys.Sell)).Should().Equal(data.Beans.Value);
        }

        await AgreeForEverybodyAsync(garden);
    }

    [Fact]
    public async Task The_functions_find_a_role_by_its_id_and_know_nothing_of_the_hosts_rule_about_whose_roles_a_request_reads()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres);
        var data = garden.Scenario;
        var functions = PlotMembership.Rules.Functions;

        // The orchard's tender on a plot of the meadow: a row the admission refuses in the meadow, given here
        // by the application's own work, which did not ask it. A caller that writes such a row past the
        // application is refused by the lock (the next test); the application's own work is not held by it.
        await garden.Services.ChangeAsync(async context =>
            (await context.Plots.SingleAsync(plot => plot.Id == data.Beans, Cancellation)).GiveRole(data.Di.Member, data.OrchardTender.Id, MemberPeriod.Open(data.Now.AddDays(-1)), data.Now, by: null));

        // The application reads the roles through the host's context, where the host's rule about whose roles
        // a request reads applies: in the meadow the orchard's tender is no role there is, and gives nothing.
        (await garden.Services.PlotsAsync(data.Di, access => access.HoldAsync(data.Beans, PlotKeys.Plant, Cancellation)))!.Via.Should().BeNull();

        // The functions run as their owner and join a role by its id. A rule the host keeps in its context
        // does not reach them, so there the row gives what it holds: keeping a role of one garden off the
        // plots of another is the admission's to ask, and the lock's where a caller writes the row itself.
        await using var di = await garden.SessionAsync(data.Di);
        (await di.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotKeys.Plant)).Should().Equal(data.Beans.Value);
        (await di.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", PlotKeys.Water)).Should().BeEmpty("the orchard took watering out of its tender's role");
    }

    [Fact]
    public async Task A_gardener_who_writes_a_plots_gardeners_gives_no_role_of_another_garden_and_its_keys_are_never_held_there()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres, asCaller: true);
        var data = garden.Scenario;
        var functions = PlotMembership.Rules.Functions;
        var beans = data.Beans.Value;
        var di = data.Di.Member.Value;

        // Eve fences the beans, through the meadow's fencer: the host's rule lets her change the plot, and the
        // lock lets her write its gardeners and their roles. Di is on the beans with no role.
        async Task<List<Guid>> HeldByDiAsync(string key)
        {
            await using var session = await garden.SessionAsync(data.Di);
            return await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", key);
        }

        (await HeldByDiAsync(PlotKeys.Plant)).Should().NotContain(beans);

        // Straight on the database, as herself: Di on the beans in the orchard's tender, a role of another
        // garden, which plants. The host's rule shows her the roles of her own garden, and the lock asks the
        // role table as her.
        const string GiveDiARole =
            $"""
            INSERT INTO {Gardens}"PlotGardenerRoles" ("PlotId", "PlotGardenerId", "RoleId", "StartsAt", "EndsAt", "GivenBy")
            SELECT m."PlotId", m."Id", $3, pg_catalog.now() - interval '1 hour', NULL, NULL
            FROM {Gardens}"PlotGardeners" m WHERE m."PlotId" = $1 AND m."MemberId" = $2
            """;
        await using (var eve = await garden.SessionAsync(data.Eve))
        {
            (await FluentActions.Awaiting(() => eve.ListAsync<object>(GiveDiARole, beans, di, data.OrchardTender.Id.Value)).Should().ThrowAsync<Npgsql.PostgresException>())
                .Which.Should().Match<Npgsql.PostgresException>(denied =>
                    denied.SqlState == Npgsql.PostgresErrorCodes.InsufficientPrivilege && denied.MessageText.Contains("Members hold roles the caller sees (insert) for authenticated"));
        }

        // Nor by changing a role a gardener holds into that one: Cy waters the beans in the meadow's waterer.
        await using (var eve = await garden.SessionAsync(data.Eve))
        {
            (await FluentActions.Awaiting(() => eve.ListAsync<object>(
                    $"""UPDATE {Gardens}"PlotGardenerRoles" SET "RoleId" = $2 WHERE "PlotId" = $1 AND "RoleId" = $3""",
                    beans, data.OrchardTender.Id.Value, data.MeadowWaterer.Id.Value)).Should().ThrowAsync<Npgsql.PostgresException>())
                .Which.Should().Match<Npgsql.PostgresException>(denied =>
                    denied.SqlState == Npgsql.PostgresErrorCodes.InsufficientPrivilege && denied.MessageText.Contains("Members hold roles the caller sees (update) for authenticated"));
        }

        // Through the application as herself, by a handler that did not ask the admission: the database says no all the same.
        var refused = await FluentActions.Awaiting(() => garden.Services.AsAsync(data.Eve, async provider =>
        {
            var context = provider.GetRequiredService<GardenContext>();
            var plot = await context.Plots.SingleAsync(candidate => candidate.Id == data.Beans, Cancellation);
            plot.GiveRole(data.Di.Member, data.OrchardTender.Id, MemberPeriod.Open(data.Now.AddDays(-1)), data.Now, by: data.Eve.Member);
            await context.SaveChangesAsync(Cancellation);
            return true;
        })).Should().ThrowAsync<DDDToolkit.Exceptions.RefusalException>();
        refused.Which.GetBaseException().Should().BeOfType<Npgsql.PostgresException>().Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);

        // The orchard's keys are held on none of the meadow's plots: in the database, and in the application.
        (await HeldByDiAsync(PlotKeys.Plant)).Should().NotContain(beans);
        await using (var session = await garden.SessionAsync(data.Di))
        {
            (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotKeys.Plant)).Should().BeEmpty();
        }

        (await garden.Services.PlotsAsync(data.Di, access => access.HoldAsync(data.Beans, PlotKeys.Plant, Cancellation)))!.Via.Should().BeNull();
        (await garden.Services.ReadAsync(data.Beans)).Gardeners.SelectMany(gardener => gardener.Roles).Should().NotContain(held => held.RoleId == data.OrchardTender.Id);

        // A role of her own garden she gives as before: it is the garden, and not the giving, that the lock asks about.
        await using (var eve = await garden.SessionAsync(data.Eve))
        {
            await eve.ListAsync<object>(GiveDiARole, beans, di, data.MeadowWaterer.Id.Value);
            await eve.CommitAsync();
        }

        (await HeldByDiAsync(PlotKeys.Water)).Should().Contain(beans);
        (await HeldByDiAsync(PlotKeys.Plant)).Should().NotContain(beans);
    }

    [Fact]
    public async Task The_owners_role_stays_in_use_whoever_writes_its_row_and_where_a_role_came_from_is_written_once()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres);
        var data = garden.Scenario;

        async Task<Npgsql.PostgresException> RefusedAsync(string sql, params object[] values)
            => (await FluentActions.Awaiting(() => garden.ListAsync<object>(sql, values)).Should().ThrowAsync<Npgsql.PostgresException>()).Which;

        // As the role that owns the tables, past every rule: the meadow's role for owners is not archived, as it is not in C#.
        var archived = await RefusedAsync($"""UPDATE {Gardens}"PlotRoles" SET "Status" = 'Archived' WHERE "Id" = $1""", data.MeadowOwner.Id.Value);
        (archived.SqlState, archived.MessageText).Should().Be((
            Npgsql.PostgresErrorCodes.CheckViolation,
            "A row of \"gardens\".\"PlotRoles\" made from the starter role 'owner' is the role every owner of a Plot holds, and is not archived."));

        // Nor is one made archived, for a garden that has none yet.
        var made = await RefusedAsync(
            $"""
            INSERT INTO {Gardens}"PlotRoles" ("Id", "GardenId", "Name", "Description", "Keys", "Status", "MadeFrom", "Version")
            VALUES (pg_catalog.gen_random_uuid(), pg_catalog.gen_random_uuid(), 'Owner', '', ARRAY[]::text[], 'Archived', 'owner', 1)
            """);
        made.SqlState.Should().Be(Npgsql.PostgresErrorCodes.CheckViolation);

        // It is renamed and given other keys like any role, the orchard's waterer is archived, and the sheds' keeper is its own trigger's.
        (await garden.ListAsync<string>($"""UPDATE {Gardens}"PlotRoles" SET "Name" = 'Plot holder', "Keys" = ARRAY['plots.see'] WHERE "Id" = $1 RETURNING "Name" """, data.MeadowOwner.Id.Value))
            .Should().Equal("Plot holder");
        (await garden.ListAsync<string>($"""UPDATE {Gardens}"PlotRoles" SET "Status" = 'Archived' WHERE "Id" = $1 RETURNING "Status" """, data.OrchardWaterer.Id.Value))
            .Should().Equal("Archived");
        (await RefusedAsync($"""UPDATE {Gardens}"ShedRoles" SET "Status" = 'Archived' WHERE "Id" = $1""", data.ShedKeeper.Id.Value)).MessageText
            .Should().Be("A row of \"gardens\".\"ShedRoles\" made from the starter role 'keeper' is the role every owner of a Shed holds, and is not archived.");

        // Where a role came from is written once: the application cannot save it changed, so the owner's role is found where it was.
        var moved = await FluentActions.Awaiting(() => garden.Services.ChangeAsync(data.MeadowOwner.Id, role => Break.Set(role, nameof(PlotRole.MadeFrom), PlotMembership.Tender)))
            .Should().ThrowAsync<InvalidOperationException>();
        moved.Which.Message.Should().Contain(nameof(PlotRole.MadeFrom));
        (await garden.Services.ReadAsync(data.MeadowOwner.Id)).MadeFrom.Should().Be(MembershipRules.DefaultOwnerRole);
    }

    [Fact]
    public async Task With_the_privileges_written_from_the_policies_a_caller_who_may_change_a_role_does_not_move_where_it_came_from()
    {
        // A host whose rule lets a signed-in user change the roles of plots of its own garden, and whose export
        // writes the privileges from the policies: every column of a role but the ones fixed once it is there.
        using var garden = await PostgresGarden.CreateAsync(postgres, secured: false);
        var data = garden.Scenario;
        RowAccessRule[] rules =
        [
            .. PostgresGarden.Rules,
            RowAccessRule.For<PlotRole>("Gardeners change the roles of their garden", RowOperations.Change, GardenersReadTheRolesOfTheirGarden.RowAccessSql, RowAccessRoles.User),
        ];

        string script;
        using (var model = PostgresGarden.Model())
        {
            script = PostgresRowAccess.Script(model, rules, null, new RowAccessExport { Contributions = PostgresGarden.Contributions, WriteGrants = true });
        }

        script.Should().MatchRegex("GRANT UPDATE \\([^)]*\"Status\"[^)]*\\) ON TABLE gardens\\.\"PlotRoles\" TO authenticated;")
            .And.NotMatchRegex("GRANT UPDATE \\([^)]*\"MadeFrom\"[^)]*\\) ON TABLE gardens\\.\"PlotRoles\"");
        await garden.ExecuteAsync(script);

        // Ben may change the meadow's roles now. Where a role came from he changes on none of them.
        await using (var ben = await garden.SessionAsync(data.Ben))
        {
            (await FluentActions.Awaiting(() => ben.ListAsync<object>($"""UPDATE {Gardens}"PlotRoles" SET "MadeFrom" = NULL WHERE "Id" = $1""", data.MeadowOwner.Id.Value))
                .Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
        }

        // The rest of a role is his to change, the owner's role kept in use whoever changes it.
        await using (var ben = await garden.SessionAsync(data.Ben))
        {
            (await ben.ListAsync<string>($"""UPDATE {Gardens}"PlotRoles" SET "Name" = 'Plot holder' WHERE "Id" = $1 RETURNING "Name" """, data.MeadowOwner.Id.Value)).Should().Equal("Plot holder");
            (await FluentActions.Awaiting(() => ben.ListAsync<object>($"""UPDATE {Gardens}"PlotRoles" SET "Status" = 'Archived' WHERE "Id" = $1""", data.MeadowOwner.Id.Value))
                .Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.CheckViolation);
        }

        (await garden.Services.ReadAsync(data.MeadowOwner.Id)).Should().Match<PlotRole>(role => role.MadeFrom == MembershipRules.DefaultOwnerRole && role.Status == KeptRoleStatus.Active);
    }

    [Fact]
    public async Task What_is_held_through_a_role_of_plots_is_one_statement_under_the_policies_and_reads_the_role_rows()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres, asCaller: true);
        var data = garden.Scenario;
        var commands = garden.Services.Commands;
        var precision = TimeSpan.FromMilliseconds(1);

        async Task<MemberHold<PlotId>?> AskAsync(GardenPerson person, PlotId plot, string key)
        {
            commands.Reset();
            var held = await garden.Services.PlotsAsync(person, access => access.HoldAsync(plot, key, Cancellation));
            commands.Count.Should().Be(1, "how " + key + " is held, until when, and the plot's version are one statement");
            return held;
        }

        // Through a role of the garden's, for as long as the role is held; the role rows are in the statement.
        var water = await AskAsync(data.Cy, data.Beans, PlotKeys.Water);
        water!.Via.Should().Be(MemberVia.Members);
        water.Until.Should().BeCloseTo(data.InThreeDays, precision);
        commands.Commands[0].Should().Contain("gardens.\"PlotGardeners\"").And.Contain("gardens.\"PlotGardenerRoles\"").And.Contain("gardens.\"PlotRoles\"");

        // As the owner, through a role put away, through no role, and on a plot that is not there for the caller: as on SQLite.
        var sell = await AskAsync(data.Ada, data.Beans, PlotKeys.Sell);
        (sell!.Via, sell.Until).Should().Be((MemberVia.Members, (DateTimeOffset?)null));
        (await AskAsync(data.Eve, data.Beans, PlotKeys.Fence))!.Via.Should().Be(MemberVia.Members);
        (await AskAsync(data.Fay, data.Beans, PlotKeys.Plant))!.Via.Should().BeNull("the seasonal hand was put away");
        (await AskAsync(data.Di, data.Beans, PlotKeys.Water))!.Via.Should().BeNull();
        (await AskAsync(data.Hal, data.Kale, PlotKeys.Water))!.Via.Should().BeNull("the orchard took watering out of its tender's role");
        (await AskAsync(data.Hal, data.Kale, PlotKeys.Plant))!.Via.Should().Be(MemberVia.Members);
        (await AskAsync(data.Hal, data.Beans, PlotKeys.See)).Should().BeNull();

        // The keys held on many, and a page of the host's own with a reach in it: one statement each.
        commands.Reset();
        var keys = await garden.Services.PlotsAsync(data.Ben, access => access.KeysOnAsync([data.Beans, data.Leeks, data.Kale], GardenScenario.KeysAsked, Cancellation));
        commands.Count.Should().Be(1);
        keys.Keys.Should().BeEquivalentTo([data.Beans, data.Leeks]);
        keys[data.Beans].Should().BeEquivalentTo([PlotKeys.See, PlotKeys.Plant, PlotKeys.Water]);
        keys[data.Leeks].Should().BeEquivalentTo([PlotKeys.See, PlotKeys.Plant, PlotKeys.Water, PlotKeys.Fence, PlotKeys.Sell]);

        commands.Reset();
        var watered = await garden.Services.AsAsync(data.Ada, async provider =>
        {
            var access = provider.GetRequiredService<IMemberQuestions<PlotId>>();
            return await provider.GetRequiredService<GardenContext>().Plots
                .Within(access.Reach(PlotKeys.See))
                .Within(access.Reach(PlotKeys.Water))
                .OrderBy(plot => plot.Name)
                .Select(plot => plot.Name)
                .ToListAsync(Cancellation);
        });
        watered.Should().Equal("Beans", "Leeks");
        commands.Count.Should().Be(1);

        // Past the application, the policy lets a caller read what the function answers.
        await using var eve = await garden.SessionAsync(data.Eve);
        (await eve.ListAsync<string>($"SELECT \"Name\" FROM {Gardens}\"Plots\" ORDER BY 1")).Should().Equal("Beans", "Leeks");
        await using var jo = await garden.SessionAsync(data.Jo);
        (await jo.ListAsync<string>($"SELECT \"Name\" FROM {Gardens}\"Plots\"")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_gardener_changes_no_role_past_the_application()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres, asCaller: true);
        var data = garden.Scenario;
        var tender = data.MeadowTender.Id.Value;

        async Task<List<Guid>> FencedByBenAsync()
        {
            await using var session = await garden.SessionAsync(data.Ben);
            return await session.ListAsync<Guid>($"SELECT id FROM {Gardens}plots_where_i_hold($1) AS id", PlotKeys.Fence);
        }

        // Ben tends the beans: the meadow's tender plants and waters, and does not fence.
        var before = await FencedByBenAsync();
        before.Should().NotContain(data.Beans.Value);

        // A role's row says what everybody who holds the role may do. Straight on the database, as himself,
        // he gives the role he holds every key a role can give, puts a role that was put away back in use, and
        // makes a role of his own: the host's rule lets a signed-in user read the roles, and nobody write them.
        await using (var session = await garden.SessionAsync(data.Ben))
        {
            (await session.ListAsync<long>(
                $"WITH changed AS (UPDATE {Gardens}\"PlotRoles\" SET \"Keys\" = ARRAY['plots.see', 'plots.plant', 'plots.water', 'plots.fence'] WHERE \"Id\" = $1 RETURNING 1) SELECT count(*) FROM changed",
                tender)).Should().Equal([0L], "no rule lets a caller change a role, so there is no row for him to change");
            (await session.ListAsync<long>(
                $"WITH changed AS (UPDATE {Gardens}\"PlotRoles\" SET \"Status\" = 'Active' WHERE \"Id\" = $1 RETURNING 1) SELECT count(*) FROM changed",
                data.Seasonal.Id.Value)).Should().Equal(0L);
            (await session.ListAsync<long>($"WITH gone AS (DELETE FROM {Gardens}\"PlotRoles\" RETURNING 1) SELECT count(*) FROM gone")).Should().Equal(0L);
        }

        await using (var session = await garden.SessionAsync(data.Ben))
        {
            (await FluentActions.Awaiting(() => session.ListAsync<object>(
                    $"INSERT INTO {Gardens}\"PlotRoles\" (\"Id\", \"GardenId\", \"Name\", \"Description\", \"Keys\", \"Status\", \"MadeFrom\", \"Version\") "
                    + "SELECT pg_catalog.gen_random_uuid(), \"GardenId\", 'Everything', '', ARRAY['plots.fence'], 'Active', NULL, 1 "
                    + $"FROM {Gardens}\"PlotRoles\" WHERE \"Id\" = $1",
                    tender)).Should().ThrowAsync<Npgsql.PostgresException>())
                .Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
        }

        // Somebody who did not sign in reads no role and writes none.
        await using (var nobody = await CallerSession.BeginAsync(garden.Database, Caller.Anonymous, role: "anon"))
        {
            (await nobody.ListAsync<long>($"SELECT count(*) FROM {Gardens}\"PlotRoles\"")).Should().Equal(0L);
            (await nobody.ListAsync<long>(
                $"WITH changed AS (UPDATE {Gardens}\"ShedRoles\" SET \"Keys\" = ARRAY['sheds.sell'] RETURNING 1) SELECT count(*) FROM changed")).Should().Equal(0L);
        }

        // The rows are what they were, and so is what he holds: in the database and in the application.
        (await garden.Services.ReadAsync(data.MeadowTender.Id)).Keys.Should().BeEquivalentTo([PlotKeys.See, PlotKeys.Plant, PlotKeys.Water]);
        (await garden.Services.ReadAsync(data.Seasonal.Id)).Status.Should().Be(KeptRoleStatus.Archived);
        (await FencedByBenAsync()).Should().BeEquivalentTo(before);
        (await garden.Services.PlotsAsync(data.Ben, access => access.HoldAsync(data.Beans, PlotKeys.Fence, Cancellation)))!.Via.Should().BeNull();

        // And the roles are read as before, under the rule: the admission and the questions run as the caller.
        await garden.Services.AsAsync(data.Ben, async provider =>
        {
            await provider.GetRequiredService<MemberAdmission<PlotId, UserId, PlotRoleId>>().RequireRoleAsync(data.MeadowTender.Id, Cancellation);
            return true;
        });
        (await garden.Services.PlotsAsync(data.Ben, access => access.HoldAsync(data.Beans, PlotKeys.Plant, Cancellation)))!.Via.Should().Be(MemberVia.Members);
    }

    [Fact]
    public void The_functions_read_a_roles_keys_from_the_role_table_and_ask_no_function_for_them()
    {
        using var context = PostgresGarden.Model();

        var plots = new PlotMembershipFunctions().Contribute(context, Export)!;

        // Nothing is allowed by the package, and no policy is written for the role table: the lock the plots'
        // rules name keys for is on the gardeners' two tables and on the plot's owner. On the role table there
        // is the trigger that keeps the owner's role in use, and nothing else.
        plots.Policies.Should().OnlyContain(policy => policy.Restrictive && policy.Table.ClrType != typeof(PlotRole));
        plots.Statements.Where(statement => statement.Contains("PlotRoles")).Should().OnlyContain(statement => statement.Contains("plots_owner_role_stays"));
        plots.Functions.Select(function => (function.Name, function.Parameters, function.Returns)).Should().Equal(
            ("plots_as_member", "", "SETOF uuid"),
            ("plots_as_member_with", "text", "SETOF uuid"),
            ("plots_i_see", "", "SETOF uuid"),
            ("plots_where_i_hold", "text", "SETOF uuid"));

        // A role held now, in a membership that applies now, whose row is in use and holds the key: the role
        // table of the same context, joined to the roles a gardener holds, for a key a member's role can give.
        var withARole = plots.Functions[1].Body;
        withARole.Should().Contain(
            "\n  AND $1 IN ('plots.see', 'plots.plant', 'plots.water', 'plots.fence')"
            + "\n  AND EXISTS (SELECT 1 FROM \"gardens\".\"PlotGardenerRoles\" h"
            + "\n              JOIN \"gardens\".\"PlotRoles\" k ON k.\"Id\" = h.\"RoleId\""
            + "\n              WHERE h.\"PlotId\" = m.\"PlotId\" AND h.\"PlotGardenerId\" = m.\"Id\" AND ");
        withARole.Should().EndWith("\n                AND k.\"Status\" = 'Active' AND $1 = ANY (k.\"Keys\"))");

        // The starter roles are in no function: they are rows like any other role once they are made.
        withARole.Should().NotContain("VALUES").And.NotContain(PlotMembership.Tender).And.NotContain(PlotMembership.Waterer);

        // No function is asked but the plots' own, and the caller is who its token says.
        foreach (var function in plots.Functions)
        {
            LogicalName().Matches(function.Body).Select(match => match.Groups["owner"].Value).Where(owner => owner != "plots").Should().BeEmpty();
            function.Body.Should().NotContain("\r");
            function.Body.Split('\n')[0].Should().MatchRegex("^-- Membership of plots, in form [0-9]+, written from the rules [0-9a-f]{64}$");
        }

        LogicalName().Matches(plots.Functions[3].Body).Select(match => match.Value).Should().Equal("{fn:plots/plots_as_member_with}", "{fn:plots/plots_as_member}");
        plots.Functions[0].Body.Should().Contain("m.\"MemberId\" = {caller:uid} AND ");
        plots.Functions[3].Body.Should().Contain("$1 IN ('plots.see', 'plots.plant', 'plots.water', 'plots.fence', 'plots.sell') AND r.\"OwnerId\" = {caller:uid}");

        // What a member's role can give stands in front of what the rows say, as the rules say it.
        string Cut(MemberKeys keys) => new MembershipRowAccessContribution<PlotGardener>(Plots(memberKeys: keys)).Contribute(context, Export)!.Functions[1].Body;
        Cut(MemberKeys.AllBut(PlotKeys.Sell, "plots.it's")).Should().Contain("\n  AND $1 NOT IN ('plots.sell', 'plots.it''s')\n  AND EXISTS (");
        Cut(MemberKeys.AllBut()).Should().NotContain("$1 NOT IN").And.Contain("$1 = ANY (k.\"Keys\")");

        // Rules under which a role could give nothing are no rules for roles a customer makes: refused where they are said.
        FluentActions.Invoking(() => Plots(memberKeys: MemberKeys.Only())).Should().Throw<ArgumentException>().WithParameterName("memberKeys");
    }

    [Fact]
    public void Roles_kept_for_a_resource_are_written_with_whatever_else_the_rules_say()
    {
        // Where the roles come from stands by itself: kept roles for members the host resolves, on a resource
        // that is reached from above. The host's functions are asked for the two things it answers, by the
        // logical names the rules give, and for the roles nothing is asked: they are read from their table.
        using var context = new PlotsInGardens(new DbContextOptionsBuilder<PlotsInGardens>().UseNpgsql("Host=model-only").Options);
        var rules = new MembershipRules(
            "plots",
            keys: [.. PlotMembership.Rules.Keys],
            roles: [.. PlotMembership.Rules.Roles.Where(role => role.Name != MembershipRules.DefaultOwnerRole)],
            members: MemberSource.Resolved("gardens/caller_gardener"),
            seeKey: PlotKeys.See,
            memberKeys: PlotMembership.Rules.MemberKeys,
            above: new("gardens/gardens_where_i_hold"),
            rolesKept: true);

        var functions = new MembershipRowAccessContribution<PlotGardener>(rules).Contribute(context, Export)!.Functions;

        functions[0].Body.Should().Contain("m.\"MemberId\" = (SELECT {fn:gardens/caller_gardener}()) AND ");
        functions[1].Body.Should()
            .Contain("m.\"MemberId\" = (SELECT {fn:gardens/caller_gardener}()) AND ")
            .And.Contain("\n  AND $1 IN ('plots.see', 'plots.plant', 'plots.water', 'plots.fence')\n  AND EXISTS (SELECT 1 FROM ")
            .And.Contain(" k ON k.\"Id\" = h.\"RoleId\"")
            .And.EndWith("AND k.\"Status\" = 'Active' AND $1 = ANY (k.\"Keys\"))");
        functions[2].Body.Should().Contain("r.\"GardenId\" IN (SELECT reached.place FROM {fn:gardens/gardens_where_i_hold}('plots.see') AS reached(place))");
        functions[3].Body.Should()
            .Contain("r.\"GardenId\" IN (SELECT reached.place FROM {fn:gardens/gardens_where_i_hold}($1) AS reached(place))")
            .And.Contain("r.\"OwnerId\" = (SELECT {fn:gardens/caller_gardener}())");

        // No function is asked for the roles: the two the rules name, and the plots' own, are all there is.
        functions.SelectMany(function => LogicalName().Matches(function.Body).Select(match => match.Value)).Distinct()
            .Should().BeEquivalentTo("{fn:gardens/caller_gardener}", "{fn:gardens/gardens_where_i_hold}", "{fn:plots/plots_as_member}", "{fn:plots/plots_as_member_with}");

        // And what the functions are written from says all three: other than the plots' own rules, and other than the same without one of them.
        static string MarkerOf(MembershipRules written, DbContext model)
            => new MembershipRowAccessContribution<PlotGardener>(written).Contribute(model, Export)!.Functions[0].Body.Split('\n')[0];
        var withoutAbove = new MembershipRules(
            "plots", keys: [.. rules.Keys], roles: [.. PlotMembership.Rules.Roles.Where(role => role.Name != MembershipRules.DefaultOwnerRole)],
            members: MemberSource.Resolved("gardens/caller_gardener"), seeKey: PlotKeys.See, memberKeys: rules.MemberKeys, rolesKept: true);
        new[] { MarkerOf(rules, context), MarkerOf(withoutAbove, context), MarkerOf(PlotMembership.Rules, context) }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_fingerprint_says_that_the_roles_are_kept_and_what_they_may_give_and_nothing_of_the_starter_roles()
    {
        using var context = PostgresGarden.Model();
        static string MarkerOf(MembershipRules rules, DbContext context)
            => new MembershipRowAccessContribution<PlotGardener>(rules).Contribute(context, Export)!.Functions[0].Body.Split('\n')[0];

        var plots = MarkerOf(PlotMembership.Rules, context);
        MarkerOf(Plots(), context).Should().Be(plots, "rules that say the same are written the same");

        // The starter roles are what a garden's first roles are made from, and no function is written from
        // them: another one, or other keys for one, leaves the database's functions the ones the rules say.
        MarkerOf(Plots(starters: [new(PlotMembership.Tender, [PlotKeys.See, PlotKeys.Plant]), new("weeder", [PlotKeys.See])]), context).Should().Be(plots);
        MarkerOf(Plots(starters: []), context).Should().Be(plots);

        // What a member's role can give is cut inside the functions, so it is part of what they are written from.
        MarkerOf(Plots(memberKeys: MemberKeys.Only(PlotKeys.See, PlotKeys.Plant, PlotKeys.Water)), context).Should().NotBe(plots);
        MarkerOf(Plots(memberKeys: MemberKeys.AllBut(PlotKeys.Sell)), context).Should().NotBe(plots);

        // And so is every key of the plot, which its owner holds, as for any resource.
        MarkerOf(new MembershipRules("plots", keys: [PlotKeys.See], seeKey: PlotKeys.See, memberKeys: PlotMembership.Rules.MemberKeys, rolesKept: true), context).Should().NotBe(plots);

        // And the starter role the owner's role is made from, which the trigger that keeps that role in use asks for.
        MarkerOf(Plots(ownerRole: PlotMembership.Tender), context).Should().NotBe(plots);
    }

    [Fact]
    public void Rules_that_keep_the_roles_are_refused_where_the_model_has_no_role_table_the_functions_can_read()
    {
        // No role class is mapped for the plots.
        using var bare = new PlotsWithoutRoles(new DbContextOptionsBuilder<PlotsWithoutRoles>().UseNpgsql("Host=model-only").Options);
        FluentActions.Invoking(() => new PlotMembershipFunctions().Contribute(bare, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules 'plots' say the roles of Plot are kept, and the model maps no role class for it.*[KeptRole<TRoleId, Plot>]*modelBuilder.Entity<PlotRole>().IsKeptRole().");

        // The keys are kept as something the functions do not read.
        using var documents = new PlotsWithKeysAsJson(new DbContextOptionsBuilder<PlotsWithKeysAsJson>().UseNpgsql("Host=model-only").Options);
        FluentActions.Invoking(() => new PlotMembershipFunctions().Contribute(documents, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("PlotRole.Keys is stored as jsonb, and the functions read a role's keys from an array, such as text[]. Leave the column as IsKeptRole maps it.");

        // A role class known by another id than the gardeners hold their roles by: nothing to join the two on.
        using var stray = new PlotsWithStrayRoles(new DbContextOptionsBuilder<PlotsWithStrayRoles>().UseNpgsql("Host=model-only").Options);
        FluentActions.Invoking(() => new PlotMembershipFunctions().Contribute(stray, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The members of Plot hold roles known by PlotRoleId, and its role class *StrayRole is known by GardenId.*declare the member class with GardenId as its role.");

        // Rules that declare the roles, for gardeners that hold a role by an id.
        using var context = PostgresGarden.Model();
        FluentActions.Invoking(() => new MembershipRowAccessContribution<PlotGardener>(
                new MembershipRules("plots", keys: [PlotKeys.See], roles: [new("reader", [PlotKeys.See])])).Contribute(context, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The members of Plot hold roles known by PlotRoleId, and the rules 'plots' declare the roles, which a member holds by name.*kept for the resource, in its role class, or kept elsewhere*");
    }

    [Fact]
    public async Task The_start_up_check_holds_the_plots_functions_to_the_rules_they_are_registered_with()
    {
        // As the host writes its access file: it starts.
        using var secured = await PostgresGarden.CreateAsync(postgres, seed: false);
        await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(secured.Services.Provider, Cancellation);

        // Without the functions it does not.
        using var plain = await PostgresGarden.CreateAsync(postgres, secured: false, seed: false);
        (await FluentActions.Awaiting(() => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(plain.Services.Provider, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .StartWith("The functions that answer the membership of Plot in the database are not as its rules, 'plots', say them: ")
            .And.Contain("gardens.plots_as_member (missing), gardens.plots_as_member_with (missing), gardens.plots_i_see (missing), gardens.plots_where_i_hold (missing)")
            .And.Contain("[MembershipRules<PlotGardener>]");

        // Written from rules that let a member's role give another key: the database would answer otherwise than the application.
        await plain.ExecuteAsync(PostgresGarden.AccessScript([new MembershipRowAccessContribution<PlotGardener>(Plots(memberKeys: MemberKeys.AllBut())), new ShedMembershipFunctions(), new GardenOwnFunctions()]));
        (await FluentActions.Awaiting(() => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(plain.Services.Provider, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("gardens.plots_as_member_with (it was written from other rules)");

        // Written from rules that differ in their starter roles alone: the functions are the same, and it starts.
        await plain.ExecuteAsync(PostgresGarden.AccessScript([new MembershipRowAccessContribution<PlotGardener>(Plots(starters: [new("weeder", [PlotKeys.See])])), new ShedMembershipFunctions(), new GardenOwnFunctions()]));
        await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(plain.Services.Provider, Cancellation);
    }

    [Fact]
    public async Task The_start_up_check_refuses_rules_that_keep_the_roles_over_a_context_that_maps_no_role_class()
    {
        // Said before the database is asked anything: the model is what is wrong.
        var services = new ServiceCollection();
        services.AddDbContext<PlotsWithoutRoles>(options => options.UseNpgsql("Host=model-only"));
        services.AddMembership<PlotGardener, PlotGardenerId, UserId, PlotRoleId, PlotsWithoutRoles, Plot, PlotId>(PlotMembership.Rules);
        await using var provider = services.BuildServiceProvider();

        (await FluentActions.Awaiting(() => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(provider, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Be(
                "The rules 'plots' say the roles of Plot are kept, and PlotsWithoutRoles maps no role class for it, so its functions cannot be the ones the rules say. "
                + "Map the role class where the context builds its model: modelBuilder.Entity<PlotRole>().IsKeptRole().");
    }

    /// <summary>
    /// For everybody and every key: the four functions, asked past the application; the access questions, asked
    /// in the garden the person's requests are in; and the rules read over the aggregates in memory.
    /// </summary>
    private static async Task AgreeForEverybodyAsync(PostgresGarden garden)
    {
        var data = garden.Scenario;
        var functions = PlotMembership.Rules.Functions;

        foreach (var person in data.People)
        {
            await using var session = await garden.SessionAsync(person);

            var seenInCSharp = await garden.Services.AsAsync(person, async provider =>
            {
                var see = provider.GetRequiredService<IMemberQuestions<PlotId>>().KeyReach([]).See;
                return await provider.GetRequiredService<GardenContext>().Plots.Within(see).Select(plot => plot.Id.Value).ToListAsync(Cancellation);
            });
            var seen = data.PlotsSeenBy(person).Select(id => id.Value).ToList();
            (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.Seen}() AS id")).Should().BeEquivalentTo(seen, "of what " + person + " sees");
            (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMember}() AS id")).Should().BeEquivalentTo(seen);
            seenInCSharp.Should().BeEquivalentTo(seen);

            foreach (var key in GardenScenario.KeysAsked)
            {
                var heldInCSharp = await garden.Services.AsAsync(person, async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<PlotId>>().Reach(key);
                    return await provider.GetRequiredService<GardenContext>().Plots.Within(reach).Select(plot => plot.Id.Value).ToListAsync(Cancellation);
                });
                var held = data.PlotsHeldBy(person, key).Select(id => id.Value).ToList();
                var because = "of what " + person + " holds " + key + " on";

                (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", key)).Should().BeEquivalentTo(held, because);
                heldInCSharp.Should().BeEquivalentTo(held, because);

                // Through a role alone: one of the garden's in use, for a key a member's role can give.
                (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", key))
                    .Should().BeEquivalentTo(data.PlotsWithARoleFor(person, key).Select(id => id.Value), because + " through a role");
            }
        }
    }

    /// <summary>
    /// The plots' rules, said again entry for entry, with other starter roles, another list of what a member's
    /// role can give, or another starter role for the owner's.
    /// </summary>
    private static MembershipRules Plots(IReadOnlyList<DeclaredRole>? starters = null, MemberKeys? memberKeys = null, string? ownerRole = null)
    {
        var plots = PlotMembership.Rules;
        return new MembershipRules(
            plots.Name,
            keys: [.. plots.Keys],
            roles: starters ?? [new(PlotMembership.Tender, [PlotKeys.See, PlotKeys.Plant, PlotKeys.Water]), new(PlotMembership.Waterer, [PlotKeys.See, PlotKeys.Water])],
            seeKey: plots.SeeKey,
            ownerRole: ownerRole,
            memberKeys: memberKeys ?? MemberKeys.Only(PlotKeys.See, PlotKeys.Plant, PlotKeys.Water, PlotKeys.Fence),
            codes: plots.Codes,
            rolesKept: true,
            changeMembersKey: plots.ChangeMembersKey,
            changeOwnerKey: plots.ChangeOwnerKey);
    }

    [GeneratedRegex(@"\{fn:(?<owner>[^/}]+)/[^}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalName();

    /// <summary>A context that maps the plots with their gardeners, and no role class for them.</summary>
    private sealed class PlotsWithoutRoles(DbContextOptions<PlotsWithoutRoles> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(GardenContext.Schema);
            modelBuilder.Entity<Plot>().HasMembers(plot => plot.Gardeners, plot => plot.OwnerId);
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }

    /// <summary>A context that maps the plots, and marks as their roles a class that is known by another id.</summary>
    private sealed class PlotsWithStrayRoles(DbContextOptions<PlotsWithStrayRoles> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Plot>().HasMembers(plot => plot.Gardeners, plot => plot.OwnerId);
            modelBuilder.Entity<StrayRole>().HasAnnotation(MembershipModel.RoleOfAnnotation, typeof(Plot).FullName);
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }

    /// <summary>Rows that look like roles and are known by something a gardener holds no role by.</summary>
    private sealed class StrayRole
    {
        public GardenId Id { get; set; }

        public List<string> Keys { get; set; } = [];

        public KeptRoleStatus Status { get; set; }
    }

    /// <summary>A context that maps the plots as sitting in their garden, and their role class.</summary>
    private sealed class PlotsInGardens(DbContextOptions<PlotsInGardens> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Plot>().HasMembers(plot => plot.Gardeners, plot => plot.OwnerId, at: plot => plot.GardenId);
            modelBuilder.Entity<PlotRole>().IsKeptRole();
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }

    /// <summary>A context that maps the plots and their role class, and then keeps a role's keys as a JSON document.</summary>
    private sealed class PlotsWithKeysAsJson(DbContextOptions<PlotsWithKeysAsJson> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Plot>().HasMembers(plot => plot.Gardeners, plot => plot.OwnerId);
            modelBuilder.Entity<PlotRole>(role =>
            {
                role.IsKeptRole();
                role.PrimitiveCollection(row => row.Keys).HasColumnType("jsonb");
            });
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }
}
