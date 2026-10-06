using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Provisioning makes everything a tenant needs in one save, with an administrator from the start, and only
/// system work outside any tenant does it. A tenant's life is system work in that tenant; its settings need
/// the settings key for the whole tenant.
/// </summary>
public class TenantCommandsTests
{
    private static readonly Guid Ada = Guid.NewGuid();

    private static HostTenancy.TenantToProvision Harbor(TenantShape shape = TenantShape.Hierarchical, string slug = "harbor")
        => new(slug, "Harbor Works", shape, "Harbor Works", Ada, "Ada");

    private static async Task<HostTenancy.ProvisionedTenant> Provision(Harness harness, HostTenancy.TenantToProvision command)
        => await harness.Run(HostCaller.System, h => h.Tenants.ProvisionAsync(command, default));

    [Fact]
    public async Task Provisioning_creates_tenant_organization_root_roles_first_admin_and_grant_in_one_save()
    {
        var harness = new Harness(New.Catalogue());

        var provisioned = await Provision(harness, Harbor());

        harness.Store.SaveCount.Should().Be(1);
        var tenant = harness.Store.Tenant(provisioned.Tenant);
        tenant.Slug.Value.Should().Be("harbor");
        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.Shape.Should().Be(TenantShape.Hierarchical);

        var organization = harness.Store.Organization(provisioned.Tenant);
        organization.Name.Should().Be("Harbor Works");
        organization.Units.Should().ContainSingle().Which.Should().Match<HostUnit>(root => root.Id == provisioned.RootUnit && root.Name == "Harbor Works" && root.IsRoot);

        var seat = harness.Store.Seat(provisioned.AdminSeat);
        seat.Identity.Should().Be(Ada);
        seat.TenantId.Should().Be(provisioned.Tenant);
        var placement = seat.Placements.Should().ContainSingle().Which;
        placement.UnitId.Should().Be(provisioned.RootUnit);
        placement.IsPrimary.Should().BeTrue();
        var grant = placement.Grants.Should().ContainSingle().Which;
        grant.RoleId.Should().Be(provisioned.AdministratorRole);
        grant.StartsAt.Should().Be(harness.Clock.Now);
        grant.EndsAt.Should().BeNull();
        grant.GrantedBy.Should().BeNull("system work with no seat acting granted it");

        harness.Store.RolesOf(provisioned.Tenant).Should().HaveCount(4);
        harness.Store.RevisionOf(provisioned.Tenant).Should().Be(0);
        harness.Store.SavedEvents.Select(saved => saved.GetType().GetGenericTypeDefinition()).Should().Contain(
        [
            typeof(TenantProvisioned<,>), typeof(TenantActivated<,>), typeof(OrganizationUnitAdded<,,>), typeof(RoleCreated<,,>),
            typeof(SeatAdded<,>), typeof(SeatPlaced<,,>), typeof(OrganizationRoleGranted<,,,>),
        ]);
    }

    [Fact]
    public async Task The_first_seat_is_an_administrator()
    {
        var harness = new Harness(New.Catalogue());
        var provisioned = await Provision(harness, Harbor());

        var caller = HostCaller.InSeat(provisioned.Tenant, provisioned.AdminSeat);
        var tenantWide = await harness.Run(caller, async h =>
        {
            var questions = new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(h.Catalogue, h.Clock).Over(h.Store, h.Store);
            return await Task.WhenAll(h.Catalogue.LiveKeys.Select(key => questions.HoldsTenantWideAsync(key, default)));
        });

        tenantWide.Should().OnlyContain(held => held, "the administrators' role holds every live key, granted at the root");
        harness.Store.SavedRights.Where(right => right.Key == TenancyKeys.AdministratorKey)
            .Should().ContainSingle().Which.Should().Match<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>(
                right => right.SeatId == provisioned.AdminSeat && right.UnitId == provisioned.RootUnit && right.EndsAt == null);

        await harness.Run(caller, h => h.Roles.CreateAsync("Clerk", "Files widgets", [HostCatalogue.WidgetCreate], default));
    }

    [Fact]
    public async Task Packs_for_the_shape_are_copied_with_their_provenance()
    {
        var harness = new Harness(New.Catalogue());

        var hierarchical = await Provision(harness, Harbor());
        var flat = await Provision(harness, Harbor(TenantShape.Flat, "kiosk"));

        hierarchical.RolesByPack.Keys.Should().BeEquivalentTo(
            [HostCatalogue.AdministratorPack, HostCatalogue.SupervisorPack, HostCatalogue.OperatorPack, HostCatalogue.WatcherPack]);
        flat.RolesByPack.Keys.Should().BeEquivalentTo(
            [HostCatalogue.AdministratorPack, HostCatalogue.OperatorPack, HostCatalogue.WatcherPack], "the supervisor's pack is for trees only");

        foreach (var (pack, id) in hierarchical.RolesByPack)
        {
            var role = harness.Store.Role(id);
            var source = harness.Catalogue.Packs.Single(candidate => candidate.Key == pack);
            role.FromPack.Should().Be(pack);
            role.Name.Should().Be(source.Name);
            role.Keys.Should().Equal(source.Keys);
            role.TenantId.Should().Be(hierarchical.Tenant);
        }

        harness.Store.Role(hierarchical.AdministratorRole).Keys.Should().Equal(harness.Catalogue.LiveKeys);
        hierarchical.AdministratorRole.Should().Be(hierarchical.RolesByPack[HostCatalogue.AdministratorPack]);
    }

    [Fact]
    public async Task A_catalogue_that_declares_no_packs_makes_the_first_seat_an_administrator_all_the_same()
    {
        var catalogue = TenancyCatalogue.Build(HostCatalogue.Application with { Packs = [] }, [new Permission("gauges.read", "Gauges", "Read gauges")]);
        var harness = new Harness(catalogue);

        var provisionedAs = new Dictionary<string, TenantId>(StringComparer.Ordinal);
        foreach (var (shape, slug) in new[] { (TenantShape.Flat, "kiosk"), (TenantShape.Hierarchical, "harbor") })
        {
            var provisioned = await Provision(harness, Harbor(shape, slug));
            provisionedAs[slug] = provisioned.Tenant;

            // The one role the tenant gets is the default administrators', and the first seat is given it at the root.
            provisioned.RolesByPack.Keys.Should().Equal([TenancyPacks.DefaultAdministratorsKey], "a {0} tenant gets the one pack there is", shape);
            provisioned.AdministratorRole.Should().Be(provisioned.RolesByPack[TenancyPacks.DefaultAdministratorsKey]);
            var administrators = harness.Store.Role(provisioned.AdministratorRole);
            administrators.Name.Should().Be("Administrator");
            administrators.Description.Should().Be(TenancyPacks.DefaultAdministrators.Description);
            administrators.FromPack.Should().Be(TenancyPacks.DefaultAdministratorsKey);
            administrators.Keys.Should().Equal(catalogue.LiveKeys).And.Contain([TenancyKeys.RolesManage, HostCatalogue.WidgetCreate, "gauges.read"]);
            var grant = harness.Store.Seat(provisioned.AdminSeat).Placements.Should().ContainSingle().Which.Grants.Should().ContainSingle().Which;
            grant.RoleId.Should().Be(provisioned.AdministratorRole);
            grant.EndsAt.Should().BeNull();

            // So the seat holds every live key for the whole tenant, and runs it: it makes a role, and gives it.
            var caller = HostCaller.InSeat(provisioned.Tenant, provisioned.AdminSeat);
            var held = await harness.Run(caller, async h =>
            {
                var questions = new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(h.Catalogue, h.Clock).Over(h.Store, h.Store);
                return await Task.WhenAll(h.Catalogue.LiveKeys.Select(key => questions.HoldsTenantWideAsync(key, default)));
            });
            held.Should().OnlyContain(holds => holds, "the default administrators' role holds every live key, granted at the root");

            var clerk = await harness.Run(caller, h => h.Roles.CreateAsync("Clerk", "Files widgets", [HostCatalogue.WidgetCreate], default));
            await harness.Run(caller, h => h.Seats.GrantAsync(provisioned.AdminSeat, provisioned.RootUnit, clerk, until: null, reason: null, default));
        }

        // A flat tenant that turns hierarchical has the one pack there is already: no role is added.
        var kiosk = provisionedAs["kiosk"];
        var roles = harness.Store.RolesOf(kiosk).Count;
        await harness.Run(HostCaller.InSeat(kiosk, harness.Store.SeatsIn(kiosk).Single().Id),
            h => h.Tenants.ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, default));
        harness.Store.Tenant(kiosk).Shape.Should().Be(TenantShape.Hierarchical);
        harness.Store.RolesOf(kiosk).Should().HaveCount(roles);
    }

    [Fact]
    public async Task Provisioning_a_listing_pack_grants_its_keys_only()
    {
        var catalogue = New.ListingCatalogue();
        var harness = new Harness(catalogue);
        string[] listed =
        [
            TenancyKeys.GrantsManage, TenancyKeys.HistoryView, TenancyKeys.RolesManage, TenancyKeys.SeatsManage, TenancyKeys.SettingsManage, TenancyKeys.UnitsManage,
            New.WidgetAssign,
        ];
        string[] widgets = [HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead];

        var provisioned = await Provision(harness, Harbor());

        // The administrators' role is a copy of its pack, like every other: what the pack lists, and no live key besides.
        var administrators = harness.Store.Role(provisioned.AdministratorRole);
        administrators.FromPack.Should().Be(HostCatalogue.AdministratorPack);
        administrators.Keys.Should().Equal(listed).And.Equal(catalogue.AdministratorPackFor(TenantShape.Hierarchical).Keys);
        catalogue.LiveKeys.Should().Contain(widgets, "the keys to work with widgets are live, and the pack leaves them out");
        harness.Store.Role(provisioned.RolesByPack[New.KeeperPack]).Keys.Should().Equal(New.WidgetAssign, HostCatalogue.WidgetRead);

        // So the first administrator's rights are those keys, at the root, and that is what the seat is answered.
        harness.Store.SavedRights.Where(right => right.SeatId == provisioned.AdminSeat)
            .Should().OnlyContain(right => right.UnitId == provisioned.RootUnit && right.EndsAt == null)
            .And.Subject.Select(right => right.Key).Should().BeEquivalentTo(listed);

        var caller = HostCaller.InSeat(provisioned.Tenant, provisioned.AdminSeat);
        var held = await harness.Run(caller, async h =>
        {
            var questions = new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(h.Catalogue, h.Clock).Over(h.Store, h.Store);
            var answers = await Task.WhenAll(h.Catalogue.LiveKeys.Select(key => questions.HoldsTenantWideAsync(key, default)));
            return h.Catalogue.LiveKeys.Where((_, index) => answers[index]).ToArray();
        });
        held.Should().Equal(listed).And.NotContain(widgets);

        // It is an administrator all the same: it makes a role, with a key it does not hold itself.
        await harness.Run(caller, h => h.Roles.CreateAsync("Clerk", "Files widgets", [HostCatalogue.WidgetCreate], default));
    }

    [Fact]
    public async Task Provisioning_uses_given_role_ids_per_pack()
    {
        var harness = new Harness(New.Catalogue());
        var tenant = new TenantId(42);
        var root = OrganizationUnitId.CreateSequential();
        var seat = SeatId.CreateSequential();
        var administrators = RoleId.CreateSequential();
        var watchers = RoleId.CreateSequential();

        var provisioned = await Provision(harness, Harbor() with
        {
            TenantId = tenant,
            RootId = root,
            AdminSeatId = seat,
            RoleIds = new Dictionary<string, RoleId> { [HostCatalogue.AdministratorPack] = administrators, [HostCatalogue.WatcherPack] = watchers },
        });

        provisioned.Tenant.Should().Be(tenant);
        provisioned.RootUnit.Should().Be(root);
        provisioned.AdminSeat.Should().Be(seat);
        provisioned.AdministratorRole.Should().Be(administrators);
        provisioned.RolesByPack[HostCatalogue.WatcherPack].Should().Be(watchers);
        harness.Store.Role(watchers).FromPack.Should().Be(HostCatalogue.WatcherPack);

        await FluentActions.Awaiting(() => Provision(harness, Harbor(TenantShape.Flat, "kiosk") with
            {
                RoleIds = new Dictionary<string, RoleId> { [HostCatalogue.SupervisorPack] = RoleId.CreateSequential() },
            }))
            .Should().ThrowAsync<ArgumentException>("a flat tenant gets no supervisor's role, so an id for one is a mistake")
            .WithMessage("*supervisor*");
        harness.Store.SaveCount.Should().Be(1, "the refused provisioning saved nothing");
    }

    [Fact]
    public async Task A_taken_slug_is_refused()
    {
        var harness = new Harness(New.Catalogue());
        await Provision(harness, Harbor());

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.SlugTaken, () => Provision(harness, Harbor(slug: " HARBOR ")));

        refusal.Arguments["Slug"].Should().Be("harbor");
        harness.Store.SaveCount.Should().Be(1);
        await FluentActions.Awaiting(() => Provision(harness, Harbor(slug: "-harbor"))).Should().ThrowAsync<InvalidValueObjectException>();
    }

    [Fact]
    public async Task Only_global_system_provisions()
    {
        var harness = Harness.OfHarbor();

        // The toolkit's code for "only the application itself", which a request that requires system work is refused with too.
        await Refused.WithCodeAsync(ToolkitRefusals.SystemOnly, () => harness.As(harness.Administrator, h => h.Tenants.ProvisionAsync(Harbor(slug: "wharf"), default)));
        await FluentActions.Awaiting(() => harness.BySystemWork(h => h.Tenants.ProvisionAsync(Harbor(slug: "wharf"), default)))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*BeginSystem()*");
        await Refused.WithCodeAsync(TenancyRefusals.TenantRequired,
            () => harness.Run(HostCaller.Nobody(TenancyRefusals.TenantRequired), h => h.Tenants.ProvisionAsync(Harbor(slug: "wharf"), default)));

        harness.Store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Suspend_reactivate_close_need_SystemInTenant()
    {
        var harness = Harness.OfHarbor();

        await Refused.WithCodeAsync(ToolkitRefusals.SystemOnly, () => harness.As(harness.Administrator, h => h.Tenants.SuspendAsync("unpaid", default)));
        await Refused.WithCodeAsync(ToolkitRefusals.SystemOnly, () => harness.As(harness.Administrator, h => h.Tenants.ReactivateAsync(default)));
        await Refused.WithCodeAsync(ToolkitRefusals.SystemOnly, () => harness.As(harness.Administrator, h => h.Tenants.CloseAsync("no longer needed", default)));

        foreach (var act in new Func<Harness, Task>[]
                 {
                     h => h.Tenants.SuspendAsync("unpaid", default),
                     h => h.Tenants.ReactivateAsync(default),
                     h => h.Tenants.CloseAsync("no longer needed", default),
                 })
        {
            await FluentActions.Awaiting(() => harness.Run(HostCaller.System, act))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*BeginSystemIn*");
        }

        harness.Store.Tenant(harness.Tenant).Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public async Task Suspend_reactivate_close_act_on_the_callers_own_tenant()
    {
        var harness = Harness.OfHarbor();
        var other = harness.Seed(2, "quarry");

        await harness.BySystemWork(h => h.Tenants.SuspendAsync("  unpaid invoice ", default));
        harness.Store.Tenant(harness.Tenant).Status.Should().Be(TenantStatus.Suspended);
        harness.Store.Tenant(harness.Tenant).StatusReason.Should().Be("unpaid invoice");

        await harness.BySystemWork(h => h.Tenants.ReactivateAsync(default));
        harness.Store.Tenant(harness.Tenant).Status.Should().Be(TenantStatus.Active);

        await Refused.WithCodeAsync(TenancyRefusals.ReasonRequired, () => harness.BySystemWork(h => h.Tenants.CloseAsync(" ", default)));
        await harness.BySystemWork(h => h.Tenants.CloseAsync("no longer needed", default));
        harness.Store.Tenant(harness.Tenant).Status.Should().Be(TenantStatus.Closed);
        await Refused.WithCodeAsync(TenancyRefusals.TenantState, () => harness.BySystemWork(h => h.Tenants.ReactivateAsync(default)));

        harness.Store.Tenant(other.Tenant.Id).Status.Should().Be(TenantStatus.Active, "system work in Harbor reaches no other tenant");
    }

    [Fact]
    public async Task Renaming_the_organization_needs_settings_manage_tenant_wide()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(supervisor, h => h.Tenants.RenameOrganizationAsync("Bert's Harbor", default)));
        refusal.Arguments["Key"].Should().Be(TenancyKeys.SettingsManage);
        refusal.Message.Should().Be("You lack the permission " + TenancyKeys.SettingsManage + ".");

        await harness.As(harness.Administrator, h => h.Tenants.RenameOrganizationAsync("Harbor Works Group", default));
        harness.Store.Organization(harness.Tenant).Name.Should().Be("Harbor Works Group");

        await harness.BySystemWork(h => h.Tenants.RenameOrganizationAsync("Harbor", default));
        harness.Store.Organization(harness.Tenant).Name.Should().Be("Harbor");
    }

    [Fact]
    public async Task Changing_shape_seeds_the_missing_packs_with_given_ids_and_touches_no_grants()
    {
        var harness = new Harness(New.Catalogue());
        var provisioned = await Provision(harness, Harbor(TenantShape.Flat, "kiosk"));
        var administrator = HostCaller.InSeat(provisioned.Tenant, provisioned.AdminSeat);
        var grantsBefore = harness.Store.Seat(provisioned.AdminSeat).Placements.SelectMany(placement => placement.Grants).ToArray();
        var supervisors = RoleId.CreateSequential();

        await Refused.WithCodeAsync(TenancyRefusals.FlatTenant, () => harness.Run(administrator,
            h => h.Organization.AddUnitAsync(provisioned.RootUnit, "North", default)));

        await harness.Run(administrator, h => h.Tenants.ChangeShapeAsync(
            TenantShape.Hierarchical, new Dictionary<string, RoleId> { [HostCatalogue.SupervisorPack] = supervisors }, language: null, default));

        harness.Store.Tenant(provisioned.Tenant).Shape.Should().Be(TenantShape.Hierarchical);
        var roles = harness.Store.RolesOf(provisioned.Tenant);
        roles.Should().HaveCount(4);
        roles.Should().ContainSingle(role => role.FromPack == HostCatalogue.SupervisorPack).Which.Id.Should().Be(supervisors);
        roles.Select(role => role.FromPack).Should().OnlyHaveUniqueItems("a pack the tenant has a copy of is not copied again");
        harness.Store.Seat(provisioned.AdminSeat).Placements.SelectMany(placement => placement.Grants)
            .Should().BeEquivalentTo(grantsBefore);

        await harness.Run(administrator, h => h.Organization.AddUnitAsync(provisioned.RootUnit, "North", default));
        await Refused.WithCodeAsync(TenancyRefusals.ShapeChange, () => harness.Run(administrator, h => h.Tenants.ChangeShapeAsync(TenantShape.Flat, roleIds: null, language: null, default)));
    }

    [Fact]
    public async Task Changing_shape_copies_a_listing_administrators_pack_with_its_keys_and_gives_it_to_nobody()
    {
        // A flat tenant's administrators hold every live key; a hierarchical tenant's list theirs, Tenancy's own.
        string[] listed = [.. TenancyKeys.Permissions.Select(permission => permission.Key).Order(StringComparer.Ordinal)];
        var catalogue = TenancyCatalogue.Build(
            HostCatalogue.Application with
            {
                Packs =
                [
                    .. HostCatalogue.Application.Packs.Select(pack => pack.Administers ? pack with { Shape = TenantShape.Flat } : pack),
                    new RolePack("tree-admin", "Tree administrator", "Runs access across the tree", listed, Shape: TenantShape.Hierarchical, Administers: true, Order: 15),
                ],
            },
            []);
        var harness = new Harness(catalogue);
        var provisioned = await Provision(harness, Harbor(TenantShape.Flat, "kiosk"));
        var administrator = HostCaller.InSeat(provisioned.Tenant, provisioned.AdminSeat);
        provisioned.RolesByPack.Keys.Should().NotContain("tree-admin", "that pack is for a hierarchical tenant");

        await harness.Run(administrator, h => h.Tenants.ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, default));

        // The tenant gets the role of the administrators' pack of its new shape, with the keys that pack lists.
        var roles = harness.Store.RolesOf(provisioned.Tenant);
        var treeAdministrators = roles.Should().ContainSingle(role => role.FromPack == "tree-admin").Which;
        treeAdministrators.Keys.Should().Equal(listed).And.Equal(catalogue.AdministratorPackFor(TenantShape.Hierarchical).Keys);

        // No grant changes. The first administrator keeps the role it was given, with every live key, and the new
        // role is nobody's until someone gives it.
        harness.Store.Seat(provisioned.AdminSeat).Placements.Single().Grants.Should().ContainSingle().Which.RoleId.Should().Be(provisioned.AdministratorRole);
        harness.Store.Role(provisioned.AdministratorRole).Keys.Should().Equal(catalogue.LiveKeys);
        harness.Store.SavedRights.Should().NotContain(right => right.RoleId == treeAdministrators.Id);
        harness.Store.SavedRights.Where(right => right.SeatId == provisioned.AdminSeat).Select(right => right.Key).Should().BeEquivalentTo(catalogue.LiveKeys);
    }

    [Fact]
    public async Task One_role_id_for_two_packs_is_refused()
    {
        var harness = new Harness(New.Catalogue());
        var shared = RoleId.CreateSequential();

        await FluentActions.Awaiting(() => Provision(harness, Harbor() with
            {
                RoleIds = new Dictionary<string, RoleId> { [HostCatalogue.OperatorPack] = shared, [HostCatalogue.WatcherPack] = shared },
            }))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*more than once*operator, watcher*");
        harness.Store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task A_refused_shape_change_leaves_the_tenant_as_it_was()
    {
        var harness = new Harness(New.Catalogue());
        var provisioned = await Provision(harness, Harbor(TenantShape.Flat, "kiosk"));
        var administrator = HostCaller.InSeat(provisioned.Tenant, provisioned.AdminSeat);
        await harness.Run(administrator, h => h.Roles.CreateAsync("supervisor", "Made by hand", [HostCatalogue.WidgetRead], default));

        await harness.Run(administrator, async h =>
        {
            await Refused.WithCodeAsync(TenancyRefusals.RoleNameTaken, () => h.Tenants.ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, default));
            await h.Tenants.RenameOrganizationAsync("Kiosk Group", default);
        });

        harness.Store.Organization(provisioned.Tenant).Name.Should().Be("Kiosk Group", "the later command was saved");
        harness.Store.Tenant(provisioned.Tenant).Shape.Should().Be(TenantShape.Flat, "the refused change left nothing behind to save");
        harness.Store.RolesOf(provisioned.Tenant).Should().HaveCount(4);
    }
}
