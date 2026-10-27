using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// A resource whose roles are kept: plots in gardens. A garden makes the roles of its plots for itself, as
/// rows of the host's role class, and a gardener holds one by its id. What a role gives is read from its row,
/// in the same one statement that reads the gardeners. Whose a role is, a garden's, is the host's own column
/// and the host's own rule: the package reads the roles through the host's context and assumes neither.
/// </summary>
public sealed class RolesACustomerMakesTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_role_a_garden_makes_gives_a_gardener_exactly_the_permissions_it_was_given()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;

        // The meadow makes a role, and says what it gives. White space around its name is dropped.
        var pruner = new PlotRole(PlotRoleId.CreateSequential(), data.Meadow, new CustomRoleDraft("  Pruner ", null, []));
        pruner.HaveItGive(PlotPermissions.Fence, PlotPermissions.Plant);
        await garden.Services.ChangeAsync(context =>
        {
            context.PlotRoles.Add(pruner);
            return Task.CompletedTask;
        });

        var kept = await garden.Services.ReadAsync(pruner.Id);
        (kept.Name, kept.Description, kept.Status, kept.MadeFrom, kept.GardenId).Should().Be(("Pruner", string.Empty, CustomRoleStatus.Active, null, data.Meadow));
        kept.Permissions.Should().Equal(PlotPermissions.Fence, PlotPermissions.Plant);

        // Di is on the beans with no role: she sees them, and that is all.
        (await PermissionsOnTheBeansAsync(garden, data.Di)).Should().BeEquivalentTo([PlotPermissions.See]);

        // The role goes to a gardener as any role does: the admission knows it, in its garden, and the plot gives it.
        await garden.Services.InAsync(data.Meadow, async provider =>
        {
            await Admitted.RoleAsync<PlotId, PlotRoleId>(provider, PlotMembership.Codes, pruner.Id, Cancellation);
            return true;
        });
        await garden.Services.ChangeAsync(async context =>
            (await context.Plots.SingleAsync(plot => plot.Id == data.Beans, Cancellation)).GiveRole(data.Di.Member, pruner.Id, MemberPeriod.Open(garden.Clock.Now), garden.Clock.Now, by: data.Ada.Member));

        // She holds exactly what the role was given, and what being on the plot gives: no permission more.
        (await PermissionsOnTheBeansAsync(garden, data.Di)).Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Fence, PlotPermissions.Plant]);
        (await garden.Services.PlotsAsync(data.Di, access => access.HoldAsync(data.Beans, PlotPermissions.Fence, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        (await garden.Services.PlotsAsync(data.Di, access => access.HoldAsync(data.Beans, PlotPermissions.Water, Cancellation)))!.Via.Should().BeNull();
        await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.NotPermitted, () => garden.Services.PlotsAsync(data.Di, access => access.RequireAsync(data.Beans, PlotPermissions.Water, Cancellation)));

        // And on that plot alone: on the leeks she is nobody.
        (await garden.Services.PlotsAsync(data.Di, access => access.HoldAsync(data.Leeks, PlotPermissions.Fence, Cancellation))).Should().BeNull();
    }

    [Fact]
    public async Task A_role_given_other_permissions_gives_them_to_everybody_who_holds_it_at_the_next_question()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;

        // Ben tends the beans: see, plant and water, as the meadow's tender gives them. Di is made a tender there as well.
        await garden.Services.ChangeAsync(async context =>
            (await context.Plots.SingleAsync(plot => plot.Id == data.Beans, Cancellation)).GiveRole(data.Di.Member, data.MeadowTender.Id, MemberPeriod.Open(garden.Clock.Now), garden.Clock.Now, by: data.Ada.Member));
        (await PermissionsOnTheBeansAsync(garden, data.Ben)).Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Water]);
        (await PermissionsOnTheBeansAsync(garden, data.Di)).Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Water]);
        var gardeners = (await garden.Services.ReadAsync(data.Beans)).Version;

        // The meadow has its tender fence instead of water. No member row is touched.
        await garden.Services.ChangeAsync(data.MeadowTender.Id, role => role.HaveItGive(PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Fence));

        (await PermissionsOnTheBeansAsync(garden, data.Ben)).Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Fence]);
        (await PermissionsOnTheBeansAsync(garden, data.Di)).Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Fence]);
        (await garden.Services.ReadAsync(data.Beans)).Version.Should().Be(gardeners, "a role is an aggregate of its own: the plot and its gardeners are as they were");

        // The orchard's tender is another row: what the meadow did to its own changed nothing there.
        (await garden.Services.PlotsAsync(data.Hal, access => access.PermissionsOnAsync([data.Kale], GardenScenario.PermissionsAsked, Cancellation)))[data.Kale]
            .Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Plant]);
    }

    [Fact]
    public async Task A_permission_the_rules_do_not_let_a_members_role_give_is_refused_and_nothing_changes()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;

        // Selling a plot is the owner's alone: the rules keep it from every member's role. And a permission the
        // rules never named is no permission a role can give either. Every permission refused is named, in one refusal.
        var refusal = await Refused.WithCodeAsync(
            PlotMembership.Codes,
            MembershipRefusals.PermissionNotForMembers,
            () => garden.Services.ChangeAsync(data.Fencer.Id, role => role.HaveItGive(PlotPermissions.Fence, PlotPermissions.Sell, "plots.unheard-of")));
        refusal.Code.Should().Be("plots.permission-not-for-members");
        refusal.Arguments["Permissions"].Should().Be("plots.sell, plots.unheard-of");
        refusal.Arguments[RefusalException.FieldArgument].Should().Be("permissions");

        // Nothing changed: the role holds what it held, and gives what it gave.
        (await garden.Services.ReadAsync(data.Fencer.Id)).Permissions.Should().Equal(PlotPermissions.Fence, PlotPermissions.See);
        (await PermissionsOnTheBeansAsync(garden, data.Eve)).Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Fence]);
        garden.Services.Raised.Should().BeEmpty();

        // A role is not made with such a permission either.
        Refused.With(PlotMembership.Codes, MembershipRefusals.PermissionNotForMembers, () => _ = new PlotRole(PlotRoleId.CreateSequential(), data.Meadow, new CustomRoleDraft("Seller", null, [PlotPermissions.Sell])))
            .Arguments["Permissions"].Should().Be(PlotPermissions.Sell);

        // The owner holds it all the same, by owning the plot, and no role of anybody's has a part in that.
        (await garden.Services.PlotsAsync(data.Ada, access => access.HoldAsync(data.Beans, PlotPermissions.Sell, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        (await garden.Services.PlotsAsync(data.Ben, access => access.HoldAsync(data.Beans, PlotPermissions.Sell, Cancellation)))!.Via.Should().BeNull();
    }

    [Fact]
    public async Task A_permission_the_rules_no_longer_let_a_role_give_is_given_by_no_role_whatever_its_row_still_holds()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;

        // A row from before the rules changed: the fencer still holds selling, which a role could give then.
        await garden.Services.ChangeAsync(data.Fencer.Id, role => Break.Set(role, nameof(PlotRole.Permissions), new[] { PlotPermissions.Fence, PlotPermissions.See, PlotPermissions.Sell }));
        (await garden.Services.ReadAsync(data.Fencer.Id)).Permissions.Should().Contain(PlotPermissions.Sell);

        // What the row holds is cut by what a member's role can give now, where it is read.
        (await PermissionsOnTheBeansAsync(garden, data.Eve)).Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Fence]);
        (await garden.Services.PlotsAsync(data.Eve, access => access.HoldAsync(data.Beans, PlotPermissions.Sell, Cancellation)))!.Via.Should().BeNull();
        garden.Commands.Reset();
        (await garden.Services.AsAsync(data.Eve, provider => provider.GetRequiredService<GardenContext>().Plots
            .Within(provider.GetRequiredService<MembershipAccessManager<Plot>>().Reach(PlotPermissions.Sell)).CountAsync(Cancellation))).Should().Be(0);
        garden.Commands.Commands.Should().ContainSingle().Which.Should().NotContain("\"PlotRoles\"", "a permission no role can give is not looked for among the roles");
    }

    [Fact]
    public async Task An_archived_role_gives_nothing_from_then_on_is_not_given_again_and_stays_on_who_holds_it()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;

        // Eve fences the beans, through the meadow's fencer.
        (await garden.Services.PlotsAsync(data.Eve, access => access.HoldAsync(data.Beans, PlotPermissions.Fence, Cancellation)))!.Via.Should().Be(MemberVia.Members);

        await garden.Services.ChangeAsync(data.Fencer.Id, role => role.PutAway());

        // From then on it gives nothing: she is still on the plot, and sees it, without the permission.
        var held = await garden.Services.PlotsAsync(data.Eve, access => access.HoldAsync(data.Beans, PlotPermissions.Fence, Cancellation));
        held.Should().NotBeNull();
        held!.Via.Should().BeNull();
        (await PermissionsOnTheBeansAsync(garden, data.Eve)).Should().BeEquivalentTo([PlotPermissions.See]);

        // It stays on who holds it: her row is as it was, and the role is still there to be read.
        var beans = await garden.Services.ReadAsync(data.Beans);
        beans.Gardeners.Single(row => row.MemberId == data.Eve.Member).Roles.Should().ContainSingle().Which.RoleId.Should().Be(data.Fencer.Id);
        var archived = await garden.Services.ReadAsync(data.Fencer.Id);
        (archived.Status, archived.Name).Should().Be((CustomRoleStatus.Archived, "Fencer"));
        archived.Permissions.Should().Equal(PlotPermissions.Fence, PlotPermissions.See);

        // It is not given again: to the admission of plots it is no role there is.
        await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.RoleNotForMembers, () => garden.Services.InAsync(data.Meadow, async provider =>
        {
            await Admitted.RoleAsync<PlotId, PlotRoleId>(provider, PlotMembership.Codes, data.Fencer.Id, Cancellation);
            return true;
        }));

        // And it changes no more.
        await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.RoleIsArchived, () => garden.Services.ChangeAsync(data.Fencer.Id, role => role.HaveItGive(PlotPermissions.See)));
        await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.RoleIsArchived, () => garden.Services.ChangeAsync(data.Fencer.Id, role => role.CallIt("Hedger", null)));
        await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.RoleIsArchived, () => garden.Services.ChangeAsync(data.Fencer.Id, role => role.PutAway()));

        // The seasonal hand was put away before anybody asked: Fay never held what it holds.
        (await PermissionsOnTheBeansAsync(garden, data.Fay)).Should().BeEquivalentTo([PlotPermissions.See]);
    }

    [Fact]
    public async Task Starter_roles_are_made_once_for_a_garden_and_never_twice()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;
        var paddock = GardenId.CreateSequential();

        // A garden that is new has no role. The host asks for its first ones, saying which garden it means.
        var made = await garden.Services.AsAsync(CurrentUserSnapshot.System, provider => provider.GetRequiredService<GardenRoles>().MakeFirstAsync(paddock, Cancellation));

        made.Select(role => (role.Name, role.MadeFrom, role.GardenId, role.Status)).Should().Equal(
            [(PlotMembership.Tender, PlotMembership.Tender, paddock, CustomRoleStatus.Active), (PlotMembership.Waterer, PlotMembership.Waterer, paddock, CustomRoleStatus.Active)],
            "the starter roles, and none for the owner, which holds no role");
        made[0].Permissions.Should().Equal(PlotPermissions.Plant, PlotPermissions.See, PlotPermissions.Water);
        made[1].Permissions.Should().Equal(PlotPermissions.See, PlotPermissions.Water);

        // Asked again, nothing is made: each starter role is there already.
        (await garden.Services.AsAsync(CurrentUserSnapshot.System, provider => provider.GetRequiredService<GardenRoles>().MakeFirstAsync(paddock, Cancellation))).Should().BeEmpty();

        // A starter role is known by what it was made from, not by what it is called or whether it is in use:
        // renamed and put away, it is still not made a second time.
        await garden.Services.ChangeAsync(made[0].Id, role => role.CallIt("Grower", "Sows and waters."));
        await garden.Services.ChangeAsync(made[1].Id, role => role.PutAway());
        (await garden.Services.AsAsync(CurrentUserSnapshot.System, provider => provider.GetRequiredService<GardenRoles>().MakeFirstAsync(paddock, Cancellation))).Should().BeEmpty();

        // Whichever garden the request that asks is in: the host said which garden it means.
        (await garden.Services.InAsync(data.Orchard, provider => provider.GetRequiredService<GardenRoles>().MakeFirstAsync(paddock, Cancellation))).Should().BeEmpty();
        (await garden.Services.InAsync(paddock, provider => provider.GetRequiredService<GardenContext>().PlotRoles.CountAsync(Cancellation))).Should().Be(2);

        // The meadow and the orchard had theirs already, and each keeps its own.
        (await garden.Services.AsAsync(CurrentUserSnapshot.System, provider => provider.GetRequiredService<GardenRoles>().MakeFirstAsync(data.Meadow, Cancellation))).Should().BeEmpty();
        (await garden.Services.InAsync(data.Meadow, provider => provider.GetRequiredService<GardenContext>().PlotRoles.CountAsync(Cancellation))).Should().Be(5);
        (await garden.Services.InAsync(data.Orchard, provider => provider.GetRequiredService<GardenContext>().PlotRoles.CountAsync(Cancellation))).Should().Be(3);

        // Two requests that ask at the same moment are kept apart by the host's own index over its garden and
        // what a role was made from: a second tender made from the starter role is not saved.
        var second = new PlotRole(PlotRoleId.CreateSequential(), paddock, new CustomRoleDraft("Second tender", null, PlotMembership.Rules.PermissionsOf(new NamedRole(PlotMembership.Tender)), PlotMembership.Tender));
        await FluentActions.Awaiting(() => garden.Services.ChangeAsync(context =>
        {
            context.PlotRoles.Add(second);
            return Task.CompletedTask;
        })).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task An_owner_holds_what_the_permissions_give_an_owner_by_owning_and_no_role_is_the_owners()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;

        // A plot is opened with no role for its owner, whatever roles the garden has, and none it has is the owner's.
        var peas = new Plot(PlotId.CreateSequential(), data.Meadow, "Peas", data.Di.Member, garden.Clock.Now);
        await garden.Services.ChangeAsync(context =>
        {
            context.Plots.Add(peas);
            return Task.CompletedTask;
        });
        (await garden.Services.ReadAsync(peas.Id)).Gardeners.Should().ContainSingle().Which.Roles.Should().BeEmpty();

        // What the owner may do does not hang on a role: she holds every permission of her plot by owning it, selling it too.
        (await garden.Services.PlotsAsync(data.Di, access => access.PermissionsOnAsync([peas.Id], GardenScenario.PermissionsAsked, Cancellation)))[peas.Id]
            .Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Water, PlotPermissions.Fence, PlotPermissions.Sell]);

        // Any role is put away alike: no role is one every owner holds.
        await garden.Services.ChangeAsync(data.MeadowHolder.Id, role => role.PutAway());
        (await garden.Services.ReadAsync(data.MeadowHolder.Id)).Status.Should().Be(CustomRoleStatus.Archived);
        (await garden.Services.PlotsAsync(data.Di, access => access.PermissionsOnAsync([peas.Id], GardenScenario.PermissionsAsked, Cancellation)))[peas.Id]
            .Should().HaveCount(5, "the owner's permissions were never a role's");
    }

    [Fact]
    public async Task Where_a_role_came_from_is_written_once_and_a_save_that_changed_it_is_refused()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;

        // What a starter role was made from tells it apart from the roles a garden made, so that is fixed once its
        // row is there: the model says so for both role classes, as it says it of a permission.
        var behaviors = await garden.Services.AsAsync(CurrentUserSnapshot.System, provider =>
        {
            var model = provider.GetRequiredService<GardenContext>().Model;
            return Task.FromResult(new[] { typeof(PlotRole), typeof(ShedRole) }
                .Select(roleClass => model.FindEntityType(roleClass)!.FindProperty(nameof(PlotRole.MadeFrom))!.GetAfterSaveBehavior())
                .ToList());
        });
        behaviors.Should().Equal(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw, Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);

        // A save that changed it, by whatever went round the role's own methods, is refused, and nothing of it is kept.
        var moved = await FluentActions.Awaiting(() => garden.Services.ChangeAsync(data.MeadowTender.Id, role =>
            {
                role.CallIt("Grower", null);
                Break.Set(role, nameof(PlotRole.MadeFrom), PlotMembership.Waterer);
            }))
            .Should().ThrowAsync<InvalidOperationException>();
        moved.Which.Message.Should().Contain(nameof(PlotRole.MadeFrom));

        var kept = await garden.Services.ReadAsync(data.MeadowTender.Id);
        (kept.MadeFrom, kept.Name).Should().Be((PlotMembership.Tender, data.MeadowTender.Name));

        // A role made from no starter role stays one: it is not made a starter role's afterwards.
        await FluentActions.Awaiting(() => garden.Services.ChangeAsync(data.Fencer.Id, role => Break.Set(role, nameof(PlotRole.MadeFrom), PlotMembership.Tender)))
            .Should().ThrowAsync<InvalidOperationException>();
        (await garden.Services.ReadAsync(data.Fencer.Id)).MadeFrom.Should().BeNull();
    }

    [Fact]
    public async Task The_roles_of_two_gardens_do_not_mix_under_the_hosts_rule()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;

        // The host's rule: a request in a garden reads that garden's roles. The package reads through the
        // host's context, so what it answers follows the rule without knowing it.
        (await garden.Services.InAsync(data.Meadow, provider => provider.GetRequiredService<GardenContext>().PlotRoles.Select(role => role.Id).ToListAsync(Cancellation)))
            .Should().BeEquivalentTo([data.MeadowHolder.Id, data.MeadowTender.Id, data.MeadowWaterer.Id, data.Fencer.Id, data.Seasonal.Id]);

        // Both gardens have a tender, each a row of its own with permissions of its own.
        (data.MeadowTender.Name, data.OrchardTender.Name).Should().Be((PlotMembership.Tender, PlotMembership.Tender));

        async Task<bool> MayBeGivenInAsync(GardenId where, PlotRoleId role)
        {
            try
            {
                await garden.Services.InAsync(where, async provider =>
                {
                    await Admitted.RoleAsync<PlotId, PlotRoleId>(provider, PlotMembership.Codes, role, Cancellation);
                    return true;
                });
                return true;
            }
            catch (RefusalException refused) when (refused.Code == PlotMembership.Codes[MembershipRefusals.RoleNotForMembers])
            {
                return false;
            }
        }

        // A role of the one garden is no role there is in the other.
        (await MayBeGivenInAsync(data.Meadow, data.MeadowTender.Id)).Should().BeTrue();
        (await MayBeGivenInAsync(data.Meadow, data.OrchardTender.Id)).Should().BeFalse("the orchard's tender is not a role of the meadow's plots");
        (await MayBeGivenInAsync(data.Orchard, data.OrchardTender.Id)).Should().BeTrue();
        (await MayBeGivenInAsync(data.Orchard, data.Fencer.Id)).Should().BeFalse();

        // A role of the orchard's that got onto a plot of the meadow all the same, given by code that did not
        // ask the admission. The rule applies where a role's permissions are read as well: in the meadow that row is
        // no role there is, so Di holds nothing through it.
        await garden.Services.ChangeAsync(async context =>
            (await context.Plots.SingleAsync(plot => plot.Id == data.Beans, Cancellation)).GiveRole(data.Di.Member, data.OrchardTender.Id, MemberPeriod.Open(garden.Clock.Now), garden.Clock.Now, by: null));
        (await garden.Services.ReadAsync(data.Beans)).Gardeners.Single(row => row.MemberId == data.Di.Member).Roles.Should().ContainSingle().Which.RoleId.Should().Be(data.OrchardTender.Id);
        (await PermissionsOnTheBeansAsync(garden, data.Di)).Should().BeEquivalentTo([PlotPermissions.See]);
        (await garden.Services.PlotsAsync(data.Di, access => access.HoldAsync(data.Beans, PlotPermissions.Plant, Cancellation)))!.Via.Should().BeNull();

        // The rule is the host's, and the package knows no other: asked in a request the host put in no garden,
        // the same row gives what it holds.
        (await garden.Services.AsAsync(data.Di.Caller, provider => provider.GetRequiredService<MembershipAccessManager<Plot>>().HoldAsync(data.Beans, PlotPermissions.Plant, Cancellation)))!
            .Via.Should().Be(MemberVia.Members);
    }

    [Fact]
    public async Task A_role_of_plots_gives_its_permissions_from_the_moment_it_starts_to_the_moment_it_ends_and_no_longer_than_the_place_on_the_plot()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;
        var tick = TimeSpan.FromTicks(1);

        Task<MemberHold<PlotId>?> HoldAsync(string permission) => garden.Services.PlotsAsync(data.Cy, access => access.HoldAsync(data.Beans, permission, Cancellation));

        // Cy is on the beans until next week, and waters them for three days. He is given the fencer from an
        // hour from now on, with no end of its own.
        var inAnHour = garden.Clock.Now.AddHours(1);
        await garden.Services.ChangeAsync(async context =>
            (await context.Plots.SingleAsync(plot => plot.Id == data.Beans, Cancellation)).GiveRole(data.Cy.Member, data.Fencer.Id, MemberPeriod.Open(inAnHour), garden.Clock.Now, by: data.Ada.Member));
        var version = (await garden.Services.ReadAsync(data.Beans)).Version;

        // A role that has not started gives nothing yet, and gives its permissions at the very moment it starts: for
        // as long as his place on the plot runs, since the role has no end of its own.
        garden.Clock.Advance(TimeSpan.FromHours(1) - tick);
        (await HoldAsync(PlotPermissions.Fence))!.Via.Should().BeNull();
        garden.Clock.Advance(tick);
        (await HoldAsync(PlotPermissions.Fence)).Should().Be(new MemberHold<PlotId>(data.Beans, MemberVia.Members, version, data.NextWeek));

        // One tick before the watering ends it is his still, until the moment the hold names; at that moment
        // it is gone, and he is on the plot as before.
        garden.Clock.Advance(data.InThreeDays - garden.Clock.Now - tick);
        (await HoldAsync(PlotPermissions.Water)).Should().Be(new MemberHold<PlotId>(data.Beans, MemberVia.Members, version, data.InThreeDays));
        garden.Clock.Advance(tick);
        (await HoldAsync(PlotPermissions.Water)).Should().Be(new MemberHold<PlotId>(data.Beans, null, version, null));
        (await garden.Services.PlotsAsync(data.Cy, access => access.PermissionsOnAsync([data.Beans], GardenScenario.PermissionsAsked, Cancellation)))[data.Beans]
            .Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Fence]);

        // A role counts only inside its place on the plot: the fencer has no end, and gives nothing from the
        // moment the place ends, when the plot is no longer there for him.
        garden.Clock.Advance(data.NextWeek - garden.Clock.Now - tick);
        (await HoldAsync(PlotPermissions.Fence))!.Until.Should().Be(data.NextWeek);
        garden.Clock.Advance(tick);
        (await HoldAsync(PlotPermissions.Fence)).Should().BeNull();
        (await garden.Services.AsAsync(data.Cy, provider => provider.GetRequiredService<GardenContext>().Plots
            .Within(provider.GetRequiredService<MembershipAccessManager<Plot>>().Reach(PlotPermissions.Fence)).CountAsync(Cancellation))).Should().Be(0);
    }

    [Fact]
    public async Task A_host_with_a_factory_for_its_context_and_no_context_of_a_request_is_answered_on_a_context_of_the_factorys()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var clock = new FixedClock();
        var data = new GardenScenario(clock.Now);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IDbContextFactory<GardenContext>>(new GardenContexts(new DbContextOptionsBuilder<GardenContext>().UseSqlite(connection).Options));
        services.AddMembership<PlotGardener, PlotGardenerId, UserId, PlotRoleId, GardenContext, Plot, PlotId>(PlotMembership.Rules);
        await using var provider = services.BuildServiceProvider();

        await using (var seeding = await provider.GetRequiredService<IDbContextFactory<GardenContext>>().CreateDbContextAsync(Cancellation))
        {
            await seeding.Database.EnsureCreatedAsync(Cancellation);
            seeding.AddRange(data.Roles);
            seeding.AddRange(data.Plots);
            await seeding.SaveChangesAsync(Cancellation);
        }

        // What the admission asks about the roles is read on a context of the factory's, under the host's rule
        // all the same: the garden a request is in is not kept in a context.
        using (TestUser.BeginSystem())
        using (GardenOfTheRequest.Begin(data.Meadow))
        {
            await using var scope = provider.CreateAsyncScope();
            (scope.ServiceProvider.GetService<GardenContext>() is null).Should().BeTrue("this host registered a factory of its own, and no context of a request");

            await Admitted.RoleAsync<PlotId, PlotRoleId>(scope.ServiceProvider, PlotMembership.Codes, data.Fencer.Id, Cancellation);
            await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.RoleNotForMembers, () => Admitted.RoleAsync<PlotId, PlotRoleId>(scope.ServiceProvider, PlotMembership.Codes, data.Seasonal.Id, Cancellation));
            await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.RoleNotForMembers, () => Admitted.RoleAsync<PlotId, PlotRoleId>(scope.ServiceProvider, PlotMembership.Codes, data.OrchardTender.Id, Cancellation));
        }

        // And so is what a gardener holds through a role.
        using (TestUser.BeginAs(data.Eve.Caller))
        using (GardenOfTheRequest.Begin(data.Meadow))
        {
            await using var scope = provider.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<MembershipAccessManager<Plot>>().HoldAsync(data.Beans, PlotPermissions.Fence, Cancellation))!.Via.Should().Be(MemberVia.Members);
        }
    }

    [Fact]
    public async Task A_role_of_another_scope_gives_nothing_where_the_host_has_a_factory_and_a_filter_on_the_requests_context()
    {
        // A host that registers its context for a request and a factory beside it, as one with a pool does, and
        // keeps the garden a request is in on the request's own context: its rule about whose roles a request
        // reads is that context's, and a context the factory makes knows nothing of it.
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var clock = new FixedClock();
        var data = new GardenScenario(clock.Now);
        var options = new DbContextOptionsBuilder<GardenContext>().UseSqlite(connection).Options;
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped(_ => new GardenContext(options));
        services.AddSingleton<IDbContextFactory<GardenContext>>(new GardenContexts(options));
        services.AddMembership<PlotGardener, PlotGardenerId, UserId, PlotRoleId, GardenContext, Plot, PlotId>(PlotMembership.Rules);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // A role of the orchard's that got onto a plot of the meadow, given by code that did not ask the admission.
        data.Plots.Single(plot => plot.Id == data.Beans).GiveRole(data.Di.Member, data.OrchardTender.Id, MemberPeriod.Open(clock.Now), clock.Now, by: null);
        await using (var seeding = await provider.GetRequiredService<IDbContextFactory<GardenContext>>().CreateDbContextAsync(Cancellation))
        {
            await seeding.Database.EnsureCreatedAsync(Cancellation);
            seeding.AddRange(data.Roles);
            seeding.AddRange(data.Plots);
            await seeding.SaveChangesAsync(Cancellation);
        }

        using (TestUser.BeginAs(data.Di.Caller))
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<GardenContext>().GardenOfThisRequest = data.Meadow;
            var access = scope.ServiceProvider.GetRequiredService<MembershipAccessManager<Plot>>();

            // The admission refuses the orchard's role in a request of the meadow's, and the questions agree:
            // read on the request's own context, under the same rule, that row is no role there is. Di is on
            // the beans, and holds nothing through it. On a context of the factory's the rule would not
            // apply, and the role would give what it holds.
            await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.RoleNotForMembers, () =>
                Admitted.RoleAsync<PlotId, PlotRoleId>(scope.ServiceProvider, PlotMembership.Codes, data.OrchardTender.Id, Cancellation));
            var hold = await access.HoldAsync(data.Beans, PlotPermissions.Plant, Cancellation);
            (hold!.Via, hold.Until).Should().Be((null, null));
            (await access.PermissionsOnAsync([data.Beans], GardenScenario.PermissionsAsked, Cancellation))[data.Beans].Should().BeEquivalentTo([PlotPermissions.See]);
            (await scope.ServiceProvider.GetRequiredService<GardenContext>().Plots.Within(access.Reach(PlotPermissions.Plant)).CountAsync(Cancellation)).Should().Be(0);
        }

        // A role of the meadow's gives what it holds, read the same way.
        using (TestUser.BeginAs(data.Eve.Caller))
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<GardenContext>().GardenOfThisRequest = data.Meadow;
            (await scope.ServiceProvider.GetRequiredService<MembershipAccessManager<Plot>>().HoldAsync(data.Beans, PlotPermissions.Fence, Cancellation))!.Via.Should().Be(MemberVia.Members);
        }
    }

    [Fact]
    public async Task A_name_is_used_once_in_a_garden_by_an_index_the_host_declares()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;

        // The package keeps no rule about names beyond what a name is: that one is used once among the roles
        // of a garden is the host's index over its own column, refusing in the host's own code.
        var refusal = (await FluentActions.Awaiting(() => garden.Services.ChangeAsync(context =>
        {
            context.PlotRoles.Add(new PlotRole(PlotRoleId.CreateSequential(), data.Meadow, new CustomRoleDraft("Fencer", null, [PlotPermissions.See])));
            return Task.CompletedTask;
        })).Should().ThrowAsync<RefusalException>()).Which;
        (refusal.Code, refusal.Kind).Should().Be((PlotMembership.RoleNameTaken, RefusalKind.Conflict));
        refusal.Message.Should().Be("Another role of this garden is called Fencer already.");

        // In another garden the name is free.
        await garden.Services.ChangeAsync(context =>
        {
            context.PlotRoles.Add(new PlotRole(PlotRoleId.CreateSequential(), data.Orchard, new CustomRoleDraft("Fencer", null, [PlotPermissions.See])));
            return Task.CompletedTask;
        });
        (await garden.Services.InAsync(data.Orchard, provider => provider.GetRequiredService<GardenContext>().PlotRoles.CountAsync(role => role.Name == "Fencer", Cancellation))).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_questions_answer_what_the_rules_say_read_in_memory_for_everybody_and_every_permission(bool ownContexts)
    {
        using var garden = await SqliteGarden.SeededAsync(ownContexts: ownContexts);
        var data = garden.Scenario;
        var plots = data.Plots.Select(plot => plot.Id).ToArray();

        foreach (var person in data.People)
        {
            var seen = data.PlotsSeenBy(person);
            (await garden.Services.AsAsync(person, async provider =>
            {
                var see = provider.GetRequiredService<MembershipAccessManager<Plot>>().ReachSeen();
                return await provider.GetRequiredService<GardenContext>().Plots.Within(see).Select(plot => plot.Id).ToListAsync(Cancellation);
            })).Should().BeEquivalentTo(seen, "of what " + person + " sees");

            var keysOn = await garden.Services.PlotsAsync(person, access => access.PermissionsOnAsync(plots, GardenScenario.PermissionsAsked, Cancellation));
            foreach (var permission in GardenScenario.PermissionsAsked)
            {
                var held = data.PlotsHeldBy(person, permission);
                var because = "of what " + person + " holds " + permission + " on";

                (await garden.Services.AsAsync(person, async provider =>
                {
                    var reach = provider.GetRequiredService<MembershipAccessManager<Plot>>().Reach(permission);
                    return await provider.GetRequiredService<GardenContext>().Plots.Within(reach).Select(plot => plot.Id).ToListAsync(Cancellation);
                })).Should().BeEquivalentTo(held, because);

                // Asked about many at once: the same permissions, on the plots the caller sees.
                keysOn.Where(pair => pair.Value.Contains(permission)).Select(pair => pair.Key).Should().BeEquivalentTo(held.Intersect(seen), because);

                foreach (var plot in plots)
                {
                    var hold = await garden.Services.PlotsAsync(person, access => access.HoldAsync(plot, permission, Cancellation));
                    (hold is not null).Should().Be(seen.Contains(plot), "of whether " + person + " sees the plot");
                    (hold?.Via is not null).Should().Be(held.Contains(plot) && seen.Contains(plot), because);
                }
            }
        }
    }

    [Fact]
    public async Task What_a_gardener_holds_through_a_role_of_plots_is_one_statement_with_the_role_rows_inside()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;
        var commands = garden.Commands;
        var beans = await garden.Services.ReadAsync(data.Beans);

        // How one permission is held on one plot, until when, and the plot's version: one statement, which reads the
        // gardeners, the roles they hold and the rows of those roles.
        commands.Reset();
        var water = await garden.Services.PlotsAsync(data.Cy, access => access.HoldAsync(data.Beans, PlotPermissions.Water, Cancellation));
        commands.Count.Should().Be(1);
        commands.Commands[0].Should().Contain("\"PlotGardeners\"").And.Contain("\"PlotGardenerRoles\"").And.Contain("\"PlotRoles\"");
        water.Should().Be(new MemberHold<PlotId>(data.Beans, MemberVia.Members, beans.Version, data.InThreeDays), "a role held for three days gives its permissions for three days");

        // The permission being on a plot gives asks nothing of the roles; neither does one only its owner holds.
        commands.Reset();
        (await garden.Services.PlotsAsync(data.Cy, access => access.HoldAsync(data.Beans, PlotPermissions.See, Cancellation)))
            .Should().Be(new MemberHold<PlotId>(data.Beans, MemberVia.Members, beans.Version, data.NextWeek));
        (await garden.Services.PlotsAsync(data.Ada, access => access.HoldAsync(data.Beans, PlotPermissions.Sell, Cancellation)))
            .Should().Be(new MemberHold<PlotId>(data.Beans, MemberVia.Members, beans.Version, Until: null));
        commands.Count.Should().Be(2);
        commands.Commands.Should().OnlyContain(sql => !sql.Contains("\"PlotRoles\""));

        // The permissions held on many plots: one statement.
        commands.Reset();
        var permissions = await garden.Services.PlotsAsync(data.Ada, access => access.PermissionsOnAsync([data.Beans, data.Leeks, data.Kale], GardenScenario.PermissionsAsked, Cancellation));
        commands.Count.Should().Be(1);
        permissions.Keys.Should().BeEquivalentTo([data.Beans, data.Leeks], "the kale is in another garden, and she is not on it");
        permissions[data.Beans].Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Water, PlotPermissions.Fence, PlotPermissions.Sell]);
        permissions[data.Leeks].Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Water]);

        // A page of the host's own with two reaches in it, and what the admission asks about a role: one each.
        commands.Reset();
        var names = await garden.Services.AsAsync(data.Ada, async provider =>
        {
            var access = provider.GetRequiredService<MembershipAccessManager<Plot>>();
            return await provider.GetRequiredService<GardenContext>().Plots
                .Within(access.Reach(PlotPermissions.See))
                .Within(access.Reach(PlotPermissions.Water))
                .OrderBy(plot => plot.Name)
                .Select(plot => plot.Name)
                .ToListAsync(Cancellation);
        });
        names.Should().Equal("Beans", "Leeks");
        commands.Count.Should().Be(1);

        commands.Reset();
        await garden.Services.InAsync(data.Meadow, async provider =>
        {
            await Admitted.RoleAsync<PlotId, PlotRoleId>(provider, PlotMembership.Codes, data.Fencer.Id, Cancellation);
            return true;
        });
        commands.Count.Should().Be(1);

        // Somebody who did not sign in is nobody's gardener: nothing is read for it.
        commands.Reset();
        (await garden.Services.AsAsync(CurrentUserSnapshot.Anonymous, provider => provider.GetRequiredService<MembershipAccessManager<Plot>>().HoldAsync(data.Beans, PlotPermissions.Water, Cancellation))).Should().BeNull();
        commands.Count.Should().Be(0);
    }

    [Fact]
    public async Task A_reach_of_plots_in_a_statement_on_another_context_is_handed_that_context()
    {
        using var garden = await SqliteGarden.SeededAsync(ownContexts: true);
        var data = garden.Scenario;

        await garden.Services.AsAsync(data.Ben, async provider =>
        {
            var water = provider.GetRequiredService<MembershipAccessManager<Plot>>().Reach(PlotPermissions.Water);
            await using var own = await provider.GetRequiredService<IDbContextFactory<GardenContext>>().CreateDbContextAsync(Cancellation);

            // The roles that give the permission are a query over the context the statement runs on, so a statement
            // on a context made for one reading says which context that is.
            FluentActions.Invoking(() => own.Plots.Within(water)).Should().Throw<InvalidOperationException>()
                .WithMessage("This reach of Plot puts the roles that give the permission into the query, the rows of its role class*hand over the context it was made from*WhereIHold(permission, db)*Within(reach, db)*");
            (await own.Plots.Within(water, own).Select(plot => plot.Id).ToListAsync(Cancellation)).Should().BeEquivalentTo([data.Beans, data.Leeks]);

            // On the request's own context nothing more is said.
            (await provider.GetRequiredService<GardenContext>().Plots.Within(water).CountAsync(Cancellation)).Should().Be(2);
            return true;
        });
    }

    [Fact]
    public async Task What_happened_to_a_role_is_said_by_the_hosts_own_events_and_by_none_of_the_packages()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;
        var raised = garden.Services.Raised;

        await garden.Services.ChangeAsync(data.MeadowWaterer.Id, role =>
        {
            role.CallIt("Waterer", "Waters, and nothing else.");
            role.HaveItGive(PlotPermissions.Water, PlotPermissions.Fence);
        });

        // Each operation answered what happened, and the host raised its own event from it.
        raised.Should().HaveCount(2);
        raised[0].Should().BeOfType<PlotRoleRenamed>().Which.Name.Should().Be("Waterer");
        var changed = raised[1].Should().BeOfType<PlotRolePermissionsChanged>().Which;
        changed.Added.Should().Equal(PlotPermissions.Fence);
        changed.Removed.Should().Equal(PlotPermissions.See);

        // What changes nothing answers that nothing happened, so nothing is raised.
        raised.Clear();
        await garden.Services.ChangeAsync(data.MeadowWaterer.Id, role =>
        {
            role.CallIt(" Waterer ", "Waters, and nothing else.");
            role.HaveItGive(PlotPermissions.Fence, PlotPermissions.Water, PlotPermissions.Fence);
        });
        raised.Should().BeEmpty();

        await garden.Services.ChangeAsync(data.MeadowWaterer.Id, role => role.PutAway());
        raised.Should().ContainSingle().Which.Should().BeOfType<PlotRolePutAway>();
        raised.Should().OnlyContain(@event => @event.GetType().Namespace == typeof(PlotRole).Namespace, "a role raises nothing of the package's");
    }

    [Fact]
    public async Task Two_kinds_of_resource_in_one_context_each_keep_roles_of_their_own()
    {
        using var garden = await SqliteGarden.SeededAsync();
        var data = garden.Scenario;
        var sheds = data.Sheds.Select(shed => shed.Id).ToArray();

        // Each resource's roles are found by the resource its role class names, in the one model.
        await garden.Services.AsAsync(CurrentUserSnapshot.System, provider =>
        {
            var model = provider.GetRequiredService<GardenContext>().Model;
            MembershipModel.MembersOf(model.FindEntityType(typeof(Plot))!)!.RoleClass!.ClrType.Should().Be(typeof(PlotRole));
            MembershipModel.MembersOf(model.FindEntityType(typeof(Shed))!)!.RoleClass!.ClrType.Should().Be(typeof(ShedRole));
            return Task.CompletedTask;
        });

        // What the sheds' rules say, read in memory, is what the questions answer, for everybody and every permission.
        foreach (var person in data.People)
        {
            var seen = data.ShedsSeenBy(person);
            var keysOn = await garden.Services.AsAsync(person, provider => provider.GetRequiredService<MembershipAccessManager<Shed>>().PermissionsOnAsync(sheds, GardenScenario.ShedPermissionsAsked, Cancellation));
            foreach (var permission in GardenScenario.ShedPermissionsAsked)
            {
                var held = data.ShedsHeldBy(person, permission);
                (await garden.Services.AsAsync(person, async provider =>
                {
                    var reach = provider.GetRequiredService<MembershipAccessManager<Shed>>().Reach(permission);
                    return await provider.GetRequiredService<GardenContext>().Sheds.Within(reach).Select(shed => shed.Id).ToListAsync(Cancellation);
                })).Should().BeEquivalentTo(held, "of what " + person + " holds " + permission + " on");
                keysOn.Where(pair => pair.Value.Contains(permission)).Select(pair => pair.Key).Should().BeEquivalentTo(held.Intersect(seen));
            }
        }

        // Ben lends tools through the lender: a permission of another module's, which a role of sheds may give
        // without the sheds naming it. Di is in the shed with no role, and being a hand gives no permission by itself.
        garden.Commands.Reset();
        (await garden.Services.AsAsync(data.Ben, provider => provider.GetRequiredService<MembershipAccessManager<Shed>>().HoldAsync(data.ToolShed, ToolPermissions.LendTools, Cancellation)))!
            .Via.Should().Be(MemberVia.Members);
        garden.Commands.Commands.Should().ContainSingle().Which.Should().Contain("\"ShedRoles\"").And.NotContain("\"PlotRoles\"", "a shed's permissions are read from the roles of sheds");
        var di = await garden.Services.AsAsync(data.Di, provider => provider.GetRequiredService<MembershipAccessManager<Shed>>().HoldAsync(data.ToolShed, ShedPermissions.Open, Cancellation));
        di.Should().NotBeNull("a hand sees the shed it is in");
        di!.Via.Should().BeNull();

        // A role of sheds may give any permission but the one the rules keep from it.
        await garden.Services.ChangeAsync(async context => (await context.ShedRoles.SingleAsync(role => role.Id == data.Lender.Id, Cancellation)).SetPermissions([ToolPermissions.LendTools, "tools.sharpen"], ShedMembership.Rules));
        await Refused.WithCodeAsync(ShedMembership.Codes, MembershipRefusals.PermissionNotForMembers, () => garden.Services.ChangeAsync(async context =>
            (await context.ShedRoles.SingleAsync(role => role.Id == data.Lender.Id, Cancellation)).SetPermissions([ShedPermissions.Open, ShedPermissions.Sell], ShedMembership.Rules)));

        // One set of roles for the whole application: a role is found in any garden and in none, with no rule of
        // the host's about whose roles a request reads, and the starter role is made once.
        (await garden.Services.AsAsync(CurrentUserSnapshot.System, provider => provider.GetRequiredService<IMemberRoles<ShedId, ShedRoleId>>().ExistsAsync(data.ShedKeeper.Id, Cancellation).AsTask())).Should().BeTrue();
        (await garden.Services.InAsync(data.Orchard, provider => provider.GetRequiredService<IMemberRoles<ShedId, ShedRoleId>>().ExistsAsync(data.ShedKeeper.Id, Cancellation).AsTask())).Should().BeTrue();
        (await garden.Services.AsAsync(CurrentUserSnapshot.System, provider => provider.GetRequiredService<ShedRoleManager>().CreateStarterRolesAsync(draft => new ShedRole(ShedRoleId.CreateSequential(), draft), Cancellation)))
            .Should().BeEmpty();
        (data.ShedKeeper.Name, data.ShedKeeper.MadeFrom).Should().Be((ShedMembership.Keeper, ShedMembership.Keeper));
        ShedMembership.Rules.PermissionsOf(new NamedRole(ShedMembership.Keeper)).Should().Equal([ShedPermissions.Open, ShedPermissions.Stock], "selling is no role's to give, and the starter role does not list it");

        // What a garden does to a role of its plots changes nothing for a shed, and what was done to the lender nothing for a plot.
        await garden.Services.ChangeAsync(data.MeadowTender.Id, role => role.PutAway());
        (await garden.Services.AsAsync(data.Ben, provider => provider.GetRequiredService<MembershipAccessManager<Shed>>().HoldAsync(data.ToolShed, ToolPermissions.LendTools, Cancellation)))!
            .Via.Should().Be(MemberVia.Members);
        (await PermissionsOnTheBeansAsync(garden, data.Eve)).Should().BeEquivalentTo([PlotPermissions.See, PlotPermissions.Fence]);
    }

    [Fact]
    public async Task Roles_kept_for_a_resource_go_with_members_the_host_resolves_and_with_reach_from_above()
    {
        // The same plots and the same roles, under rules that say the other two things the other way as well:
        // who a gardener is, is what the host resolves, and a permission held at a plot's garden reaches the plot.
        // Where the roles come from stands by itself, so they are still read from their rows.
        var rules = MembershipRules.Declare(
            typeof(Plot),
            "plots",
            PlotMembership.Rules.Permissions,
            Roles.Custom(starters: [new(PlotMembership.Tender, [PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Water]), new(PlotMembership.Waterer, [PlotPermissions.See, PlotPermissions.Water])]),
            members: MemberSource.Resolved(),
            reachFromAbove: true,
            codes: PlotMembership.Codes);

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var clock = new FixedClock();
        var commands = new CommandCounter();
        var data = new GardenScenario(clock.Now);
        var ranger = UserId.CreateSequential();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(new Wardens(letGo: data.Eve.Member));
        services.AddDbContext<GardensWithWardens>(options => options.UseSqlite(connection).AddInterceptors(commands));
        services.AddMembership<PlotGardener, PlotGardenerId, UserId, PlotRoleId, GardensWithWardens, Plot, PlotId, Wardens>(rules);
        await using var provider = services.BuildServiceProvider();

        using (TestUser.BeginSystem())
        {
            await using var seeding = provider.CreateAsyncScope();
            var context = seeding.ServiceProvider.GetRequiredService<GardensWithWardens>();
            await context.Database.EnsureCreatedAsync(Cancellation);
            context.AddRange(data.Roles);
            context.AddRange(data.Plots);

            // The ranger sees and waters every plot of the meadow from above, and Ben fences there.
            context.AddRange(
                new GardenPermission { UserId = ranger.Value, GardenId = data.Meadow, Permission = PlotPermissions.See },
                new GardenPermission { UserId = ranger.Value, GardenId = data.Meadow, Permission = PlotPermissions.Water },
                new GardenPermission { UserId = data.Ben.User, GardenId = data.Meadow, Permission = PlotPermissions.Fence });
            await context.SaveChangesAsync(Cancellation);
        }

        async Task<MemberHold<PlotId>?> HoldAsync(UserId user, PlotId plot, string permission)
        {
            using (TestUser.BeginAs(TestCallers.User(user)))
            {
                await using var scope = provider.CreateAsyncScope();
                commands.Reset();
                var held = await scope.ServiceProvider.GetRequiredService<MembershipAccessManager<Plot>>().HoldAsync(plot, permission, Cancellation);
                commands.Count.Should().BeLessThanOrEqualTo(1, "whatever the rules say, a question is one statement");
                return held;
            }
        }

        // Through a role of the garden's, read from its row, for as long as the role is held.
        var water = await HoldAsync(data.Cy.Member, data.Beans, PlotPermissions.Water);
        (water!.Via, water.Until).Should().Be((MemberVia.Members, data.InThreeDays));
        commands.Commands.Should().ContainSingle().Which.Should().Contain("FROM \"PlotRole\"").And.Contain("\"GardenPermission\"", "the roles' rows and what the host answers are in the one statement");

        // From above, by somebody on no plot: seen and watered, and no more held than is held there.
        var above = await HoldAsync(ranger, data.Leeks, PlotPermissions.Water);
        (above!.Via, above.Until).Should().Be((MemberVia.Above, null));
        (await HoldAsync(ranger, data.Beans, PlotPermissions.Plant))!.Via.Should().BeNull();
        (await HoldAsync(ranger, data.Kale, PlotPermissions.See)).Should().BeNull("the kale is in the orchard");

        // Both ways at once: Ben tends the beans through a kept role and fences them from the garden.
        (await HoldAsync(data.Ben.Member, data.Beans, PlotPermissions.Plant))!.Via.Should().Be(MemberVia.Members);
        (await HoldAsync(data.Ben.Member, data.Beans, PlotPermissions.Fence))!.Via.Should().Be(MemberVia.Above);

        // An archived role gives nothing here either, and somebody the host does not count is nobody's
        // gardener, whatever role her row holds.
        (await HoldAsync(data.Fay.Member, data.Beans, PlotPermissions.Plant))!.Via.Should().BeNull();
        (await HoldAsync(data.Eve.Member, data.Beans, PlotPermissions.Fence)).Should().BeNull();
    }

    /// <summary>The permissions somebody holds on the beans, of every permission a question about plots is asked with.</summary>
    private static async Task<IReadOnlySet<string>> PermissionsOnTheBeansAsync(SqliteGarden garden, GardenPerson person)
        => (await garden.Services.PlotsAsync(person, access => access.PermissionsOnAsync([garden.Scenario.Beans], GardenScenario.PermissionsAsked, Cancellation)))[garden.Scenario.Beans];

    /// <summary>A factory of the host's own for its context: all this host registers of it.</summary>
    private sealed class GardenContexts(DbContextOptions<GardenContext> options) : IDbContextFactory<GardenContext>
    {
        public GardenContext CreateDbContext() => new(options);
    }

    /// <summary>A permission somebody holds at a garden, and so on every plot in it: the host's own rows.</summary>
    private sealed class GardenPermission
    {
        public Guid UserId { get; set; }

        public GardenId GardenId { get; set; }

        public string Permission { get; set; } = string.Empty;
    }

    /// <summary>
    /// What a host answers for plots that stand beside something of its own: who the caller is as a gardener,
    /// which is nobody for somebody it let go, and at which gardens the caller holds a permission.
    /// </summary>
    private sealed class Wardens(UserId letGo) : ICallerMember<PlotId, UserId>, IPlacesReached<PlotId, GardenId>
    {
        public UserId? Find(CurrentUserSnapshot caller) => caller.Id is { } user && user != letGo.Value ? new UserId(user) : null;

        public IQueryable<GardenId> PlacesReached(DbContext context, CurrentUserSnapshot caller, string permission)
        {
            var user = caller.Id;
            var counted = user is not null && user != letGo.Value;
            return context.Set<GardenPermission>().Where(held => counted && held.UserId == user && held.Permission == permission).Select(held => held.GardenId);
        }
    }

    /// <summary>A context that maps the plots as sitting in their garden, their role class, and the permissions held at gardens.</summary>
    private sealed class GardensWithWardens(DbContextOptions<GardensWithWardens> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Plot>(plot =>
            {
                plot.Property(row => row.Id).ValueGeneratedNever();
                plot.HasMembers(row => row.Gardeners);
            });
            modelBuilder.Entity<PlotRole>().IsCustomRole();
            modelBuilder.Entity<GardenPermission>().HasKey(held => new { held.UserId, held.GardenId, held.Permission });
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }
}
