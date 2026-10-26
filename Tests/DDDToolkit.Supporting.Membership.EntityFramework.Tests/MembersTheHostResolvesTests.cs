namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// A resource whose members the host resolves, with roles of its own: pallets in a depot. Who a caller is as a
/// member is what the depot says, a porter, and the pallet's roles are declared in its rules, by name, as a
/// document's are. Nothing above reaches a pallet. One of the three things rules say is said another way,
/// and the other two are as they always were.
/// </summary>
public sealed class MembersTheHostResolvesTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_porter_on_a_pallet_holds_what_its_role_gives_and_the_owner_every_key()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;
        var pears = await depot.Services.AsAsync(Caller.System, provider => provider.GetRequiredService<DepotContext>().Pallets.AsNoTracking().SingleAsync(pallet => pallet.Id == data.Pears, Cancellation));

        // As a member: a loader loads, and holds no key its role does not give.
        (await depot.Services.PalletsAsync(data.Bo, access => access.HoldAsync(data.Pears, PalletKeys.Load, Cancellation)))
            .Should().Be(new MemberHold<PalletId>(data.Pears, MemberVia.Members, pears.Version, Until: null));
        (await depot.Services.PalletsAsync(data.Bo, access => access.HoldAsync(data.Pears, PalletKeys.Strap, Cancellation)))
            .Should().Be(new MemberHold<PalletId>(data.Pears, null, pears.Version, Until: null));

        // Being on a pallet gives the key that sees it, until the membership ends; a checker checks and no more.
        (await depot.Services.PalletsAsync(data.Cas, access => access.HoldAsync(data.Pears, PalletKeys.See, Cancellation)))
            .Should().Be(new MemberHold<PalletId>(data.Pears, MemberVia.Members, pears.Version, data.NextWeek));
        (await depot.Services.PalletsAsync(data.Dot, access => access.ViaAsync(data.Pears, PalletKeys.See, Cancellation))).Should().Be(MemberVia.Members);
        (await depot.Services.PalletsAsync(data.Dot, access => access.ViaAsync(data.Pears, PalletKeys.Load, Cancellation))).Should().BeNull();

        // As the owner: every key of the pallet, with no end, on the pallet it owns and on no other. Strapping is
        // a key the loader's role lists and the rules keep from every member's role, so the owner's alone.
        PalletMembership.Rules.Roles.Single(role => role.Name == "loader").Keys.Should().Contain(PalletKeys.Strap);
        PalletMembership.Rules.KeysOf(PalletMembership.Loader).Should().Equal(PalletKeys.See, PalletKeys.Load);
        var strap = await depot.Services.PalletsAsync(data.Ana, access => access.RequireAsync(data.Pears, PalletKeys.Strap, Cancellation));
        (strap.Via, strap.Until).Should().Be((MemberVia.Members, null));
        await Refused.WithCodeAsync(PalletMembership.Codes, MembershipRefusals.NotPermitted, () => depot.Services.PalletsAsync(data.Bo, access => access.RequireAsync(data.Pears, PalletKeys.Strap, Cancellation)));
        await Refused.WithCodeAsync(PalletMembership.Codes, MembershipRefusals.NotPermitted, () => depot.Services.PalletsAsync(data.Ana, access => access.RequireAsync(data.Plums, PalletKeys.Strap, Cancellation)));

        // And none: a porter that is on no pallet, and a pallet a porter is not on.
        await Refused.WithCodeAsync(PalletMembership.Codes, MembershipRefusals.NotFound, () => depot.Services.PalletsAsync(data.Eli, access => access.RequireAsync(data.Pears, PalletKeys.See, Cancellation)));
        await Refused.WithCodeAsync(PalletMembership.Codes, MembershipRefusals.NotFound, () => depot.Services.PalletsAsync(data.Ana, access => access.RequireAsync(data.Limes, PalletKeys.See, Cancellation)));
    }

    [Fact]
    public async Task What_the_storage_answers_about_pallets_is_what_the_rules_say_of_the_porters_on_them()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;
        PalletId[] pallets = [data.Pears, data.Plums, data.Limes];

        foreach (var person in data.People)
        {
            var seen = data.PalletsSeenBy(person);
            var seenInTheDatabase = await depot.Services.AsAsync(person.Caller, async provider =>
            {
                var see = provider.GetRequiredService<IMemberQuestions<PalletId>>().KeyReach([]).See;
                return await provider.GetRequiredService<DepotContext>().Pallets.Within(see).Select(pallet => pallet.Id).ToListAsync(Cancellation);
            });
            seenInTheDatabase.Should().BeEquivalentTo(seen, "of what " + person + " sees");

            var keys = await depot.Services.PalletsAsync(person, access => access.KeysOnAsync(pallets, DepotScenario.PalletKeysAsked, Cancellation));
            foreach (var key in DepotScenario.PalletKeysAsked)
            {
                var held = data.PalletsHeldBy(person, key);
                var heldInTheDatabase = await depot.Services.AsAsync(person.Caller, async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<PalletId>>().Reach(key);
                    return await provider.GetRequiredService<DepotContext>().Pallets.Within(reach).Select(pallet => pallet.Id).ToListAsync(Cancellation);
                });

                heldInTheDatabase.Should().BeEquivalentTo(held, "of what " + person + " holds " + key + " on");
                pallets.Where(pallet => keys.TryGetValue(pallet, out var on) && on.Contains(key)).Should().BeEquivalentTo(held, "of " + key + " among the keys " + person + " holds");

                foreach (var pallet in pallets)
                {
                    var hold = await depot.Services.PalletsAsync(person, access => access.HoldAsync(pallet, key, Cancellation));
                    var expected = !seen.Contains(pallet) ? "not there" : held.Contains(pallet) ? "Members" : "seen";
                    (hold is null ? "not there" : hold.Via?.ToString() ?? "seen").Should().Be(expected, "of " + person + " and " + key);
                }
            }
        }
    }

    [Fact]
    public async Task A_porter_the_depot_let_go_is_nobodys_member_whatever_its_rows_say()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        // Ned is on the pears as a loader, by a membership and a role that both apply. The depot let him go, so
        // the host answers nobody for him: nothing is there, and nothing is read to find that out.
        (await depot.Services.PalletsAsync(data.Ned, access => access.HoldAsync(data.Pears, PalletKeys.Load, Cancellation))).Should().BeNull();
        (await depot.Services.PalletsAsync(data.Ned, access => access.KeysOnAsync([data.Pears], DepotScenario.PalletKeysAsked, Cancellation))).Should().BeEmpty();
        await Refused.WithCodeAsync(PalletMembership.Codes, MembershipRefusals.NotFound, () => depot.Services.PalletsAsync(data.Ned, access => access.RequireAsync(data.Pears, PalletKeys.See, Cancellation)));
        depot.Commands.Count.Should().Be(0);

        // Taken on again, his rows count again.
        await depot.Services.ChangeAsync(async (context, desk) =>
        {
            var porter = await context.Porters.SingleAsync(candidate => candidate.Id == data.Ned.Porter, Cancellation);
            porter.Active = true;
            desk.TakeOn(porter);
        });
        (await depot.Services.PalletsAsync(data.Ned, access => access.HoldAsync(data.Pears, PalletKeys.Load, Cancellation)))!.Via.Should().Be(MemberVia.Members);

        // And nobody the depot counts is put on a pallet in the first place: the admission asks the depot.
        await depot.Services.AsAsync(Caller.System, async provider =>
        {
            var admission = provider.GetRequiredService<MemberAdmission<PalletId, PorterId, NamedRole>>();
            await Refused.WithCodeAsync(PalletMembership.Codes, MembershipRefusals.MemberNotActive, () => admission.RequireMemberAsync(PorterId.CreateSequential(), Cancellation).AsTask());
            await admission.RequireMemberAsync(data.Eli.Porter!.Value, Cancellation);

            // The roles are the ones the rules declare, asked of the rules: no row of the depot's says them.
            await admission.RequireRoleAsync(PalletMembership.Loader, Cancellation);
            await Refused.WithCodeAsync(PalletMembership.Codes, MembershipRefusals.RoleNotForMembers, () => admission.RequireRoleAsync(new NamedRole("packer"), Cancellation).AsTask());
            (await admission.OwnerRoleAsync(Cancellation)).Should().Be(PalletMembership.Owner);
            provider.GetRequiredService<IMemberRoles<PalletId, NamedRole>>().Should().BeOfType<NamedRoles<PalletId>>();
        });
    }

    [Fact]
    public async Task Who_a_member_is_is_what_the_host_answers_and_never_the_callers_own_id()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        // A porter whose id happens to be the very id a user's token carries. That user is not that porter:
        // the depot never took it on, and a member is who the host says, not who the token says.
        var lookalike = new PorterId(data.Ivo.User);
        await depot.Services.ChangeAsync(async (context, _) =>
            (await context.Pallets.SingleAsync(pallet => pallet.Id == data.Limes, Cancellation)).PutOn(lookalike, PalletMembership.Loader, MemberPeriod.Open(data.Now.AddDays(-1)), data.Now, by: null));

        (await depot.Services.PalletsAsync(data.Ivo, access => access.HoldAsync(data.Limes, PalletKeys.Load, Cancellation))).Should().BeNull();
        depot.Commands.Reset();
        (await depot.Services.PalletsAsync(data.Ivo, access => access.KeysOnAsync([data.Limes], [PalletKeys.See, PalletKeys.Load], Cancellation))).Should().BeEmpty();
        depot.Commands.Count.Should().Be(0);

        // Once the depot knows the user as that porter, it is one.
        await depot.Services.ChangeAsync((context, desk) =>
        {
            var porter = new Porter { Id = lookalike, UserId = data.Ivo.User, Active = true };
            context.Porters.Add(porter);
            desk.TakeOn(porter);
            return Task.CompletedTask;
        });
        (await depot.Services.PalletsAsync(data.Ivo, access => access.HoldAsync(data.Limes, PalletKeys.Load, Cancellation)))!.Via.Should().Be(MemberVia.Members);

        // Only a signed-in user is asked about at all: the host is not asked who an anonymous caller is.
        (await depot.Services.AsAsync(Caller.Anonymous, provider => provider.GetRequiredService<IMemberQuestions<PalletId>>().HoldAsync(data.Limes, PalletKeys.See, Cancellation))).Should().BeNull();
    }

    [Fact]
    public async Task What_is_held_on_a_pallet_is_one_statement_over_the_pallets_own_rows()
    {
        using var depot = await SqliteDepot.SeededAsync();
        var data = depot.Scenario;

        (DepotPerson Person, string Key)[] asked = [(data.Bo, PalletKeys.Load), (data.Ana, PalletKeys.Strap), (data.Dot, PalletKeys.Load), (data.Cas, PalletKeys.See), (data.Bo, "pallets.unheard-of")];
        foreach (var (person, key) in asked)
        {
            depot.Commands.Reset();
            await depot.Services.PalletsAsync(person, access => access.HoldAsync(data.Pears, key, Cancellation));
            depot.Commands.Count.Should().Be(1, "seeing the pallet, holding " + key + " on it as " + person + ", until when, and its version are one statement");
        }

        depot.Commands.Reset();
        var keys = await depot.Services.PalletsAsync(data.Bo, access => access.KeysOnAsync([data.Pears, data.Plums, data.Limes], DepotScenario.PalletKeysAsked, Cancellation));
        depot.Commands.Count.Should().Be(1);
        keys[data.Pears].Should().BeEquivalentTo([PalletKeys.See, PalletKeys.Load]);
        keys[data.Plums].Should().BeEquivalentTo([PalletKeys.See, PalletKeys.Load, PalletKeys.Strap], "an owner holds every key of a pallet");
        keys.Should().NotContainKey(data.Limes);

        // Who the caller is was answered before the statement, and the roles are values of the rules: nothing
        // of the depot's is read, and a crate's rows are none of a pallet's business.
        depot.Commands.Commands.Should().ContainSingle().Which.Should().Contain("\"PalletPorters\"").And.Contain("\"PalletPorterRoles\"")
            .And.NotContain("\"Porters\"").And.NotContain("\"RoleKeys\"").And.NotContain("\"BayHolds\"").And.NotContain("\"CratePorters\"");
    }
}
