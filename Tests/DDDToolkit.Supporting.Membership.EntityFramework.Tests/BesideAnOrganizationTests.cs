using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// A resource beside an organization: crates in a depot. Each of the three things a resource's rules say is
/// said the other way than for a document: who a member is, is what the depot resolves for the caller; the
/// roles are the depot's own, kept in its rows; and a key held at the crate's bay, or above it, reaches the
/// crate. The package knows the depot only through what the host answers.
/// </summary>
public sealed class BesideAnOrganizationTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_porter_on_a_crate_holds_what_the_depots_role_gives_it_now()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;
        var tea = await depot.Services.AsAsync(Caller.System, provider => provider.GetRequiredService<DepotContext>().Crates.AsNoTracking().SingleAsync(crate => crate.Id == data.Tea, Cancellation));

        // Bo packs on the tea for three days: the packer's role gives labels to print, a key of another module's.
        (await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Tea, CrateKeys.PrintLabels, Cancellation)))
            .Should().Be(new MemberHold<CrateId>(data.Tea, MemberVia.Members, tea.Version, data.InThreeDays), "a role held for three days gives its keys for three days");

        // No role of his gives weighing: he sees the crate without that key.
        (await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Tea, CrateKeys.Weigh, Cancellation)))
            .Should().Be(new MemberHold<CrateId>(data.Tea, null, tea.Version, Until: null));

        // Being on a crate gives the key that sees it, with no role at all, until the membership ends.
        (await depot.Services.CratesAsync(data.Dot, access => access.HoldAsync(data.Tea, CrateKeys.See, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        (await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, CrateKeys.See, Cancellation)))
            .Should().Be(new MemberHold<CrateId>(data.Tea, MemberVia.Members, tea.Version, data.NextWeek));

        // Dot's packing ended yesterday: a role counts only while it applies.
        (await depot.Services.CratesAsync(data.Dot, access => access.HoldAsync(data.Tea, CrateKeys.Pack, Cancellation)))!.Via.Should().BeNull();
    }

    [Fact]
    public async Task The_owner_of_a_crate_holds_its_keys_by_owning_it_with_no_end()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        // Scrapping is a key of the crate, which no role of the depot's gives: the owner's, by the owner column.
        var scrap = await depot.Services.CratesAsync(data.Ana, access => access.HoldAsync(data.Tea, CrateKeys.Scrap, Cancellation));
        (scrap!.Via, scrap.Until).Should().Be((MemberVia.Members, null));

        // What else an owner holds is what the depot's role for owners gives, like anybody in that role.
        (await depot.Services.CratesAsync(data.Ana, access => access.HoldAsync(data.Tea, CrateKeys.Weigh, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        (await depot.Services.CratesAsync(data.Ana, access => access.HoldAsync(data.Tea, CrateKeys.Move, Cancellation)))!.Via.Should().BeNull("moving is held at a bay, and Ana holds it at none");

        // And only on the crate it owns: Bo owns the salt, and is a packer on the tea.
        (await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Salt, CrateKeys.Scrap, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        (await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Tea, CrateKeys.Scrap, Cancellation)))!.Via.Should().BeNull();
    }

    [Fact]
    public async Task An_owner_whose_own_place_has_not_begun_sees_the_crate_and_holds_by_owning_only_the_crates_own_keys()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;
        var barley = CrateId.CreateSequential();

        // The barley names Dot as its owner today, and her place on it begins tomorrow.
        await depot.Services.ChangeAsync((context, _) =>
        {
            context.Crates.Add(new Crate(barley, "Barley", data.RightBay, data.Dot.Porter!.Value, data.Lead, data.Now.AddDays(1)));
            return Task.CompletedTask;
        });

        // She sees it by owning it, and holds by owning it what the rules give an owner, scrapping, with no end.
        var seen = await depot.Services.AsAsync(data.Dot.Caller, async provider =>
        {
            var see = provider.GetRequiredService<IMemberQuestions<CrateId>>().KeyReach([]).See;
            return await provider.GetRequiredService<DepotContext>().Crates.Within(see).Select(crate => crate.Id).ToListAsync(Cancellation);
        });
        seen.Should().Contain(barley);
        var scrap = await depot.Services.CratesAsync(data.Dot, access => access.HoldAsync(barley, CrateKeys.Scrap, Cancellation));
        (scrap!.Via, scrap.Until).Should().Be((MemberVia.Members, null));

        // The key that being on a crate gives is none of the crate's own keys, so owning it does not give it, and
        // neither does the depot's role for owners before her place begins: the keys asked about many crates say
        // what a hold on each says.
        (await depot.Services.CratesAsync(data.Dot, access => access.HoldAsync(barley, CrateKeys.See, Cancellation)))!.Via.Should().BeNull();
        (await depot.Services.CratesAsync(data.Dot, access => access.KeysOnAsync([barley], [CrateKeys.See, CrateKeys.Scrap, CrateKeys.Pack], Cancellation)))[barley]
            .Should().BeEquivalentTo([CrateKeys.Scrap]);
    }

    [Fact]
    public async Task A_key_held_at_the_crates_bay_or_above_it_is_held_on_the_crate()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        // Eli is on no crate. He sees and moves crates in the left aisle: the tea stands in a bay under it,
        // the rice in the aisle itself, and the salt in the right aisle.
        var tea = await depot.Services.CratesAsync(data.Eli, access => access.HoldAsync(data.Tea, CrateKeys.Move, Cancellation));
        (tea!.Via, tea.Until).Should().Be((MemberVia.Above, null), "what is held above has no end the package knows");
        (await depot.Services.CratesAsync(data.Eli, access => access.HoldAsync(data.Rice, CrateKeys.Move, Cancellation)))!.Via.Should().Be(MemberVia.Above);
        (await depot.Services.CratesAsync(data.Eli, access => access.HoldAsync(data.Salt, CrateKeys.Move, Cancellation))).Should().BeNull("a crate in another aisle is not there for him");

        // Seen from above, and no more held than is held there: Eli packs nowhere.
        (await depot.Services.CratesAsync(data.Eli, access => access.HoldAsync(data.Tea, CrateKeys.Pack, Cancellation)))!.Via.Should().BeNull();
        await Refused.WithCodeAsync(CrateMembership.Codes, MembershipRefusals.NotPermitted, () => depot.Services.CratesAsync(data.Eli, access => access.RequireAsync(data.Tea, CrateKeys.Pack, Cancellation)));
        await Refused.WithCodeAsync(CrateMembership.Codes, MembershipRefusals.NotFound, () => depot.Services.CratesAsync(data.Eli, access => access.RequireAsync(data.Salt, CrateKeys.Move, Cancellation)));

        // Flo sees and packs in the whole hall: every crate, wherever it stands.
        foreach (var crate in new[] { data.Tea, data.Salt, data.Rice })
        {
            (await depot.Services.CratesAsync(data.Flo, access => access.RequireAsync(crate, CrateKeys.Pack, Cancellation))).Via.Should().Be(MemberVia.Above);
            (await depot.Services.CratesAsync(data.Flo, access => access.ViaAsync(crate, CrateKeys.See, Cancellation))).Should().Be(MemberVia.Above);
        }

        // A crate that is moved is reached from where it stands now.
        await depot.Services.ChangeAsync(async (context, _) => (await context.Crates.SingleAsync(crate => crate.Id == data.Salt, Cancellation)).PutIn(data.LeftBay));
        (await depot.Services.CratesAsync(data.Eli, access => access.HoldAsync(data.Salt, CrateKeys.Move, Cancellation)))!.Via.Should().Be(MemberVia.Above);
    }

    [Fact]
    public async Task A_reach_says_where_one_key_is_held_and_the_reach_that_sees_goes_in_front_of_it()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        // Bo packs in the left aisle, where the rice stands, and holds the key that sees at no bay: the rice is
        // within his reach for packing, as the function for that key answers it, and it is not there for him.
        (await CratesWithinAsync(depot.Services, data.Bo, CrateKeys.Pack)).Should().BeEquivalentTo([data.Tea, data.Salt, data.Rice]);
        (await CratesWithinAsync(depot.Services, data.Bo, CrateKeys.See)).Should().BeEquivalentTo([data.Tea, data.Salt]);
        (await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Rice, CrateKeys.Pack, Cancellation))).Should().BeNull();
        await Refused.WithCodeAsync(CrateMembership.Codes, MembershipRefusals.NotFound, () => depot.Services.CratesAsync(data.Bo, access => access.RequireAsync(data.Rice, CrateKeys.Pack, Cancellation)));

        // A statement of the host's own that lists what its caller may pack puts the reach that sees in front:
        // two reaches, and still one statement.
        depot.Commands.Reset();
        var packed = await depot.Services.AsAsync(data.Bo.Caller, async provider =>
        {
            var access = provider.GetRequiredService<IMemberQuestions<CrateId>>();
            return await provider.GetRequiredService<DepotContext>().Crates
                .Within(access.Reach(CrateKeys.See))
                .Within(access.Reach(CrateKeys.Pack))
                .Select(crate => crate.Id)
                .ToListAsync(Cancellation);
        });
        packed.Should().BeEquivalentTo([data.Tea, data.Salt]);
        depot.Commands.Count.Should().Be(1);
    }

    [Fact]
    public async Task Only_a_signed_in_caller_holds_a_key_from_above_and_nothing_is_read_for_any_other()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        // A caller that did not sign in is nobody's member and holds nothing anywhere: the depot is not asked
        // where it holds a key, and no statement is sent to find out.
        await depot.Services.AsAsync(Caller.Anonymous, async provider =>
        {
            var access = provider.GetRequiredService<IMemberQuestions<CrateId>>();
            (await access.HoldAsync(data.Tea, CrateKeys.See, Cancellation)).Should().BeNull();
            (await access.KeysOnAsync([data.Tea, data.Salt, data.Rice], DepotScenario.CrateKeysAsked, Cancellation)).Should().BeEmpty();
            depot.Commands.Count.Should().Be(0);

            // In a statement of the host's own its reach is the empty one, whichever context it runs on.
            (await provider.GetRequiredService<DepotContext>().Crates.Within(access.Reach(CrateKeys.See)).CountAsync(Cancellation)).Should().Be(0);
            depot.Commands.Commands.Should().ContainSingle().Which.Should().NotContain("\"BayHolds\"").And.NotContain("\"CratePorters\"");
        });
    }

    [Fact]
    public async Task A_member_that_holds_the_key_above_as_well_holds_it_as_a_member_and_with_no_end()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        // Bo packs on the tea for three days, and packs in the left aisle besides: the members come first,
        // and the key stays his when his role on the crate runs out.
        var pack = await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Tea, CrateKeys.Pack, Cancellation));
        (pack!.Via, pack.Until).Should().Be((MemberVia.Members, null));

        // In a statement of the host's own the two are apart: how it is held as a member, until when, and from above.
        var found = await depot.Services.AsAsync(data.Bo.Caller, async provider =>
        {
            var access = provider.GetRequiredService<IMemberQuestions<CrateId>>();
            return await provider.GetRequiredService<DepotContext>().Crates
                .Where(crate => crate.Id == data.Tea)
                .Reached(access.Reach(CrateKeys.Pack))
                .Select(reached => new { reached.AsMember, reached.FromAbove, reached.Until })
                .SingleAsync(Cancellation);
        });
        (found.AsMember, found.FromAbove, found.Until).Should().Be((true, true, data.InThreeDays));

        // Where the hold above is gone, the member's own end is the hold's.
        await depot.Services.ChangeAsync(async (context, _) => context.BayHolds.Remove(
            await context.BayHolds.SingleAsync(hold => hold.PorterId == data.Bo.Porter && hold.Key == CrateKeys.Pack, Cancellation)));
        var alone = await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Tea, CrateKeys.Pack, Cancellation));
        (alone!.Via, alone.Until).Should().Be((MemberVia.Members, data.InThreeDays));
    }

    [Fact]
    public async Task A_key_held_from_above_is_held_for_as_long_as_the_caller_sees_the_crate()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;
        var cas = data.Cas.Porter!.Value;

        // Cas is on the tea until next week, in a role the depot no longer uses, and weighs in the bay the tea
        // stands in. He holds the key that sees at no bay: the tea is there for him because he is on it, so
        // what he holds on it from above he holds until he is off it.
        await depot.Services.ChangeAsync((context, _) =>
        {
            context.BayHolds.Add(new BayHold { PorterId = cas, BayId = data.LeftBay, Key = CrateKeys.Weigh });
            return Task.CompletedTask;
        });
        depot.Commands.Reset();
        var weigh = await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, CrateKeys.Weigh, Cancellation));
        (weigh!.Via, weigh.Until).Should().Be((MemberVia.Above, data.NextWeek));
        depot.Commands.Count.Should().Be(1, "until when the crate is seen is read in the statement that reads the hold");

        // The same for a key he holds both ways: a packer for three days, and packing in that bay besides. The
        // key outlasts his role, and not his sight of the crate.
        await depot.Services.ChangeAsync(async (context, _) =>
        {
            (await context.Crates.SingleAsync(crate => crate.Id == data.Tea, Cancellation))
                .GiveRole(cas, data.Packer, MemberPeriod.Between(data.Now.AddDays(-1), data.InThreeDays), data.Now, by: null);
            context.BayHolds.Add(new BayHold { PorterId = cas, BayId = data.LeftBay, Key = CrateKeys.Pack });
        });
        var pack = await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, CrateKeys.Pack, Cancellation));
        (pack!.Via, pack.Until).Should().Be((MemberVia.Members, data.NextWeek));

        // What his role alone gives ends with the role, as ever.
        var print = await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, CrateKeys.PrintLabels, Cancellation));
        (print!.Via, print.Until).Should().Be((MemberVia.Members, data.InThreeDays));

        // Once he holds the key that sees there as well, the crate stays in sight when he is off it, and
        // nothing known here ends what he holds on it from above.
        await depot.Services.ChangeAsync((context, _) =>
        {
            context.BayHolds.Add(new BayHold { PorterId = cas, BayId = data.Hall, Key = CrateKeys.See });
            return Task.CompletedTask;
        });
        foreach (var key in new[] { CrateKeys.Weigh, CrateKeys.Pack, CrateKeys.See })
        {
            (await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, key, Cancellation)))!.Until.Should().BeNull("of " + key);
        }

        (await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, CrateKeys.PrintLabels, Cancellation)))!.Until
            .Should().Be(data.InThreeDays, "a key no bay gives him is his role's, however he sees the crate");

        // A statement of the host's own reads the two apart, as it did: until when as a member, and whether from above.
        var found = await depot.Services.AsAsync(data.Cas.Caller, async provider =>
        {
            var access = provider.GetRequiredService<IMemberQuestions<CrateId>>();
            return await provider.GetRequiredService<DepotContext>().Crates
                .Where(crate => crate.Id == data.Tea)
                .Reached(access.Reach(CrateKeys.Pack))
                .Select(reached => new { reached.AsMember, reached.FromAbove, reached.Until })
                .SingleAsync(Cancellation);
        });
        (found.AsMember, found.FromAbove, found.Until).Should().Be((true, true, data.InThreeDays));
    }

    [Fact]
    public async Task A_key_held_from_above_on_a_crate_seen_as_a_member_alone_is_gone_at_the_moment_its_hold_names_and_not_a_tick_before()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;
        var cas = data.Cas.Porter!.Value;

        // Cas is on the tea until next week and weighs in the bay it stands in, holding the key that sees at no bay.
        await depot.Services.ChangeAsync((context, _) =>
        {
            context.BayHolds.Add(new BayHold { PorterId = cas, BayId = data.LeftBay, Key = CrateKeys.Weigh });
            return Task.CompletedTask;
        });

        // One tick before his membership ends, the key is his still, until the moment the hold names.
        depot.Clock.Advance(data.NextWeek - depot.Clock.Now - TimeSpan.FromTicks(1));
        var before = await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, CrateKeys.Weigh, Cancellation));
        (before!.Via, before.Until).Should().Be((MemberVia.Above, data.NextWeek));

        // At that moment the tea is not there for him, whatever he holds at its bay: what the hold said.
        depot.Clock.Advance(TimeSpan.FromTicks(1));
        (await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, CrateKeys.Weigh, Cancellation))).Should().BeNull();
        await Refused.WithCodeAsync(CrateMembership.Codes, MembershipRefusals.NotFound, () => depot.Services.CratesAsync(data.Cas, access => access.RequireAsync(data.Tea, CrateKeys.Weigh, Cancellation)));
        (await depot.Services.CratesAsync(data.Cas, access => access.KeysOnAsync([data.Tea], [CrateKeys.See, CrateKeys.Weigh], Cancellation))).Should().BeEmpty();

        // The reach for the key still says where it is held, seen or not; behind the reach that sees, the tea is gone.
        (await CratesWithinAsync(depot.Services, data.Cas, CrateKeys.Weigh)).Should().BeEquivalentTo([data.Tea, data.Rice], "he weighs in the tea's bay, and owns the rice in the depot's role for owners");
        var inSight = await depot.Services.AsAsync(data.Cas.Caller, async provider =>
        {
            var access = provider.GetRequiredService<IMemberQuestions<CrateId>>();
            return await provider.GetRequiredService<DepotContext>().Crates
                .Within(access.Reach(CrateKeys.See))
                .Within(access.Reach(CrateKeys.Weigh))
                .Select(crate => crate.Id)
                .ToListAsync(Cancellation);
        });
        inSight.Should().Equal(data.Rice);

        // With the key that sees held at the bay as well, the tea is there again, and the key his with no end known here.
        await depot.Services.ChangeAsync((context, _) =>
        {
            context.BayHolds.Add(new BayHold { PorterId = cas, BayId = data.LeftBay, Key = CrateKeys.See });
            return Task.CompletedTask;
        });
        var seen = await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, CrateKeys.Weigh, Cancellation));
        (seen!.Via, seen.Until).Should().Be((MemberVia.Above, null));
    }

    [Fact]
    public async Task What_a_role_holds_where_it_is_kept_is_cut_by_what_a_members_role_gives()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        // The depot's packer holds four keys. The crates' rules keep two of them from a member's role: moving a
        // crate, and the depot's own key.
        data.RolesGiving(CrateKeys.ManageDepot).Should().Equal(data.Packer);
        data.RolesGiving(CrateKeys.Move).Should().Equal(data.Packer);

        (await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Tea, CrateKeys.ManageDepot, Cancellation)))!.Via.Should().BeNull("no role on a crate gives a key of the depot's own");
        (await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Tea, CrateKeys.PrintLabels, Cancellation)))!.Via.Should().Be(MemberVia.Members, "a key nobody kept from members is given");

        // Moving: his role holds it, his membership does not give it, and what he holds at the bay does.
        (await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Tea, CrateKeys.Move, Cancellation)))!.Via.Should().Be(MemberVia.Above);
        await depot.Services.ChangeAsync(async (context, _) => context.BayHolds.Remove(
            await context.BayHolds.SingleAsync(hold => hold.PorterId == data.Bo.Porter && hold.Key == CrateKeys.Move, Cancellation)));
        (await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Tea, CrateKeys.Move, Cancellation)))!.Via.Should().BeNull("whatever the packer holds in the depot");

        // And nothing is asked of the depot about a key no member's role gives: the statement has no roles in it.
        depot.Commands.Reset();
        await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Tea, CrateKeys.ManageDepot, Cancellation));
        depot.Commands.Commands.Should().ContainSingle().Which.Should().NotContain("\"RoleKeys\"");
    }

    [Fact]
    public async Task A_role_the_depot_no_longer_uses_gives_nothing_until_it_is_in_use_again()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        // Cas is on the tea as an old hand, a role the depot keeps and no longer uses.
        (await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, CrateKeys.Pack, Cancellation)))!.Via.Should().BeNull();

        // Which roles give a key is asked where they are kept, each time: nothing of it is copied into the rules.
        await depot.Services.ChangeAsync(async (context, _) => (await context.Roles.SingleAsync(role => role.Id == data.OldHand, Cancellation)).InUse = true);
        var pack = await depot.Services.CratesAsync(data.Cas, access => access.HoldAsync(data.Tea, CrateKeys.Pack, Cancellation));
        (pack!.Via, pack.Until).Should().Be((MemberVia.Members, data.NextWeek));
    }

    [Fact]
    public async Task A_porter_the_depot_let_go_is_nobodys_member_and_holds_nothing_above()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        // Ned is on the tea as a packer, and sees crates in the whole hall, by rows that all still apply. The
        // depot let him go, so he is nobody: nothing is there for him.
        foreach (var key in DepotScenario.CrateKeysAsked)
        {
            (await depot.Services.CratesAsync(data.Ned, access => access.HoldAsync(data.Tea, key, Cancellation))).Should().BeNull();
        }

        await Refused.WithCodeAsync(CrateMembership.Codes, MembershipRefusals.NotFound, () => depot.Services.CratesAsync(data.Ned, access => access.RequireAsync(data.Tea, CrateKeys.See, Cancellation)));
        (await depot.Services.CratesAsync(data.Ned, access => access.KeysOnAsync([data.Tea, data.Salt, data.Rice], DepotScenario.CrateKeysAsked, Cancellation))).Should().BeEmpty();

        // Taken on again, he holds what his rows say.
        await depot.Services.ChangeAsync(async (context, desk) =>
        {
            var porter = await context.Porters.SingleAsync(candidate => candidate.Id == data.Ned.Porter, Cancellation);
            porter.Active = true;
            desk.TakeOn(porter);
        });
        (await depot.Services.CratesAsync(data.Ned, access => access.HoldAsync(data.Tea, CrateKeys.Pack, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        (await depot.Services.CratesAsync(data.Ned, access => access.HoldAsync(data.Salt, CrateKeys.See, Cancellation)))!.Via.Should().Be(MemberVia.Above);
    }

    [Fact]
    public async Task Somebody_the_depot_never_took_on_is_refused_with_the_depots_own_refusal_before_anything_is_read()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        // The questions that refuse ask the host first: Ivo is nobody its crates can be asked about.
        (await FluentActions.Awaiting(() => depot.Services.CratesAsync(data.Ivo, access => access.RequireAsync(data.Tea, CrateKeys.See, Cancellation)))
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(CratesInTheDepot.NotOfTheDepot);
        (await FluentActions.Awaiting(() => depot.Services.CratesAsync(data.Ivo, access => access.ViaAsync(data.Tea, CrateKeys.See, Cancellation)))
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(CratesInTheDepot.NotOfTheDepot);
        await depot.Services.CratesAsync(data.Ivo, access =>
        {
            FluentActions.Invoking(access.RequireCaller).Should().Throw<RefusalException>().Which.Code.Should().Be(CratesInTheDepot.NotOfTheDepot);
            return Task.FromResult(true);
        });
        depot.Commands.Count.Should().Be(0, "nobody is refused as nobody, without a statement made for it");

        // The questions that only answer refuse nobody: to Ivo nothing is there, and nothing is held.
        (await depot.Services.CratesAsync(data.Ivo, access => access.HoldAsync(data.Tea, CrateKeys.See, Cancellation))).Should().BeNull();
        (await CratesWithinAsync(depot.Services, data.Ivo, CrateKeys.See)).Should().BeEmpty();

        // A porter the depot let go is known to it, so it is not refused as a stranger: nothing is there for it.
        await depot.Services.CratesAsync(data.Ned, access =>
        {
            access.RequireCaller();
            return Task.FromResult(true);
        });

        // The application's own work is never asked about: it holds every key on every crate.
        (await depot.Services.AsAsync(Caller.System, provider => provider.GetRequiredService<IMemberQuestions<CrateId>>().RequireAsync(data.Tea, CrateKeys.Scrap, Cancellation)))
            .Should().BeEquivalentTo(new { Via = MemberVia.System, Until = (DateTimeOffset?)null });

        // A pallet's rules have the depot resolve who a caller is too, and the depot refuses nobody there:
        // somebody it never took on simply reaches no pallet.
        await Refused.WithCodeAsync(PalletMembership.Codes, MembershipRefusals.NotFound, () => depot.Services.PalletsAsync(data.Ivo, access => access.RequireAsync(data.Pears, PalletKeys.See, Cancellation)));
    }

    [Fact]
    public async Task What_the_storage_answers_about_crates_is_what_the_rules_say_of_the_depots_rows()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;
        CrateId[] crates = [data.Tea, data.Salt, data.Rice];

        foreach (var person in data.People)
        {
            var seen = data.CratesSeenBy(person);
            var seenInTheDatabase = await depot.Services.AsAsync(person.Caller, async provider =>
            {
                var see = provider.GetRequiredService<IMemberQuestions<CrateId>>().KeyReach([]).See;
                return await provider.GetRequiredService<DepotContext>().Crates.Within(see).Select(crate => crate.Id).ToListAsync(Cancellation);
            });
            seenInTheDatabase.Should().BeEquivalentTo(seen, "of what " + person + " sees");

            var keys = await depot.Services.CratesAsync(person, access => access.KeysOnAsync(crates, DepotScenario.CrateKeysAsked, Cancellation));
            foreach (var key in DepotScenario.CrateKeysAsked)
            {
                var held = data.CratesHeldBy(person, key);
                (await CratesWithinAsync(depot.Services, person, key)).Should().BeEquivalentTo(held, "of what " + person + " holds " + key + " on");

                // The keys held on many crates: only on those the caller sees.
                crates.Where(crate => keys.TryGetValue(crate, out var on) && on.Contains(key)).Should().BeEquivalentTo(held.Intersect(seen), "of " + key + " among the keys " + person + " holds");

                foreach (var crate in crates)
                {
                    var hold = await depot.Services.CratesAsync(person, access => access.HoldAsync(crate, key, Cancellation));
                    var expected = !seen.Contains(crate) ? "not there"
                        : !held.Contains(crate) ? "seen"
                        : IsHeldAsMember(data, person, crate, key) ? "Members"
                        : "Above";
                    (hold is null ? "not there" : hold.Via?.ToString() ?? "seen").Should().Be(expected, "of " + person + " and " + key + " on " + data.Crates.Single(each => each.Id == crate).Label);
                }
            }
        }
    }

    [Fact]
    public async Task What_is_held_on_a_crate_is_one_statement_however_it_is_held()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        // As a member through a role of the depot's, as the owner, from above, both ways, and not at all.
        (DepotPerson Person, CrateId Crate, string Key)[] asked =
        [
            (data.Bo, data.Tea, CrateKeys.PrintLabels), (data.Ana, data.Tea, CrateKeys.Scrap), (data.Eli, data.Rice, CrateKeys.Move),
            (data.Bo, data.Tea, CrateKeys.Pack), (data.Dot, data.Tea, CrateKeys.Pack), (data.Eli, data.Salt, CrateKeys.See), (data.Flo, data.Tea, "crates.unheard-of"),
        ];

        foreach (var (person, crate, key) in asked)
        {
            depot.Commands.Reset();
            await depot.Services.CratesAsync(person, access => access.HoldAsync(crate, key, Cancellation));
            depot.Commands.Count.Should().Be(1, "seeing the crate, holding " + key + " on it as " + person + ", until when, and its version are one statement");
        }

        // The questions that refuse are that statement and nothing more.
        depot.Commands.Reset();
        await depot.Services.CratesAsync(data.Bo, access => access.RequireAsync(data.Tea, CrateKeys.Pack, Cancellation));
        await depot.Services.CratesAsync(data.Eli, access => access.ViaAsync(data.Tea, CrateKeys.Pack, Cancellation));
        depot.Commands.Count.Should().Be(2);

        // What the depot answers is read where its rows are, inside the statement: its roles and its bays are
        // subqueries of it, and no list of either was fetched first.
        depot.Commands.Reset();
        await depot.Services.CratesAsync(data.Bo, access => access.HoldAsync(data.Tea, CrateKeys.Pack, Cancellation));
        var statement = depot.Commands.Commands.Should().ContainSingle().Subject;
        statement.Should().Contain("\"CratePorters\"").And.Contain("\"RoleKeys\"").And.Contain("\"BayPaths\"").And.Contain("\"BayHolds\"");

        // The keys held on many crates are one statement whatever their number, and so is a page of the host's own.
        depot.Commands.Reset();
        var keys = await depot.Services.CratesAsync(data.Bo, access => access.KeysOnAsync([data.Tea, data.Salt, data.Rice, CrateId.CreateSequential()], DepotScenario.CrateKeysAsked, Cancellation));
        depot.Commands.Count.Should().Be(1);
        keys[data.Tea].Should().BeEquivalentTo([CrateKeys.See, CrateKeys.Pack, CrateKeys.Move, CrateKeys.PrintLabels]);
        keys[data.Salt].Should().BeEquivalentTo([CrateKeys.See, CrateKeys.Pack, CrateKeys.Weigh, CrateKeys.Scrap], "he owns the salt, in the depot's role for owners");
        keys.Should().NotContainKey(data.Rice, "he packs in the aisle it stands in, and does not see it");

        depot.Commands.Reset();
        (await CratesWithinAsync(depot.Services, data.Flo, CrateKeys.Pack)).Should().HaveCount(3);
        depot.Commands.Count.Should().Be(1);

        // Somebody who is nobody's member may still hold a key above, which only the statement can tell: one
        // statement, as for anybody. Where nothing above reaches a resource, nothing is read for nobody.
        depot.Commands.Reset();
        (await depot.Services.CratesAsync(data.Ivo, access => access.HoldAsync(data.Tea, CrateKeys.See, Cancellation))).Should().BeNull();
        depot.Commands.Count.Should().Be(1);
        (await depot.Services.PalletsAsync(data.Ivo, access => access.HoldAsync(data.Pears, PalletKeys.See, Cancellation))).Should().BeNull();
        (await depot.Services.PalletsAsync(data.Ivo, access => access.KeysOnAsync([data.Pears], [PalletKeys.See], Cancellation))).Should().BeEmpty();
        depot.Commands.Count.Should().Be(1, "a pallet is reached through its members alone, and Ivo is nobody's");
    }

    [Fact]
    public async Task A_reach_goes_into_a_statement_on_the_requests_context_and_on_another_that_is_handed_over()
    {
        using var depot = await SqliteDepot.SeededAsync(ownContexts: true);
        var data = depot.Scenario;

        await depot.Services.AsAsync(data.Eli.Caller, async provider =>
        {
            var access = provider.GetRequiredService<IMemberQuestions<CrateId>>();
            var seen = access.Reach(CrateKeys.See);
            var keys = access.KeyReach([CrateKeys.Move, CrateKeys.Pack]);

            // On the request's own context nothing more is said.
            var requests = provider.GetRequiredService<DepotContext>();
            (await requests.Crates.Within(seen).OrderBy(crate => crate.Label).Select(crate => crate.Label).ToListAsync(Cancellation)).Should().Equal("Rice", "Tea");

            // A reading on a context of its own hands that context over: what the depot answers is a query
            // over that context's rows, and goes into the statement that runs there.
            await using var reading = await provider.GetRequiredService<IDbContextFactory<DepotContext>>().CreateDbContextAsync(Cancellation);
            depot.Commands.Reset();
            (await reading.Crates.AsNoTracking().Within(seen, reading).CountAsync(Cancellation)).Should().Be(2);
            (await reading.Crates.Where(crate => crate.Id == data.Tea).Reached(seen, reading).Select(found => found.FromAbove).SingleAsync(Cancellation)).Should().BeTrue();
            (await reading.Crates.KeysOn(keys, reading).ToListAsync(Cancellation)).Select(row => (row.Resource, row.Key))
                .Should().BeEquivalentTo([(data.Tea, CrateKeys.Move), (data.Rice, CrateKeys.Move)]);
            depot.Commands.Sent.Should().HaveCount(3).And.OnlyContain(sent => sent.Context == reading);

            // Left unsaid, it is refused where the statement is put together, with what to write.
            FluentActions.Invoking(() => reading.Crates.Within(seen)).Should().Throw<InvalidOperationException>()
                .WithMessage("This reach of Crate puts what the application answers into the query*hand over the context it was made from*Within(reach, db)*");
            FluentActions.Invoking(() => reading.Crates.Reached(seen)).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => reading.Crates.KeysOn(keys)).Should().Throw<InvalidOperationException>();

            // And so is a context that is not the one the query was made from.
            FluentActions.Invoking(() => reading.Crates.Within(seen, requests)).Should().Throw<InvalidOperationException>()
                .WithMessage("The context handed over with this reach is not the one the query over Crate runs on*");
        });

        // A reach that asks the host for nothing goes into a query on any context, as it always did: a pallet's
        // roles are declared in its rules, and nothing above reaches it.
        await depot.Services.AsAsync(data.Bo.Caller, async provider =>
        {
            var loads = provider.GetRequiredService<IMemberQuestions<PalletId>>().Reach(PalletKeys.Load);
            await using var reading = await provider.GetRequiredService<IDbContextFactory<DepotContext>>().CreateDbContextAsync(Cancellation);
            (await reading.Pallets.Within(loads).Select(pallet => pallet.Id).ToListAsync(Cancellation)).Should().BeEquivalentTo([data.Pears, data.Plums]);
        });

        // The package's own questions read on a context of their own, and ask the host with that one.
        depot.Commands.Reset();
        await depot.Services.AsAsync(data.Flo.Caller, async provider =>
        {
            var access = provider.GetRequiredService<IMemberQuestions<CrateId>>();
            var asked = await Task.WhenAll(
                access.HoldAsync(data.Tea, CrateKeys.Pack, Cancellation),
                access.HoldAsync(data.Salt, CrateKeys.Pack, Cancellation),
                access.HoldAsync(data.Rice, CrateKeys.Weigh, Cancellation));

            asked.Select(held => held?.Via).Should().Equal(MemberVia.Above, MemberVia.Above, null);
            depot.Commands.Sent.Should().HaveCount(3);
            depot.Commands.Sent.Select(sent => sent.Context).Should().NotContain(provider.GetRequiredService<DepotContext>());
        });
    }

    [Fact]
    public async Task The_admission_of_a_crate_asks_the_depot_who_counts_and_which_roles_there_are()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        await depot.Services.AsAsync(Caller.System, async provider =>
        {
            var admission = provider.GetRequiredService<MemberAdmission<CrateId, PorterId, DepotRoleId>>();

            // A porter the depot let go is not put on a crate, and neither is somebody it does not know.
            (await Refused.WithCodeAsync(CrateMembership.Codes, MembershipRefusals.MemberNotActive, () => admission.RequireMemberAsync(data.Ned.Porter!.Value, Cancellation).AsTask()))
                .Arguments.Should().Contain("Member", data.Ned.Porter!.Value);
            await Refused.WithCodeAsync(CrateMembership.Codes, MembershipRefusals.MemberNotActive, () => admission.RequireMemberAsync(PorterId.CreateSequential(), Cancellation).AsTask());
            await admission.RequireMemberAsync(data.Eli.Porter!.Value, Cancellation);

            // The roles there are, are the depot's rows: one in use goes to a member, one that is not does not.
            await admission.RequireRoleAsync(data.Packer, Cancellation);
            await Refused.WithCodeAsync(CrateMembership.Codes, MembershipRefusals.RoleNotForMembers, () => admission.RequireRoleAsync(data.OldHand, Cancellation).AsTask());
            await Refused.WithCodeAsync(CrateMembership.Codes, MembershipRefusals.RoleNotForMembers, () => admission.RequireRoleAsync(DepotRoleId.CreateSequential(), Cancellation).AsTask());

            // The owner's role is the depot's role found by what the rules name.
            (await admission.OwnerRoleAsync(Cancellation)).Should().Be(data.Lead);
            provider.GetRequiredService<IMemberRoles<CrateId, DepotRoleId>>().Should().BeOfType<CratesInTheDepot>()
                .And.BeSameAs(provider.GetRequiredService<IRolesWithKey<CrateId, DepotRoleId>>(), "one instance for a scope answers every port of the resource");
        });

        // With no role of that name in use there is no owner's role, which is refused under the crate's code.
        await depot.Services.ChangeAsync(async (context, _) => (await context.Roles.SingleAsync(role => role.Id == data.Lead, Cancellation)).InUse = false);
        await depot.Services.AsAsync(Caller.System, provider => Refused.WithCodeAsync(
            CrateMembership.Codes,
            MembershipRefusals.NoOwnerRole,
            () => provider.GetRequiredService<MemberAdmission<CrateId, PorterId, DepotRoleId>>().OwnerRoleAsync(Cancellation).AsTask()));
    }

    /// <summary>The crates a reach for <paramref name="key"/> reaches, read in a statement of the host's own on the request's context.</summary>
    private static Task<List<CrateId>> CratesWithinAsync(DepotServices services, DepotPerson person, string key)
        => services.AsAsync(person.Caller, async provider =>
        {
            var reach = provider.GetRequiredService<IMemberQuestions<CrateId>>().Reach(key);
            return await provider.GetRequiredService<DepotContext>().Crates.Within(reach).Select(crate => crate.Id).ToListAsync(Cancellation);
        });

    /// <summary>Whether somebody holds a key on a crate through its members: as its owner, by being on it, or through a role held on it.</summary>
    private static bool IsHeldAsMember(DepotScenario data, DepotPerson person, CrateId crate, string key)
    {
        var rules = CrateMembership.Rules;
        var found = data.Crates.Single(each => each.Id == crate);
        return (rules.OwnerHolds(key) && found.OwnerId == person.Member)
            || (rules.MembershipGives(key) && data.CratesAsMember(person).Contains(crate))
            || data.CratesWithARoleFor(person, key).Contains(crate);
    }
}
