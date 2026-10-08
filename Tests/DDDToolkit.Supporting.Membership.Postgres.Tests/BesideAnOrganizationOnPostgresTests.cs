using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using Npgsql;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// The resources that stand beside the depot, on Postgres: the four functions of each answer what the access
/// questions answer in C#, for the same data, and what the rules say read over the aggregates and the depot's
/// rows in memory. The functions ask the depot's own by the logical names the rules give, and those are all
/// that joins the package's SQL to the depot's.
/// </summary>
public sealed class BesideAnOrganizationOnPostgresTests(FilingPostgres postgres)
{
    private const string Depot = DepotContext.Schema + ".";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_functions_of_the_crates_answer_what_the_access_questions_answer()
    {
        using var depot = await PostgresDepot.CreateAsync(postgres);
        var data = depot.Scenario;
        var functions = CrateMembership.Rules.Functions;

        foreach (var person in data.People)
        {
            await using var session = await depot.SessionAsync(person);

            // The crates the caller is on, and those it sees: on a crate, or holding the key that sees where it stands.
            var seenInCSharp = await depot.Services.AsAsync(person.Caller, async provider =>
            {
                var see = provider.GetRequiredService<IMemberQuestions<CrateId>>().KeyReach([]).See;
                return await provider.GetRequiredService<DepotContext>().Crates.Within(see).Select(crate => crate.Id.Value).ToListAsync(Cancellation);
            });
            var seen = data.CratesSeenBy(person).Select(id => id.Value).ToList();
            (await session.ListAsync<Guid>($"SELECT id FROM {Depot}{functions.Seen}() AS id")).Should().BeEquivalentTo(seen, "of what " + person + " sees");
            (await session.ListAsync<Guid>($"SELECT id FROM {Depot}{functions.AsMember}() AS id")).Should().BeEquivalentTo(data.CratesAsMember(person).Select(id => id.Value), "of the crates " + person + " is on");
            seenInCSharp.Should().BeEquivalentTo(seen);

            foreach (var key in DepotScenario.CrateKeysAsked)
            {
                var heldInCSharp = await depot.Services.AsAsync(person.Caller, async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<CrateId>>().Reach(key);
                    return await provider.GetRequiredService<DepotContext>().Crates.Within(reach).Select(crate => crate.Id.Value).ToListAsync(Cancellation);
                });
                var held = data.CratesHeldBy(person, key).Select(id => id.Value).ToList();
                var because = "of what " + person + " holds " + key + " on";

                (await session.ListAsync<Guid>($"SELECT id FROM {Depot}{functions.HeldOn}($1) AS id", key)).Should().BeEquivalentTo(held, because);
                heldInCSharp.Should().BeEquivalentTo(held, because);

                // Through a role alone: one of the depot's in use, for a key a member's role can give.
                (await session.ListAsync<Guid>($"SELECT id FROM {Depot}{functions.AsMemberWith}($1) AS id", key))
                    .Should().BeEquivalentTo(data.CratesWithARoleFor(person, key).Select(id => id.Value), because + " through a role");
            }
        }
    }

    [Fact]
    public async Task The_functions_of_the_pallets_answer_what_the_access_questions_answer()
    {
        using var depot = await PostgresDepot.CreateAsync(postgres);
        var data = depot.Scenario;
        var functions = PalletMembership.Rules.Functions;

        foreach (var person in data.People)
        {
            await using var session = await depot.SessionAsync(person);

            var seen = data.PalletsSeenBy(person).Select(id => id.Value).ToList();
            (await session.ListAsync<Guid>($"SELECT id FROM {Depot}{functions.Seen}() AS id")).Should().BeEquivalentTo(seen, "of what " + person + " sees");
            (await session.ListAsync<Guid>($"SELECT id FROM {Depot}{functions.AsMember}() AS id")).Should().BeEquivalentTo(seen);

            foreach (var key in DepotScenario.PalletKeysAsked)
            {
                var heldInCSharp = await depot.Services.AsAsync(person.Caller, async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<PalletId>>().Reach(key);
                    return await provider.GetRequiredService<DepotContext>().Pallets.Within(reach).Select(pallet => pallet.Id.Value).ToListAsync(Cancellation);
                });
                var held = data.PalletsHeldBy(person, key).Select(id => id.Value).ToList();
                var because = "of what " + person + " holds " + key + " on";

                (await session.ListAsync<Guid>($"SELECT id FROM {Depot}{functions.HeldOn}($1) AS id", key)).Should().BeEquivalentTo(held, because);
                heldInCSharp.Should().BeEquivalentTo(held, because);
                (await session.ListAsync<Guid>($"SELECT id FROM {Depot}{functions.AsMemberWith}($1) AS id", key))
                    .Should().BeEquivalentTo(data.PalletsWithARoleFor(person, key).Select(id => id.Value), because + " through a role");
            }
        }
    }

    [Fact]
    public async Task An_owner_whose_own_place_has_not_begun_is_answered_by_the_functions_what_the_access_questions_answer()
    {
        using var depot = await PostgresDepot.CreateAsync(postgres);
        var data = depot.Scenario;
        var barley = CrateId.CreateSequential();

        // The barley names Dot as its owner today, and her place on it begins tomorrow. She is on the tea now.
        await depot.Services.ChangeAsync((context, _) =>
        {
            context.Crates.Add(new Crate(barley, "Barley", data.RightBay, data.Dot.Porter!.Value, data.Lead, data.Now.AddDays(1)));
            return Task.CompletedTask;
        });

        // She sees the barley by owning it, holds scrapping on it by owning it, and the key that sees it only as
        // one on it, which she is not yet.
        await using (var session = await depot.SessionAsync(data.Dot))
        {
            (await session.ListAsync<Guid>($"SELECT id FROM {Depot}crates_i_see() AS id")).Should().BeEquivalentTo([data.Tea.Value, barley.Value]);
            (await session.ListAsync<Guid>($"SELECT id FROM {Depot}crates_where_i_hold($1) AS id", CrateKeys.Scrap)).Should().Equal(barley.Value);
            (await session.ListAsync<Guid>($"SELECT id FROM {Depot}crates_where_i_hold($1) AS id", CrateKeys.See)).Should().Equal(data.Tea.Value);
        }

        var keys = await depot.Services.CratesAsync(data.Dot, access => access.KeysOnAsync([barley, data.Tea], [CrateKeys.See, CrateKeys.Scrap], Cancellation));
        keys[barley].Should().BeEquivalentTo([CrateKeys.Scrap], "what the functions answer for each key");
        keys[data.Tea].Should().BeEquivalentTo([CrateKeys.See]);
    }

    [Fact]
    public async Task A_porter_the_depot_let_go_and_somebody_it_never_took_on_are_answered_nothing_by_the_database_too()
    {
        using var depot = await PostgresDepot.CreateAsync(postgres);
        var data = depot.Scenario;

        // Ned's rows all still apply, on a pallet, on a crate and at the hall; the depot's function answers
        // nobody for him, as the host does in C#. Ivo's user id is no porter's.
        foreach (var person in new[] { data.Ned, data.Ivo })
        {
            await using var session = await depot.SessionAsync(person);
            foreach (var functions in new[] { PalletMembership.Rules.Functions, CrateMembership.Rules.Functions })
            {
                (await session.ListAsync<Guid>($"SELECT id FROM {Depot}{functions.Seen}() AS id")).Should().BeEmpty("of " + person);
                (await session.ListAsync<Guid>($"SELECT id FROM {Depot}{functions.AsMember}() AS id")).Should().BeEmpty();
                foreach (var key in DepotScenario.PalletKeysAsked.Concat(DepotScenario.CrateKeysAsked))
                {
                    (await session.ListAsync<Guid>($"SELECT id FROM {Depot}{functions.HeldOn}($1) AS id", key)).Should().BeEmpty();
                    (await session.ListAsync<Guid>($"SELECT id FROM {Depot}{functions.AsMemberWith}($1) AS id", key)).Should().BeEmpty();
                }
            }
        }

        // Taken on again, the database counts him again, with no file written anew: the function asks the depot's rows.
        await depot.Services.ChangeAsync(async (context, desk) =>
        {
            var porter = await context.Porters.SingleAsync(candidate => candidate.Id == data.Ned.Porter, Cancellation);
            porter.Active = true;
            desk.TakeOn(porter);
        });
        await using var again = await depot.SessionAsync(data.Ned);
        (await again.ListAsync<Guid>($"SELECT id FROM {Depot}crates_i_see() AS id")).Should().BeEquivalentTo([data.Tea.Value, data.Salt.Value, data.Rice.Value]);
        (await again.ListAsync<Guid>($"SELECT id FROM {Depot}crates_where_i_hold($1) AS id", CrateKeys.Pack)).Should().Equal(data.Tea.Value);
        (await again.ListAsync<Guid>($"SELECT id FROM {Depot}pallets_where_i_hold($1) AS id", PalletKeys.Load)).Should().Equal(data.Pears.Value);
    }

    [Fact]
    public async Task The_keyed_functions_answer_nothing_and_ask_nothing_for_no_key()
    {
        using var depot = await PostgresDepot.CreateAsync(postgres);
        var data = depot.Scenario;

        // The depot's two functions that take a key, written again so that they say when they are asked
        // without one. The resources' functions run as their owner and ask these as that owner: a depot that
        // answers for its caller by a condition of its own is asked nothing it has no key to answer for.
        foreach (var function in new[] { "roles_with_key", "bays_where_i_hold" })
        {
            await depot.ExecuteAsync(
                $"""
                CREATE OR REPLACE FUNCTION {Depot}{function}(text) RETURNS SETOF uuid LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = '' AS $body$
                BEGIN
                    IF $1 IS NULL THEN RAISE EXCEPTION 'asked without a key'; END IF;
                    RETURN;
                END
                $body$
                """);
            (await FluentActions.Awaiting(() => depot.ListAsync<Guid>($"SELECT asked FROM {Depot}{function}(NULL) AS asked")).Should().ThrowAsync<PostgresException>())
                .Which.MessageText.Should().Be("asked without a key");
        }

        // Asked without a key, as a porter on a crate and as one that holds keys above: no row, and no word from the depot.
        foreach (var person in new[] { data.Bo, data.Eli, data.Ana })
        {
            await using var session = await depot.SessionAsync(person);
            (await session.ListAsync<Guid>($"SELECT id FROM {Depot}crates_where_i_hold(NULL) AS id")).Should().BeEmpty("of " + person);
            (await session.ListAsync<Guid>($"SELECT id FROM {Depot}crates_as_member_with(NULL) AS id")).Should().BeEmpty();
            (await session.ListAsync<Guid>($"SELECT id FROM {Depot}pallets_where_i_hold(NULL) AS id")).Should().BeEmpty();
            (await session.ListAsync<Guid>($"SELECT id FROM {Depot}pallets_as_member_with(NULL) AS id")).Should().BeEmpty();
        }

        // Which is what the bodies say: a condition without a column in front of what they read and ask.
        var bodies = await depot.ListAsync<string>(
            $"SELECT p.proname || ': ' || p.prosrc FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = '{DepotContext.Schema}' AND p.proname LIKE 'crates_%' ORDER BY 1");
        bodies.Single(body => body.StartsWith("crates_as_member_with:", StringComparison.Ordinal)).Should().Contain("WHERE $1 IS NOT NULL AND m.\"MemberId\" = ");
        bodies.Single(body => body.StartsWith("crates_where_i_hold:", StringComparison.Ordinal)).Should().Contain("WHERE $1 IS NOT NULL AND r.\"BayId\" IN (SELECT reached.place FROM depot.bays_where_i_hold($1) AS reached(place))");
    }

    [Fact]
    public async Task What_is_held_on_a_crate_is_one_statement_under_the_policies_and_answers_what_it_answers_without_them()
    {
        using var depot = await PostgresDepot.CreateAsync(postgres, asCaller: true);
        var data = depot.Scenario;
        var commands = depot.Services.Commands;
        var precision = TimeSpan.FromMilliseconds(1);

        async Task<MemberHold<CrateId>?> AskAsync(DepotPerson person, CrateId crate, string key)
        {
            commands.Reset();
            var held = await depot.Services.CratesAsync(person, access => access.HoldAsync(crate, key, Cancellation));
            commands.Count.Should().Be(1, "how " + key + " is held, until when, and the crate's version are one statement");
            return held;
        }

        // As a member through a role of the depot's, as the owner, from above, both ways, and not at all: as on SQLite.
        var print = await AskAsync(data.Bo, data.Tea, CrateKeys.PrintLabels);
        print!.Via.Should().Be(MemberVia.Members);
        print.Until.Should().BeCloseTo(data.InThreeDays, precision);
        commands.Commands[0].Should().Contain("depot.\"CratePorters\"").And.Contain("depot.\"RoleKeys\"").And.Contain("depot.\"BayPaths\"");

        var scrap = await AskAsync(data.Ana, data.Tea, CrateKeys.Scrap);
        (scrap!.Via, scrap.Until).Should().Be((MemberVia.Members, (DateTimeOffset?)null));
        var move = await AskAsync(data.Eli, data.Rice, CrateKeys.Move);
        (move!.Via, move.Until).Should().Be((MemberVia.Above, (DateTimeOffset?)null));
        var pack = await AskAsync(data.Bo, data.Tea, CrateKeys.Pack);
        (pack!.Via, pack.Until).Should().Be((MemberVia.Members, (DateTimeOffset?)null));
        (await AskAsync(data.Bo, data.Tea, CrateKeys.ManageDepot))!.Via.Should().BeNull("whatever the packer holds in the depot, no member's role gives the depot's own key");
        (await AskAsync(data.Cas, data.Tea, CrateKeys.See))!.Until.Should().BeCloseTo(data.NextWeek, precision);
        (await AskAsync(data.Eli, data.Salt, CrateKeys.See)).Should().BeNull();
        (await AskAsync(data.Ned, data.Tea, CrateKeys.See)).Should().BeNull();

        // The keys held on many, and a page of the host's own with a reach in it: one statement each.
        commands.Reset();
        var keys = await depot.Services.CratesAsync(data.Flo, access => access.KeysOnAsync([data.Tea, data.Salt, data.Rice], DepotScenario.CrateKeysAsked, Cancellation));
        commands.Count.Should().Be(1);
        keys.Values.Should().HaveCount(3).And.OnlyContain(held => held.SetEquals(new[] { CrateKeys.See, CrateKeys.Pack }));

        commands.Reset();
        var labels = await depot.Services.AsAsync(data.Eli.Caller, async provider =>
        {
            var seen = provider.GetRequiredService<IMemberQuestions<CrateId>>().Reach(CrateKeys.See);
            return await provider.GetRequiredService<DepotContext>().Crates.Within(seen).OrderBy(crate => crate.Label).Select(crate => crate.Label).ToListAsync(Cancellation);
        });
        labels.Should().Equal("Rice", "Tea");
        commands.Count.Should().Be(1);

        // Past the application, the policy lets a caller read what the function answers: seen from above too.
        await using var eli = await depot.SessionAsync(data.Eli);
        (await eli.ListAsync<string>($"SELECT \"Label\" FROM {Depot}\"Crates\" ORDER BY 1")).Should().Equal("Rice", "Tea");
        await using var ned = await depot.SessionAsync(data.Ned);
        (await ned.ListAsync<string>($"SELECT \"Label\" FROM {Depot}\"Crates\"")).Should().BeEmpty();
        (await ned.ListAsync<string>($"SELECT \"Label\" FROM {Depot}\"Pallets\"")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_key_held_from_above_is_held_for_as_long_as_the_caller_sees_the_crate_on_postgres_too()
    {
        using var depot = await PostgresDepot.CreateAsync(postgres);
        var data = depot.Scenario;
        var cas = data.Cas.Porter!.Value;
        var precision = TimeSpan.FromMilliseconds(1);

        // Cas is on the tea until next week and weighs in its bay, holding the key that sees at no bay: what he
        // holds on the tea from above he holds until he is off it.
        await depot.Services.ChangeAsync((context, _) =>
        {
            context.BayHolds.Add(new BayHold { PorterId = cas, BayId = data.LeftBay, Key = CrateKeys.Weigh });
            return Task.CompletedTask;
        });
        depot.Services.Commands.Reset();
        var weigh = await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, CrateKeys.Weigh, Cancellation));
        weigh!.Via.Should().Be(MemberVia.Above);
        weigh.Until.Should().BeCloseTo(data.NextWeek, precision);
        depot.Services.Commands.Count.Should().Be(1);

        // Seeing the tea from above as well, he holds it with no end known here.
        await depot.Services.ChangeAsync((context, _) =>
        {
            context.BayHolds.Add(new BayHold { PorterId = cas, BayId = data.Hall, Key = CrateKeys.See });
            return Task.CompletedTask;
        });
        var seen = await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, CrateKeys.Weigh, Cancellation));
        (seen!.Via, seen.Until).Should().Be((MemberVia.Above, (DateTimeOffset?)null));
    }

    [Fact]
    public async Task The_functions_ask_the_depots_own_by_the_names_they_have_where_they_are_defined()
    {
        using var depot = await PostgresDepot.CreateAsync(postgres, seed: false);

        var bodies = await depot.ListAsync<string>(
            $"""
            SELECT p.proname || ': ' || p.prosrc FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = '{DepotContext.Schema}' ORDER BY 1
            """);
        string Body(string name) => bodies.Single(body => body.StartsWith(name + ":", StringComparison.Ordinal));

        // Who the caller is as a member: the depot's answer, for a pallet and for a crate.
        Body("pallets_as_member").Should().Contain("m.\"MemberId\" = (SELECT depot.caller_porter())");
        Body("crates_as_member").Should().Contain("m.\"MemberId\" = (SELECT depot.caller_porter())");

        // Which roles give a key: the rules' own list for a pallet, and the depot's answer for a crate, behind
        // what the rules let a member's role give.
        Body("pallets_as_member_with").Should().Contain("('loader', 'pallets.load')").And.NotContain("roles_with_key");
        Body("crates_as_member_with").Should()
            .Contain("AND $1 NOT IN ('crates.move', 'depot.manage')")
            .And.Contain("h.\"RoleId\" IN (SELECT giving.role FROM depot.roles_with_key($1) AS giving(role))")
            .And.NotContain("VALUES");

        // Reach from above: where the crate sits, against where the caller holds the key. A pallet sits nowhere.
        Body("crates_i_see").Should().Contain("r.\"BayId\" IN (SELECT reached.place FROM depot.bays_where_i_hold('crates.see') AS reached(place))");
        Body("crates_where_i_hold").Should()
            .Contain("r.\"BayId\" IN (SELECT reached.place FROM depot.bays_where_i_hold($1) AS reached(place))")
            .And.Contain("$1 IN ('crates.scrap') AND r.\"OwnerId\" = (SELECT depot.caller_porter())");
        Body("pallets_i_see").Should().NotContain("bays_where_i_hold");
        Body("pallets_where_i_hold").Should().NotContain("bays_where_i_hold");

        // The depot's own are asked by the functions that run as their owner, and by nobody else: a caller
        // cannot ask the depot who it is, or which roles give what.
        var askable = await depot.ListAsync<string>(
            $"""
            SELECT p.proname FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = '{DepotContext.Schema}' AND pg_catalog.has_function_privilege('authenticated', p.oid, 'EXECUTE') ORDER BY 1
            """);
        askable.Should().Equal(
            "crates_as_member", "crates_as_member_with", "crates_i_see", "crates_where_i_hold",
            "pallets_as_member", "pallets_as_member_with", "pallets_i_see", "pallets_where_i_hold");

        await using var session = await depot.SessionAsync(new DepotPerson("anybody", Guid.CreateVersion7(), null, false));
        (await FluentActions.Awaiting(() => session.ListAsync<Guid>($"SELECT {Depot}caller_porter()")).Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public void A_function_the_rules_name_that_nothing_defines_is_refused_when_the_file_is_written()
    {
        // Written with a resource's contribution and without the depot's own, which the host forgot to list: the
        // names the rules give are defined nowhere in what the file is written with.
        FluentActions.Invoking(() => PostgresDepot.AccessScript([new MembershipRowAccessContribution<PalletPorter>(PalletMembership.Rules)], rules: []))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*pallets/pallets_as_member*asks the function depot/caller_porter, and none of the functions this is written with is called that*");

        FluentActions.Invoking(() => PostgresDepot.AccessScript([new MembershipRowAccessContribution<CratePorter>(CrateMembership.Rules)], rules: []))
            .Should().Throw<InvalidOperationException>().WithMessage("*asks the function depot/*");

        // A name the depot does not define, for one of the three: said for that one.
        var misnamed = new MembershipRules(
            "crates",
            keys: [CrateKeys.Scrap],
            members: MemberSource.Resolved(DepotFunctions.CallerPorter),
            seeKey: CrateKeys.See,
            ownerRole: CrateMembership.OwnerRole,
            memberKeys: MemberKeys.AllBut(CrateKeys.Move),
            rolesKeptElsewhere: new(DepotFunctions.RolesWithKey),
            above: new("depot/aisles_where_i_hold"));
        FluentActions.Invoking(() => PostgresDepot.AccessScript([new MembershipRowAccessContribution<CratePorter>(misnamed), new DepotOwnFunctions()], rules: []))
            .Should().Throw<InvalidOperationException>().WithMessage("*asks the function depot/aisles_where_i_hold, and none of the functions this is written with is called that*");

        // With the depot's own listed, the file is written, each function after the ones it asks.
        var script = PostgresDepot.AccessScript();
        script.IndexOf("FUNCTION depot.caller_porter()", StringComparison.Ordinal)
            .Should().BePositive().And.BeLessThan(script.IndexOf("FUNCTION depot.bays_where_i_hold(text)", StringComparison.Ordinal));
        script.IndexOf("FUNCTION depot.bays_where_i_hold(text)", StringComparison.Ordinal)
            .Should().BeLessThan(script.IndexOf("FUNCTION depot.crates_i_see()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_start_up_check_holds_the_database_to_what_the_rules_have_the_host_answer()
    {
        // As the rules say them: the check passes, for both resources.
        using (var depot = await PostgresDepot.CreateAsync(postgres, seed: false))
        {
            await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(depot.Services.Provider, Cancellation);
        }

        // Written from rules that differ in one thing the host answers, each of which the functions are written from.
        var registered = CrateMembership.Rules;
        (string What, MembershipRules Rules)[] others =
        [
            ("another function says who the caller is", Crates(members: MemberSource.Resolved("spare/acting_porter"))),
            ("the caller's own id is who it is", Crates(members: MemberSource.CallerId)),
            ("another function says which roles give a key", Crates(elsewhere: new("depot/bays_where_i_hold"))),
            ("a member's role gives other keys", Crates(memberKeys: MemberKeys.AllBut(CrateKeys.Move))),
            ("a member's role gives only what is listed", Crates(memberKeys: MemberKeys.Only(CrateKeys.Move, CrateKeys.ManageDepot))),
            ("another function says where a key is held", Crates(above: new("depot/roles_with_key"))),
            ("nothing above reaches a crate", Crates(reached: false)),
        ];

        foreach (var (what, rules) in others)
        {
            using var depot = await PostgresDepot.CreateAsync(postgres, secured: false, seed: false);
            await depot.ExecuteAsync(PostgresDepot.AccessScript(
                [new MembershipRowAccessContribution<PalletPorter>(PalletMembership.Rules), new MembershipRowAccessContribution<CratePorter>(rules), new DepotOwnFunctions(), new SpareFunction()],
                rules: []));

            var refused = await FluentActions.Awaiting(() => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(depot.Services.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>(what);
            refused.Which.Message.Should()
                .StartWith("The functions that answer the membership of Crate in the database are not as its rules, 'crates', say them: ")
                .And.Contain("depot.crates_as_member (it was written from other rules), depot.crates_as_member_with (it was written from other rules)")
                .And.NotContain("pallets_");
        }

        MembershipRules Crates(MemberSource? members = null, RolesKeptElsewhere? elsewhere = null, MemberKeys? memberKeys = null, ReachFromAbove? above = null, bool reached = true)
            => new(
                registered.Name,
                keys: registered.Keys,
                members: members ?? registered.Members,
                seeKey: registered.SeeKey,
                ownerRole: registered.OwnerRole,
                memberKeys: memberKeys ?? registered.MemberKeys,
                rolesKeptElsewhere: elsewhere ?? registered.RolesKeptElsewhere,
                above: reached ? above ?? registered.Above : null);
    }

    [Fact]
    public async Task The_start_up_check_refuses_rules_that_reach_from_above_over_a_model_that_does_not_say_where_a_resource_sits()
    {
        // A pallet is mapped without a place. Rules that would have it reached from above cannot be the rules
        // its functions were written from, and the check says so before it asks the database anything.
        var rules = new MembershipRules(
            "pens",
            keys: ["pens.view"],
            roles: [new("hand", ["pens.view"])],
            members: MemberSource.Resolved(DepotFunctions.CallerPorter),
            seeKey: "pens.view",
            above: new(DepotFunctions.BaysWhereIHold));
        var services = new ServiceCollection();
        services.AddDbContext<DepotContext>(options => options.UseNpgsql("Host=model-only"));
        services.AddSingleton<DepotDesk>();
        services.AddScoped<IPlacesReached<PalletId, BayId>, PalletsAtBays>();
        services.AddMembership<PalletPorter, PalletPorterId, PorterId, NamedRole, DepotContext, Pallet, PalletId, PalletsInTheDepot>(rules);
        await using var provider = services.BuildServiceProvider();

        (await FluentActions.Awaiting(() => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(provider, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("The rules 'pens' let Pallet be reached from above, and DepotContext does not say where it sits*HasMembers(..., at: resource => resource.PlaceId).");
    }

    /// <summary>Where a caller holds a key, for pallets that would sit at bays.</summary>
    private sealed class PalletsAtBays : IPlacesReached<PalletId, BayId>
    {
        public IQueryable<BayId> PlacesReached(DbContext context, Caller caller, string key) => context.Set<BayPath>().Where(path => false).Select(path => path.BayId);
    }

    /// <summary>Another function that says who the calling porter is, defined by somebody else than the depot: nobody, to everybody.</summary>
    private sealed class SpareFunction : IRowAccessContribution
    {
        public string Owner => "spare";

        public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
            => context is DepotContext
                ? new RowAccessContributionResult([new ContributedFunction("acting_porter", "", "uuid", "SELECT NULL::pg_catalog.uuid", SecurityDefiner: true)], [], [])
                : null;
    }
}
