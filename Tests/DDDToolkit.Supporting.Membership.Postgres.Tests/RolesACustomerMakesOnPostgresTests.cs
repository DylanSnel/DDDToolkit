using DDDToolkit.Abstractions.Access;
using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.TestHost.Access;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// A resource whose roles are kept, on Postgres: plots in gardens. The four functions of the plots read a
/// role's permissions straight from the table of the host's role class, in the same context, and answer what the
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

        // The sheds' functions, the questions about sheds, and the sheds' rules read in memory: for everybody and every permission.
        foreach (var person in data.People)
        {
            await using var session = await garden.SessionAsync(person);

            // The sheds' rules name no permission for seeing: a shed is seen by the hands in it, and that is all being a hand gives.
            var seen = data.ShedsSeenBy(person).Select(id => id.Value).ToList();
            (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.Seen}() AS id")).Should().BeEquivalentTo(seen, "of the sheds " + person + " sees");
            (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMember}() AS id")).Should().BeEquivalentTo(seen);

            foreach (var permission in GardenScenario.ShedPermissionsAsked)
            {
                var heldInCSharp = await garden.Services.AsAsync(person, async provider =>
                {
                    var reach = provider.GetRequiredService<MembershipAccessManager<Shed>>().Reach(permission);
                    return await provider.GetRequiredService<GardenContext>().Sheds.Within(reach).Select(shed => shed.Id.Value).ToListAsync(Cancellation);
                });
                var held = data.ShedsHeldBy(person, permission).Select(id => id.Value).ToList();
                var because = "of the sheds " + person + " holds " + permission + " on";

                (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", permission)).Should().BeEquivalentTo(held, because);
                heldInCSharp.Should().BeEquivalentTo(held, because);
                (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", permission))
                    .Should().BeEquivalentTo(data.ShedsWithARoleFor(person, permission).Select(id => id.Value), because + " through a role");
            }
        }

        // Ben lends tools through the lender, a permission of another module's, which the sheds' rules let a role give by not keeping it from one.
        await using var ben = await garden.SessionAsync(data.Ben);
        (await ben.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", ToolPermissions.LendTools)).Should().Equal(data.ToolShed.Value);

        // Each resource's functions read its own role table, and each cuts by its own rules.
        using var context = PostgresGarden.Model();
        var sheds = new MembershipRowAccessContribution<ShedHand>(ShedMembership.Rules).Contribute(context, Export)!.Functions[1].Body;
        var plots = new MembershipRowAccessContribution<PlotGardener>(PlotMembership.Rules).Contribute(context, Export)!.Functions[1].Body;
        sheds.Should().Contain("\n  AND $1 NOT IN ('sheds.sell')\n").And.Contain("JOIN \"gardens\".\"ShedRoles\" k ON k.\"Id\" = h.\"RoleId\"").And.NotContain("PlotRoles");
        plots.Should().Contain("JOIN \"gardens\".\"PlotRoles\" k ON k.\"Id\" = h.\"RoleId\"").And.NotContain("ShedRoles");
    }

    [Fact]
    public async Task What_a_garden_does_to_a_role_is_what_the_functions_answer_from_then_on()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres);
        var data = garden.Scenario;
        var functions = PlotMembership.Rules.Functions;

        // Eve fences the beans through the meadow's fencer, as the function for that permission says.
        await using (var before = await garden.SessionAsync(data.Eve))
        {
            (await before.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", PlotPermissions.Fence)).Should().Equal(data.Beans.Value);
        }

        // The meadow puts the fencer away. No access file is written anew: the functions read the role's row.
        await garden.Services.ChangeAsync(data.Fencer.Id, role => role.PutAway());
        data.Fencer.PutAway();
        await using (var after = await garden.SessionAsync(data.Eve))
        {
            (await after.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", PlotPermissions.Fence)).Should().BeEmpty("an archived role gives nothing from then on");
            (await after.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotPermissions.Fence)).Should().BeEmpty();
            (await after.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.Seen}() AS id")).Should().BeEquivalentTo([data.Beans.Value, data.Leeks.Value], "she is still on both plots");
        }

        await AgreeForEverybodyAsync(garden);

        // It has its tender fence instead of water: everybody who tends holds the one and no longer the other.
        await garden.Services.ChangeAsync(data.MeadowTender.Id, role => role.HaveItGive(PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Fence));
        data.MeadowTender.HaveItGive(PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Fence);
        await using (var ben = await garden.SessionAsync(data.Ben))
        {
            (await ben.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotPermissions.Fence)).Should().Equal([data.Beans.Value], "through a role: the leeks he owns, and holds no role on");
            (await ben.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotPermissions.Water)).Should().BeEmpty("the tender waters no more");
            (await ben.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", PlotPermissions.Water)).Should().Equal([data.Leeks.Value], "he waters the leeks by owning them, and the beans no more");
        }

        await AgreeForEverybodyAsync(garden);

        // A row from before the rules changed, which still holds a permission no member's role can give now: the
        // functions cut what it gives as the rules say, as the questions do.
        string[] stale = [PlotPermissions.Plant, PlotPermissions.See, PlotPermissions.Sell, PlotPermissions.Water];
        await garden.Services.ChangeAsync(data.MeadowWaterer.Id, role => Break.Set(role, nameof(PlotRole.Permissions), stale));
        Break.Set(data.MeadowWaterer, nameof(PlotRole.Permissions), stale);
        await using (var ada = await garden.SessionAsync(data.Ada))
        {
            // Ada waters the leeks: planting came with the row, selling did not, and the beans are hers to plant and sell by owning them, through no role.
            (await ada.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotPermissions.Plant)).Should().Equal([data.Leeks.Value]);
            (await ada.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", PlotPermissions.Plant)).Should().BeEquivalentTo([data.Beans.Value, data.Leeks.Value]);
            (await ada.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotPermissions.Sell)).Should().BeEmpty();
            (await ada.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", PlotPermissions.Sell)).Should().Equal(data.Beans.Value);
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
        (await garden.Services.PlotsAsync(data.Di, access => access.HoldAsync(data.Beans, PlotPermissions.Plant, Cancellation)))!.Via.Should().BeNull();

        // The functions run as their owner and join a role by its id. A rule the host keeps in its context
        // does not reach them, so there the row gives what it holds: keeping a role of one garden off the
        // plots of another is the admission's to ask, and the lock's where a caller writes the row itself.
        await using var di = await garden.SessionAsync(data.Di);
        (await di.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotPermissions.Plant)).Should().Equal(data.Beans.Value);
        (await di.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", PlotPermissions.Water)).Should().BeEmpty("the orchard took watering out of its tender's role");
    }

    [Fact]
    public async Task A_gardener_who_writes_a_plots_gardeners_gives_no_role_of_another_garden_and_its_permissions_are_never_held_there()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres, asUser: true);
        var data = garden.Scenario;
        var functions = PlotMembership.Rules.Functions;
        var beans = data.Beans.Value;
        var di = data.Di.Member.Value;

        // Eve fences the beans, through the meadow's fencer: the host's rule lets her change the plot, and the
        // lock lets her write its gardeners and their roles. Di is on the beans with no role.
        async Task<List<Guid>> HeldByDiAsync(string permission)
        {
            await using var session = await garden.SessionAsync(data.Di);
            return await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", permission);
        }

        (await HeldByDiAsync(PlotPermissions.Plant)).Should().NotContain(beans);

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

        // The orchard's permissions are held on none of the meadow's plots: in the database, and in the application.
        (await HeldByDiAsync(PlotPermissions.Plant)).Should().NotContain(beans);
        await using (var session = await garden.SessionAsync(data.Di))
        {
            (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", PlotPermissions.Plant)).Should().BeEmpty();
        }

        (await garden.Services.PlotsAsync(data.Di, access => access.HoldAsync(data.Beans, PlotPermissions.Plant, Cancellation)))!.Via.Should().BeNull();
        (await garden.Services.ReadAsync(data.Beans)).Gardeners.SelectMany(gardener => gardener.Roles).Should().NotContain(held => held.RoleId == data.OrchardTender.Id);

        // A role of her own garden she gives as before: it is the garden, and not the giving, that the lock asks about.
        await using (var eve = await garden.SessionAsync(data.Eve))
        {
            await eve.ListAsync<object>(GiveDiARole, beans, di, data.MeadowWaterer.Id.Value);
            await eve.CommitAsync();
        }

        (await HeldByDiAsync(PlotPermissions.Water)).Should().Contain(beans);
        (await HeldByDiAsync(PlotPermissions.Plant)).Should().NotContain(beans);
    }

    [Fact]
    public async Task Any_role_is_archived_alike_and_where_a_role_came_from_is_written_once()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres);
        var data = garden.Scenario;

        // As the role that owns the tables, past every rule: a role is archived like any other, a starter role's row
        // too. The owner holds no role, so no role is kept in use for owners.
        (await garden.ListAsync<string>($"""UPDATE {Gardens}"PlotRoles" SET "Status" = 'Archived' WHERE "Id" = $1 RETURNING "Status" """, data.MeadowTender.Id.Value))
            .Should().Equal("Archived");
        (await garden.ListAsync<string>($"""UPDATE {Gardens}"ShedRoles" SET "Status" = 'Archived' WHERE "Id" = $1 RETURNING "Status" """, data.ShedKeeper.Id.Value))
            .Should().Equal("Archived");

        // Where a role came from is written once: the application cannot save it changed.
        var moved = await FluentActions.Awaiting(() => garden.Services.ChangeAsync(data.MeadowWaterer.Id, role => Break.Set(role, nameof(PlotRole.MadeFrom), PlotMembership.Tender)))
            .Should().ThrowAsync<InvalidOperationException>();
        moved.Which.Message.Should().Contain(nameof(PlotRole.MadeFrom));
        (await garden.Services.ReadAsync(data.MeadowWaterer.Id)).MadeFrom.Should().Be(PlotMembership.Waterer);
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

        script.Should().MatchRegex("GRANT UPDATE \\([^)]*\"Status\"[^)]*\\) ON TABLE \"gardens\"\\.\"PlotRoles\" TO authenticated;")
            .And.NotMatchRegex("GRANT UPDATE \\([^)]*\"MadeFrom\"[^)]*\\) ON TABLE \"gardens\"\\.\"PlotRoles\"");
        await garden.ExecuteAsync(script);

        // Ben may change the meadow's roles now. Where a role came from he changes on none of them.
        await using (var ben = await garden.SessionAsync(data.Ben))
        {
            (await FluentActions.Awaiting(() => ben.ListAsync<object>($"""UPDATE {Gardens}"PlotRoles" SET "MadeFrom" = NULL WHERE "Id" = $1""", data.MeadowTender.Id.Value))
                .Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
        }

        // The rest of a role is his to change, its name and whether it is in use among it.
        await using (var ben = await garden.SessionAsync(data.Ben))
        {
            (await ben.ListAsync<string>($"""UPDATE {Gardens}"PlotRoles" SET "Name" = 'Grower' WHERE "Id" = $1 RETURNING "Name" """, data.MeadowTender.Id.Value)).Should().Equal("Grower");
            (await ben.ListAsync<string>($"""UPDATE {Gardens}"PlotRoles" SET "Status" = 'Archived' WHERE "Id" = $1 RETURNING "Status" """, data.MeadowTender.Id.Value)).Should().Equal("Archived");
            await ben.CommitAsync();
        }

        (await garden.Services.ReadAsync(data.MeadowTender.Id)).Should().Match<PlotRole>(role => role.MadeFrom == PlotMembership.Tender && role.Name == "Grower" && role.Status == CustomRoleStatus.Archived);
    }

    [Fact]
    public async Task What_is_held_through_a_role_of_plots_is_one_statement_under_the_policies_and_reads_the_role_rows()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres, asUser: true);
        var data = garden.Scenario;
        var commands = garden.Services.Commands;
        var precision = TimeSpan.FromMilliseconds(1);

        async Task<MemberHold<PlotId>?> AskAsync(GardenPerson person, PlotId plot, string permission)
        {
            commands.Reset();
            var held = await garden.Services.PlotsAsync(person, access => access.HoldAsync(plot, permission, Cancellation));
            commands.Count.Should().Be(1, "how " + permission + " is held, until when, and the plot's version are one statement");
            return held;
        }

        // Through a role of the garden's, for as long as the role is held; the role rows are in the statement.
        var water = await AskAsync(data.Cy, data.Beans, PlotPermissions.Water);
        water!.Via.Should().Be(MemberVia.Members);
        water.Until.Should().BeCloseTo(data.InThreeDays, precision);
        commands.Commands[0].Should().Contain("gardens.\"PlotGardeners\"").And.Contain("gardens.\"PlotGardenerRoles\"").And.Contain("gardens.\"PlotRoles\"");

        // As the owner, through a role put away, through no role, and on a plot that is not there for the caller: as on SQLite.
        var sell = await AskAsync(data.Ada, data.Beans, PlotPermissions.Sell);
        (sell!.Via, sell.Until).Should().Be((MemberVia.Members, (DateTimeOffset?)null));
        (await AskAsync(data.Eve, data.Beans, PlotPermissions.Fence))!.Via.Should().Be(MemberVia.Members);
        (await AskAsync(data.Fay, data.Beans, PlotPermissions.Plant))!.Via.Should().BeNull("the seasonal hand was put away");
        (await AskAsync(data.Di, data.Beans, PlotPermissions.Water))!.Via.Should().BeNull();
        (await AskAsync(data.Hal, data.Kale, PlotPermissions.Water))!.Via.Should().BeNull("the orchard took watering out of its tender's role");
        (await AskAsync(data.Hal, data.Kale, PlotPermissions.Plant))!.Via.Should().Be(MemberVia.Members);
        (await AskAsync(data.Hal, data.Beans, PlotPermissions.See)).Should().BeNull();

        // The permissions held on many, and a page of the host's own with a reach in it: one statement each.
        commands.Reset();
        var permissions = await garden.Services.PlotsAsync(data.Ben, access => access.PermissionsOnAsync([data.Beans, data.Leeks, data.Kale], GardenScenario.PermissionsAsked, Cancellation));
        commands.Count.Should().Be(1);
        permissions.Keys.Should().BeEquivalentTo([data.Beans, data.Leeks]);
        permissions[data.Beans].Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Water]);
        permissions[data.Leeks].Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Water, PlotPermissions.Fence, PlotPermissions.Sell]);

        commands.Reset();
        var watered = await garden.Services.AsAsync(data.Ada, async provider =>
        {
            var access = provider.GetRequiredService<MembershipAccessManager<Plot>>();
            return await provider.GetRequiredService<GardenContext>().Plots
                .Within(access.Reach(PlotPermissions.See))
                .Within(access.Reach(PlotPermissions.Water))
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
        using var garden = await PostgresGarden.CreateAsync(postgres, asUser: true);
        var data = garden.Scenario;
        var tender = data.MeadowTender.Id.Value;

        async Task<List<Guid>> FencedByBenAsync()
        {
            await using var session = await garden.SessionAsync(data.Ben);
            return await session.ListAsync<Guid>($"SELECT id FROM {Gardens}plots_where_i_hold($1) AS id", PlotPermissions.Fence);
        }

        // Ben tends the beans: the meadow's tender plants and waters, and does not fence.
        var before = await FencedByBenAsync();
        before.Should().NotContain(data.Beans.Value);

        // A role's row says what everybody who holds the role may do. Straight on the database, as himself,
        // he gives the role he holds every permission a role can give, puts a role that was put away back in use, and
        // makes a role of his own: the host's rule lets a signed-in user read the roles, and nobody write them.
        await using (var session = await garden.SessionAsync(data.Ben))
        {
            (await session.ListAsync<long>(
                $"WITH changed AS (UPDATE {Gardens}\"PlotRoles\" SET \"Permissions\" = ARRAY['plots.see', 'plots.plant', 'plots.water', 'plots.fence'] WHERE \"Id\" = $1 RETURNING 1) SELECT count(*) FROM changed",
                tender)).Should().Equal([0L], "no rule lets a caller change a role, so there is no row for him to change");
            (await session.ListAsync<long>(
                $"WITH changed AS (UPDATE {Gardens}\"PlotRoles\" SET \"Status\" = 'Active' WHERE \"Id\" = $1 RETURNING 1) SELECT count(*) FROM changed",
                data.Seasonal.Id.Value)).Should().Equal(0L);
            (await session.ListAsync<long>($"WITH gone AS (DELETE FROM {Gardens}\"PlotRoles\" RETURNING 1) SELECT count(*) FROM gone")).Should().Equal(0L);
        }

        await using (var session = await garden.SessionAsync(data.Ben))
        {
            (await FluentActions.Awaiting(() => session.ListAsync<object>(
                    $"INSERT INTO {Gardens}\"PlotRoles\" (\"Id\", \"GardenId\", \"Name\", \"Description\", \"Permissions\", \"Status\", \"MadeFrom\", \"Version\") "
                    + "SELECT pg_catalog.gen_random_uuid(), \"GardenId\", 'Everything', '', ARRAY['plots.fence'], 'Active', NULL, 1 "
                    + $"FROM {Gardens}\"PlotRoles\" WHERE \"Id\" = $1",
                    tender)).Should().ThrowAsync<Npgsql.PostgresException>())
                .Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
        }

        // Somebody who did not sign in reads no role and writes none.
        await using (var nobody = await UserSession.BeginAsync(garden.Database, CurrentUserSnapshot.Anonymous, role: "anon"))
        {
            (await nobody.ListAsync<long>($"SELECT count(*) FROM {Gardens}\"PlotRoles\"")).Should().Equal(0L);
            (await nobody.ListAsync<long>(
                $"WITH changed AS (UPDATE {Gardens}\"ShedRoles\" SET \"Permissions\" = ARRAY['sheds.sell'] RETURNING 1) SELECT count(*) FROM changed")).Should().Equal(0L);
        }

        // The rows are what they were, and so is what he holds: in the database and in the application.
        (await garden.Services.ReadAsync(data.MeadowTender.Id)).Permissions.Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Water]);
        (await garden.Services.ReadAsync(data.Seasonal.Id)).Status.Should().Be(CustomRoleStatus.Archived);
        (await FencedByBenAsync()).Should().BeEquivalentTo(before);
        (await garden.Services.PlotsAsync(data.Ben, access => access.HoldAsync(data.Beans, PlotPermissions.Fence, Cancellation)))!.Via.Should().BeNull();

        // And the roles are read as before, under the rule: what the member manager asks and the questions run as the caller.
        await garden.Services.AsAsync(data.Ben, async provider =>
        {
            await Admitted.RoleAsync<PlotId, PlotRoleId>(provider, PlotMembership.Codes, data.MeadowTender.Id, Cancellation);
            return true;
        });
        (await garden.Services.PlotsAsync(data.Ben, access => access.HoldAsync(data.Beans, PlotPermissions.Plant, Cancellation)))!.Via.Should().Be(MemberVia.Members);
    }

    [Fact]
    public void The_functions_read_a_roles_permissions_from_the_role_table_and_ask_no_function_for_them()
    {
        using var context = PostgresGarden.Model();

        var plots = new MembershipRowAccessContribution<PlotGardener>(PlotMembership.Rules).Contribute(context, Export)!;

        // Nothing is allowed by the package, and no policy is written for the role table: the lock the plots'
        // permissions ask for is on the gardeners' two tables and on the plot's owner. On the role table the trigger
        // an earlier form wrote to keep an owner's role in use is taken away, and nothing else is written.
        plots.Policies.Should().OnlyContain(policy => policy.Restrictive && policy.Table.ClrType != typeof(PlotRole));
        plots.Statements.Where(statement => statement.Contains("PlotRoles")).Should().OnlyContain(statement => statement.Contains("plots_owner_role_stays"));
        plots.Functions.Select(function => (function.Name, function.Parameters, function.Returns)).Should().Equal(
            ("plots_as_member", "", "SETOF uuid"),
            ("plots_as_member_with", "text", "SETOF uuid"),
            ("plots_i_see", "", "SETOF uuid"),
            ("plots_where_i_hold", "text", "SETOF uuid"),
            ("plots_fellow_members", "", "SETOF uuid"));

        // A role held now, in a membership that applies now, whose row is in use and holds the permission: the role
        // table of the same context, joined to the roles a gardener holds, for a permission a member's role can give.
        var withARole = plots.Functions[1].Body;
        withARole.Should().Contain(
            "\n  AND $1 IN ('plots.see', 'plots.plant', 'plots.water', 'plots.fence')"
            + "\n  AND EXISTS (SELECT 1 FROM \"gardens\".\"PlotGardenerRoles\" h"
            + "\n              JOIN \"gardens\".\"PlotRoles\" k ON k.\"Id\" = h.\"RoleId\""
            + "\n              WHERE h.\"PlotId\" = m.\"PlotId\" AND h.\"PlotGardenerId\" = m.\"Id\" AND ");
        withARole.Should().EndWith("\n                AND k.\"Status\" = 'Active' AND $1 = ANY (k.\"Permissions\"))");

        // The starter roles are in no function: they are rows like any other role once they are made.
        withARole.Should().NotContain("VALUES").And.NotContain(PlotMembership.Tender).And.NotContain(PlotMembership.Waterer);

        // No function is asked but the plots' own, and the caller is who its token says.
        foreach (var function in plots.Functions)
        {
            LogicalName().Matches(function.Body).Select(match => match.Groups["owner"].Value).Where(owner => owner != "plots").Should().BeEmpty();
            function.Body.Should().NotContain("\r");
            function.Body.Split('\n')[0].Should().MatchRegex("^-- Membership of plots \\(Plot\\), in form [0-9]+, written from the rules [0-9a-f]{64}$");
        }

        LogicalName().Matches(plots.Functions[3].Body).Select(match => match.Value).Should().Equal("{fn:plots/plots_as_member_with}", "{fn:plots/plots_as_member}");
        plots.Functions[0].Body.Should().Contain("m.\"MemberId\" = {user:uid} AND ");
        plots.Functions[3].Body.Should().Contain("$1 IN ('plots.see', 'plots.plant', 'plots.water', 'plots.fence', 'plots.sell') AND r.\"OwnerId\" = {user:uid}");

        // What a role can give stands in front of what the rows say, as the rules say it.
        string Cut(OtherPermissions permissions) => new MembershipRowAccessContribution<PlotGardener>(Plots(otherPermissions: permissions)).Contribute(context, Export)!.Functions[1].Body;
        Cut(OtherPermissions.AllBut("plots.it's")).Should().Contain("\n  AND $1 NOT IN ('plots.sell', 'plots.it''s')\n  AND EXISTS (");
        Cut(OtherPermissions.All).Should().Contain("\n  AND $1 NOT IN ('plots.sell')\n").And.Contain("$1 = ANY (k.\"Permissions\")");

        // Rules under which a role could give nothing are no rules for roles a customer makes: refused where they are said.
        FluentActions.Invoking(() => Plots(permissions: [new(PlotPermissions.See, EveryMember: true, InRoles: false)], starters: [])).Should().Throw<ArgumentException>().WithParameterName("roles");
    }

    [Fact]
    public void Roles_kept_for_a_resource_are_written_with_whatever_else_the_rules_say()
    {
        // Where the roles come from stands by itself: kept roles for members the host resolves, on a resource
        // that is reached from above. The host's functions are asked for the two things it answers, by the
        // logical names the rules give, and for the roles nothing is asked: they are read from their table.
        using var context = new PlotsInGardens(new DbContextOptionsBuilder<PlotsInGardens>().UseNpgsql("Host=model-only").Options);
        var rules = MembershipRules.Declare(
            typeof(Plot),
            "plots",
            PlotMembership.Rules.Permissions,
            Roles.Custom(starters: PlotMembership.Rules.Roles.Declared),
            members: MemberSource.Resolved(new DatabaseFunction("gardens", "current_gardener")),
            reachFromAbove: new DatabaseFunction("gardens", "gardens_where_i_hold"));

        var functions = new MembershipRowAccessContribution<PlotGardener>(rules).Contribute(context, Export)!.Functions;

        functions[0].Body.Should().Contain("m.\"MemberId\" = (SELECT {fn:gardens/current_gardener}()) AND ");
        functions[1].Body.Should()
            .Contain("m.\"MemberId\" = (SELECT {fn:gardens/current_gardener}()) AND ")
            .And.Contain("\n  AND $1 IN ('plots.see', 'plots.plant', 'plots.water', 'plots.fence')\n  AND EXISTS (SELECT 1 FROM ")
            .And.Contain(" k ON k.\"Id\" = h.\"RoleId\"")
            .And.EndWith("AND k.\"Status\" = 'Active' AND $1 = ANY (k.\"Permissions\"))");
        functions[2].Body.Should().Contain("r.\"GardenId\" IN (SELECT reached.place FROM {fn:gardens/gardens_where_i_hold}('plots.see') AS reached(place))");
        functions[3].Body.Should()
            .Contain("r.\"GardenId\" IN (SELECT reached.place FROM {fn:gardens/gardens_where_i_hold}($1) AS reached(place))")
            .And.Contain("r.\"OwnerId\" = (SELECT {fn:gardens/current_gardener}())");

        // No function is asked for the roles: the two the rules name, and the plots' own, are all there is.
        functions.SelectMany(function => LogicalName().Matches(function.Body).Select(match => match.Value)).Distinct()
            .Should().BeEquivalentTo("{fn:gardens/current_gardener}", "{fn:gardens/gardens_where_i_hold}", "{fn:plots/plots_as_member}", "{fn:plots/plots_as_member_with}");

        // And what the functions are written from says all three: other than the plots' own rules, and other than the same without one of them.
        static string MarkerOf(MembershipRules written, DbContext model)
            => new MembershipRowAccessContribution<PlotGardener>(written).Contribute(model, Export)!.Functions[0].Body.Split('\n')[0];
        var withoutAbove = MembershipRules.Declare(
            typeof(Plot),
            "plots",
            PlotMembership.Rules.Permissions,
            Roles.Custom(starters: PlotMembership.Rules.Roles.Declared),
            members: MemberSource.Resolved(new DatabaseFunction("gardens", "current_gardener")));
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
        // them: another one, or other permissions for one, leaves the database's functions the ones the rules say.
        MarkerOf(Plots(starters: [new(PlotMembership.Tender, [PlotPermissions.See, PlotPermissions.Plant]), new("weeder", [PlotPermissions.See])]), context).Should().Be(plots);
        MarkerOf(Plots(starters: []), context).Should().Be(plots);

        // What a role can give is cut inside the functions, so it is part of what they are written from.
        MarkerOf(Plots(otherPermissions: OtherPermissions.All), context).Should().NotBe(plots);
        MarkerOf(Plots(otherPermissions: OtherPermissions.AllBut(ToolPermissions.LendTools)), context).Should().NotBe(plots);

        // And so is every permission of the plot, and which of them its owner holds, as for any resource.
        MarkerOf(Plots(permissions: [new(PlotPermissions.See, EveryMember: true), new(PlotPermissions.Plant), new(PlotPermissions.Water)]), context).Should().NotBe(plots);
        MarkerOf(Plots(permissions: [.. PlotMembership.Rules.Permissions.Select(permission => permission.Name == PlotPermissions.Plant ? permission with { Owner = false } : permission)]), context).Should().NotBe(plots);
    }

    [Fact]
    public void Rules_that_keep_the_roles_are_refused_where_the_model_has_no_role_table_the_functions_can_read()
    {
        // No role class is mapped for the plots.
        using var bare = new PlotsWithoutRoles(new DbContextOptionsBuilder<PlotsWithoutRoles>().UseNpgsql("Host=model-only").Options);
        FluentActions.Invoking(() => new MembershipRowAccessContribution<PlotGardener>(PlotMembership.Rules).Contribute(bare, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules 'plots' say a customer makes the roles of Plot, and the model maps no role class for it.*[CustomRole<TRoleId, Plot>]*modelBuilder.Entity<PlotRole>().IsCustomRole().");

        // The permissions are kept as something the functions do not read.
        using var documents = new PlotsWithPermissionsAsJson(new DbContextOptionsBuilder<PlotsWithPermissionsAsJson>().UseNpgsql("Host=model-only").Options);
        FluentActions.Invoking(() => new MembershipRowAccessContribution<PlotGardener>(PlotMembership.Rules).Contribute(documents, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("PlotRole.Permissions is stored as jsonb, and the functions read a role's permissions from an array, such as text[]. Leave the column as IsCustomRole maps it.");

        // A role class known by another id than the gardeners hold their roles by: nothing to join the two on.
        using var stray = new PlotsWithStrayRoles(new DbContextOptionsBuilder<PlotsWithStrayRoles>().UseNpgsql("Host=model-only").Options);
        FluentActions.Invoking(() => new MembershipRowAccessContribution<PlotGardener>(PlotMembership.Rules).Contribute(stray, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The members of Plot hold roles known by PlotRoleId, and its role class *StrayRole is known by GardenId.*declare the member class with GardenId as its role.");

        // Rules that declare the roles, for gardeners that hold a role by an id.
        using var context = PostgresGarden.Model();
        FluentActions.Invoking(() => new MembershipRowAccessContribution<PlotGardener>(
                MembershipRules.Declare(typeof(Plot), "plots", [new(PlotPermissions.See)], Roles.Fixed([new("reader", [PlotPermissions.See])]))).Contribute(context, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The members of Plot hold roles known by PlotRoleId, and the rules 'plots' have fixed roles, which a member holds by name.*Roles.Custom() for a role class of the resource*");
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
            .And.Contain("\"gardens\".plots_as_member (missing), \"gardens\".plots_as_member_with (missing), \"gardens\".plots_fellow_members (missing), \"gardens\".plots_i_see (missing), \"gardens\".plots_where_i_hold (missing)")
            .And.Contain("[MembershipRules<PlotGardener>]");

        // Written from rules that let a member's role give another permission: the database would answer otherwise than the application.
        await plain.ExecuteAsync(PostgresGarden.AccessScript([new MembershipRowAccessContribution<PlotGardener>(Plots(otherPermissions: OtherPermissions.All)), new MembershipRowAccessContribution<ShedHand>(ShedMembership.Rules), new GardenOwnFunctions()]));
        (await FluentActions.Awaiting(() => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(plain.Services.Provider, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("\"gardens\".plots_as_member_with (it was written from other rules)");

        // Written from rules that differ in their starter roles alone: the functions are the same, and it starts.
        await plain.ExecuteAsync(PostgresGarden.AccessScript([new MembershipRowAccessContribution<PlotGardener>(Plots(starters: [new("weeder", [PlotPermissions.See])])), new MembershipRowAccessContribution<ShedHand>(ShedMembership.Rules), new GardenOwnFunctions()]));
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
                "The rules 'plots' say a customer makes the roles of Plot, and PlotsWithoutRoles maps no role class for it, so its functions cannot be the ones the rules say. "
                + "Map the role class where the context builds its model: modelBuilder.Entity<PlotRole>().IsCustomRole().");
    }

    /// <summary>
    /// For everybody and every permission: the four functions, asked past the application; the access questions, asked
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
                var see = provider.GetRequiredService<MembershipAccessManager<Plot>>().ReachSeen();
                return await provider.GetRequiredService<GardenContext>().Plots.Within(see).Select(plot => plot.Id.Value).ToListAsync(Cancellation);
            });
            var seen = data.PlotsSeenBy(person).Select(id => id.Value).ToList();
            (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.Seen}() AS id")).Should().BeEquivalentTo(seen, "of what " + person + " sees");
            (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMember}() AS id")).Should().BeEquivalentTo(seen);
            seenInCSharp.Should().BeEquivalentTo(seen);

            foreach (var permission in GardenScenario.PermissionsAsked)
            {
                var heldInCSharp = await garden.Services.AsAsync(person, async provider =>
                {
                    var reach = provider.GetRequiredService<MembershipAccessManager<Plot>>().Reach(permission);
                    return await provider.GetRequiredService<GardenContext>().Plots.Within(reach).Select(plot => plot.Id.Value).ToListAsync(Cancellation);
                });
                var held = data.PlotsHeldBy(person, permission).Select(id => id.Value).ToList();
                var because = "of what " + person + " holds " + permission + " on";

                (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.HeldOn}($1) AS id", permission)).Should().BeEquivalentTo(held, because);
                heldInCSharp.Should().BeEquivalentTo(held, because);

                // Through a role alone: one of the garden's in use, for a permission a member's role can give.
                (await session.ListAsync<Guid>($"SELECT id FROM {Gardens}{functions.AsMemberWith}($1) AS id", permission))
                    .Should().BeEquivalentTo(data.PlotsWithARoleFor(person, permission).Select(id => id.Value), because + " through a role");
            }
        }
    }

    /// <summary>
    /// The plots' rules, said again entry for entry, with other starter roles, other permissions a role may give, or other
    /// permissions.
    /// </summary>
    private static MembershipRules Plots(IReadOnlyList<DeclaredRole>? starters = null, OtherPermissions? otherPermissions = null, IReadOnlyList<ResourcePermission>? permissions = null)
        => MembershipRules.Declare(
            typeof(Plot),
            PlotMembership.Rules.Name,
            permissions ?? PlotMembership.Rules.Permissions,
            Roles.Custom(starters ?? [new(PlotMembership.Tender, [PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Water]), new(PlotMembership.Waterer, [PlotPermissions.See, PlotPermissions.Water])], otherPermissions),
            systemScopes: PlotMembership.Rules.SystemScopes,
            codes: PlotMembership.Codes);

    [GeneratedRegex(@"\{fn:(?<owner>[^/}]+)/[^}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalName();

    /// <summary>A context that maps the plots with their gardeners, and no role class for them.</summary>
    private sealed class PlotsWithoutRoles(DbContextOptions<PlotsWithoutRoles> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(GardenContext.Schema);
            modelBuilder.Entity<Plot>().HasMembers(plot => plot.Gardeners);
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
            modelBuilder.Entity<Plot>().HasMembers(plot => plot.Gardeners);
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

        public List<string> Permissions { get; set; } = [];

        public CustomRoleStatus Status { get; set; }
    }

    /// <summary>A context that maps the plots as sitting in their garden, and their role class.</summary>
    private sealed class PlotsInGardens(DbContextOptions<PlotsInGardens> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Plot>().HasMembers(plot => plot.Gardeners);
            modelBuilder.Entity<PlotRole>().IsCustomRole();
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }

    /// <summary>A context that maps the plots and their role class, and then keeps a role's permissions as a JSON document.</summary>
    private sealed class PlotsWithPermissionsAsJson(DbContextOptions<PlotsWithPermissionsAsJson> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Plot>().HasMembers(plot => plot.Gardeners);
            modelBuilder.Entity<PlotRole>(role =>
            {
                role.IsCustomRole();
                role.PrimitiveCollection(row => row.Permissions).HasColumnType("jsonb");
            });
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }
}
