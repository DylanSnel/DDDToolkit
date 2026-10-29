namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The catalogue is checked once, as a whole, and every problem is reported together, so a catalogue that
/// does not hold together stops start-up rather than a provisioning half way.
/// </summary>
public class CatalogueTests
{
    private static readonly StarterRole Administrators = new("host-admin", "Administrator", "Runs the tenant", [], Administers: true);

    /// <summary>Tenancy's own permissions, as an administrators' starter role that lists its permissions writes them.</summary>
    private static readonly string[] TenancysOwn = [.. TenancyPermissions.All.Select(permission => permission.Name)];

    /// <summary>The problem of an administrators' starter role that lists permissions and leaves <paramref name="permission"/> out.</summary>
    private static string LeftOut(string starterRole, string permission, string because)
        => "The administrators' starter role '" + starterRole + "' lists permissions but not '" + permission + "', which " + because
           + ": an administrator holds every permission that manages access, and Tenancy's own.";

    private static ApplicationCatalogue Application(
        IReadOnlyList<StarterRole>? starterRoles = null,
        IReadOnlyList<Permission>? permissions = null)
        => new(starterRoles ?? [Administrators], permissions ?? HostCatalogue.Permissions);

    private static IReadOnlyList<string> Problems(ApplicationCatalogue application, params Permission[] contributed)
        => FluentActions.Invoking(() => TenancyCatalogue.Build(application, contributed))
            .Should().Throw<TenancyCatalogueException>().Which.Problems;

    [Fact]
    public void Tenancy_permissions_are_always_present()
    {
        var catalogue = TenancyCatalogue.Build(Application(permissions: []), []);

        catalogue.Permissions.Select(permission => permission.Name).Should().Equal(
            TenancyPermissions.SettingsManage, TenancyPermissions.UnitsManage, TenancyPermissions.SeatsManage, TenancyPermissions.GrantsManage, TenancyPermissions.RolesManage,
            TenancyPermissions.HistoryView);
        catalogue.Permissions.Should().OnlyContain(permission => permission.Module == "Tenancy");
        catalogue.LivePermissions.Should().BeEquivalentTo(TenancyPermissions.All.Select(permission => permission.Name));
        TenancyPermissions.AdministratorPermission.Should().Be(TenancyPermissions.RolesManage);
    }

    [Fact]
    public void Permissions_that_manage_access_stay_contained_unless_the_application_turns_it_off()
    {
        TenancyCatalogue.Build([]).ContainAccessManagingPermissions.Should().BeTrue("an application without a catalogue of its own gets the default");
        TenancyCatalogue.Build(Application(), []).ContainAccessManagingPermissions.Should().BeTrue();

        var off = TenancyCatalogue.Build(Application() with { ContainAccessManagingPermissions = false, AccessManagingPermissions = [HostCatalogue.WidgetCreate] }, []);

        off.ContainAccessManagingPermissions.Should().BeFalse();
        off.AccessManagingPermissions.Should().Contain([TenancyPermissions.GrantsManage, HostCatalogue.WidgetCreate], "turning containment off marks no permission differently");
        off.ManagesAccess(HostCatalogue.WidgetCreate).Should().BeTrue();
    }

    [Fact]
    public void An_application_permission_under_tenancy_is_refused()
    {
        Problems(Application(permissions: [new Permission("tenancy.extras.manage", "Extras", "Extra things")]))
            .Should().ContainSingle().Which.Should().Contain("'tenancy.extras.manage'").And.Contain("belong to the Tenancy package");

        Problems(Application(), new Permission(TenancyPermissions.SettingsManage, "Widgets", "Duplicate"))
            .Should().ContainSingle().Which.Should().Contain("A contribution");
    }

    [Fact]
    public void A_duplicate_key_is_refused()
    {
        Problems(Application(), new Permission(HostCatalogue.WidgetRead, "Gadgets", "See gadgets"))
            .Should().ContainSingle().Which.Should().Be("'widget.read' is declared more than once.");

        Problems(Application(permissions: [new Permission("Widget.Read", "Widgets", "Shouting")]))
            .Should().ContainSingle().Which.Should().Contain("not lowercase words");
        Problems(Application(permissions: [new Permission("widgets", "Widgets", "No dot")]))
            .Should().ContainSingle();
        Problems(Application(permissions: [new Permission("widget.paint", " ", "Missing module")]))
            .Should().ContainSingle().Which.Should().Contain("names no module");
    }

    [Fact]
    public void One_declaration_added_twice_is_refused_once_for_its_whole_list()
    {
        // The same list contributed twice, as a module's own registration beside the host's generated one adds it.
        Permission[] gauges = [new("gauges.read", "Gauges", "Read gauges"), new("gauges.lock", "Gauges", "Lock gauges")];

        Problems(Application(), [.. gauges, .. gauges]).Should().ContainSingle().Which.Should().Be(
            "The same declaration of 'gauges.read', 'gauges.lock' is added more than once. A module's [Permissions] classes are added by the host's "
            + "AddTenancyPermissionsOfModules(), and with AddTenancyPermissions no more; and either is called once.");

        // Two declarations of one permission that are not one and the same are refused as a duplicate, as before.
        Problems(Application(), [.. gauges, new Permission("gauges.read", "Gauges", "Read gauges")])
            .Should().ContainSingle().Which.Should().Be("'gauges.read' is declared more than once.");
    }

    [Fact]
    public void A_starter_role_naming_an_unknown_or_retired_permission_is_refused()
    {
        var problems = Problems(Application(
            starterRoles: [Administrators, new StarterRole("painter", "Painter", "Paints", ["widget.paint", "widget.old"])],
            permissions: [.. HostCatalogue.Permissions, new Permission("widget.old", "Widgets", "Old", Retired: true)]));

        problems.Should().BeEquivalentTo(
            "The starter role 'painter' lists 'widget.old', which is retired.",
            "The starter role 'painter' lists 'widget.paint', which is unknown to the catalogue.");
    }

    [Fact]
    public void A_permission_unknown_to_the_catalogue_is_told_with_how_a_modules_permissions_reach_it()
    {
        // What a host that leaves out services.AddTenancyPermissionsOfModules() meets: a starter role names a module's permission
        // that never reached the catalogue, or, with no starter role naming one, the first question about it.
        const string Cure = "*[Permissions]*services.AddTenancyPermissionsOfModules()*";
        var refused = FluentActions.Invoking(() => TenancyCatalogue.Build(Application(starterRoles: [Administrators, new StarterRole("viewer", "Viewer", "Looks", ["orders.view"])]), []))
            .Should().Throw<TenancyCatalogueException>().Which;

        refused.Problems.Should().ContainSingle().Which.Should().Be(
            "The starter role 'viewer' lists 'orders.view', which is unknown to the catalogue.", "the problems stay what is wrong, one sentence each");
        refused.Message.Should().Match(Cure, "the cure is said once, after the problems")
            .And.EndWith("in a project that references the module.");

        FluentActions.Invoking(() => TenancyCatalogue.Build([]).RequireAskable("orders.view"))
            .Should().Throw<ArgumentException>().WithMessage("'orders.view' is not a permission of the permission catalogue. " + Cure);

        // A catalogue that names no unknown permission hears nothing about modules.
        FluentActions.Invoking(() => TenancyCatalogue.Build(Application(starterRoles: [Administrators, Administrators]), []))
            .Should().Throw<TenancyCatalogueException>().Which.Message.Should().NotContain("[Permissions]");
    }

    [Fact]
    public void A_duplicate_starter_role_is_refused()
    {
        var watcher = new StarterRole("watcher", "Watcher", "Looks", [HostCatalogue.WidgetRead]);

        Problems(Application(starterRoles: [Administrators, watcher, watcher with { Name = "Other watcher" }]))
            .Should().ContainSingle().Which.Should().Be("The starter role 'watcher' is declared more than once.");
    }

    [Fact]
    public void Each_shape_has_exactly_one_administrators_starter_role()
    {
        // The cure keeps one for the shape, however many there are and whichever shapes they are seeded for.
        static string MoreThanOne(string shape, string starterRoles)
            => "A " + shape + " tenant needs exactly one administrators' starter role seeded for it, and has " + starterRoles + ". Keep one of them for it: seed the "
               + "others for another shape with SeededFor, or declare them without Administers.";

        Problems(Application(starterRoles: [Administrators, Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat }]))
            .Should().ContainSingle().Which.Should().Be(MoreThanOne("flat", "2: host-admin, flat-admin"));

        // Two seeded for the same shape already.
        Problems(Application(starterRoles:
            [
                Administrators with { Key = "owner", Name = "Owner", SeededFor = TenantShape.Flat },
                Administrators with { Key = "manager", Name = "Manager", SeededFor = TenantShape.Flat },
                Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical },
            ]))
            .Should().ContainSingle().Which.Should().Be(MoreThanOne("flat", "2: owner, manager"));

        // Three for two shapes: no SeededFor gives each shape one of them, so one goes without Administers.
        Problems(Application(starterRoles:
            [
                Administrators,
                Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat },
                Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical },
            ]))
            .Should().Equal(MoreThanOne("flat", "2: host-admin, flat-admin"), MoreThanOne("hierarchical", "2: host-admin, tree-admin"));

        var perShape = TenancyCatalogue.Build(Application(starterRoles:
        [
            // Named apart: a flat tenant that turns hierarchical is given the role of the other starter role next to its own.
            Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat },
            Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical },
        ]), []);
        perShape.AdministratorStarterRoleFor(TenantShape.Flat).Key.Should().Be("flat-admin");
        perShape.AdministratorStarterRoleFor(TenantShape.Hierarchical).Key.Should().Be("tree-admin");
        perShape.HasDefaultAdministrators.Should().BeFalse();
    }

    [Fact]
    public void An_application_that_declares_no_starter_roles_gets_the_default_administrators_starter_role_for_every_shape()
    {
        var catalogue = TenancyCatalogue.Build(Application(starterRoles: []), []);

        var administrators = catalogue.StarterRoles.Should().ContainSingle().Which;
        administrators.Should().BeEquivalentTo(TenancyStarterRoles.DefaultAdministrators with { Permissions = catalogue.LivePermissions }, options => options.ComparingByMembers<StarterRole>());
        administrators.Key.Should().Be("administrator");
        administrators.Name.Should().Be("Administrator");
        administrators.Description.Should().NotBeNullOrWhiteSpace("the screens that assign roles show it");
        administrators.Should().Match<StarterRole>(starterRole => starterRole.Administers && starterRole.SeedOnProvision && starterRole.SeededFor == null);
        administrators.Permissions.Should().Equal(catalogue.LivePermissions, "it lists nothing, and so holds every live permission, Tenancy's and the application's");
        catalogue.HasDefaultAdministrators.Should().BeTrue();

        foreach (var shape in Enum.GetValues<TenantShape>())
        {
            catalogue.AdministratorStarterRoleFor(shape).Should().BeSameAs(administrators);
            catalogue.StarterRolesFor(shape).Should().Equal([administrators], "a new tenant of every shape gets a copy");
        }

        // The application leaves the starter roles out altogether, and names its permissions.
        var withoutStarterRoles = TenancyCatalogue.Build(new ApplicationCatalogue(Permissions: HostCatalogue.Permissions), []);
        withoutStarterRoles.StarterRoles.Should().BeEquivalentTo(catalogue.StarterRoles, options => options.ComparingByMembers<StarterRole>());
        withoutStarterRoles.LivePermissions.Should().Equal(catalogue.LivePermissions).And.Contain(HostCatalogue.WidgetCreate);

        var marked = new ApplicationCatalogue(Permissions: HostCatalogue.Permissions, AccessManagingPermissions: [HostCatalogue.WidgetCreate]);
        marked.StarterRoles.Should().BeEmpty();
        marked.Permissions.Should().BeSameAs(HostCatalogue.Permissions);
        TenancyCatalogue.Build(marked, []).AccessManagingPermissions.Should().Contain(HostCatalogue.WidgetCreate, "the permissions it marks reach the catalogue without a starter role of its own");

        New.Catalogue().HasDefaultAdministrators.Should().BeFalse("the host declares an administrators' starter role of its own");
        New.Catalogue().StarterRoles.Should().NotContain(starterRole => starterRole.Key == TenancyStarterRoles.DefaultAdministratorsKey);
    }

    [Fact]
    public void The_default_administrators_starter_role_comes_first_next_to_the_starter_roles_the_application_declares()
    {
        var watcher = new StarterRole("watcher", "Watcher", "Looks", [HostCatalogue.WidgetChange], Order: 10);
        var supervisor = new StarterRole("supervisor", "Supervisor", "Runs a part", [TenancyPermissions.UnitsManage], SeededFor: TenantShape.Hierarchical, Order: 20);

        var catalogue = TenancyCatalogue.Build(Application(starterRoles: [watcher, supervisor]), []);

        catalogue.HasDefaultAdministrators.Should().BeTrue("no starter role the application declares administers");
        catalogue.StarterRoles.Select(starterRole => starterRole.Key).Should().Equal(TenancyStarterRoles.DefaultAdministratorsKey, "watcher", "supervisor");
        catalogue.StarterRolesFor(TenantShape.Hierarchical).Select(starterRole => starterRole.Key).Should().Equal(TenancyStarterRoles.DefaultAdministratorsKey, "watcher", "supervisor");
        catalogue.StarterRolesFor(TenantShape.Flat).Select(starterRole => starterRole.Key).Should().Equal(TenancyStarterRoles.DefaultAdministratorsKey, "watcher");
        catalogue.StarterRoles.Single(starterRole => starterRole.Key == "watcher").Permissions.Should().Equal([HostCatalogue.WidgetChange, HostCatalogue.WidgetRead], "the application's starter roles are built as they were");
        catalogue.AdministratorStarterRoleFor(TenantShape.Flat).Permissions.Should().Equal(catalogue.LivePermissions);
    }

    [Fact]
    public void The_default_administrators_starter_role_holds_a_permission_declared_later_as_a_starter_role_that_lists_none_does()
    {
        var application = Application(starterRoles: [], permissions: [.. HostCatalogue.Permissions, new Permission("widget.old", "Widgets", "Old", Retired: true)]);

        var before = TenancyCatalogue.Build(application, []);
        var later = TenancyCatalogue.Build(application, [new Permission("gauges.read", "Gauges", "Read gauges"), new Permission("gauges.lock", "Gauges", "Lock gauges", ManagesAccess: true)]);

        before.AdministratorStarterRoleFor(TenantShape.Flat).Permissions.Should().Equal(before.LivePermissions).And.NotContain("widget.old", "a retired permission is not live");
        later.AdministratorStarterRoleFor(TenantShape.Hierarchical).Permissions.Should().Equal(later.LivePermissions)
            .And.Contain(["gauges.read", "gauges.lock"], "a module's permission comes with it, one that manages access included, as the application never lists it")
            .And.Contain(before.LivePermissions);
    }

    [Fact]
    public void Administrators_starter_roles_seeded_for_some_shapes_and_not_others_are_refused()
    {
        // The problem names the property to write, with the shape the catalogue lacks a starter role for.
        static string NoneFor(string shape, string property)
            => "A " + shape + " tenant needs exactly one administrators' starter role seeded for it, and has none. The default one, 'administrator', is added only "
               + "when the application declares no administrators' starter role at all: declare one with SeededFor: " + property + " as well, or declare none.";

        Problems(Application(starterRoles: [Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat }]))
            .Should().ContainSingle().Which.Should().Be(NoneFor("hierarchical", "TenantShape.Hierarchical"));

        // A starter role that administers counts as declared even when something else about it is refused.
        Problems(Application(starterRoles: [Administrators with { Key = " " }]))
            .Should().BeEquivalentTo("A starter role has no key.", NoneFor("flat", "TenantShape.Flat"), NoneFor("hierarchical", "TenantShape.Hierarchical"));
    }

    [Fact]
    public void An_administrators_starter_role_seeded_for_a_shape_but_not_on_provision_is_named_as_the_one_it_lacks()
    {
        // Not seeded, it gives a new tenant no administrator, and the default is not added in its place. Its
        // SeededFor is right already, so the problem names SeedOnProvision, not another starter role to declare.
        static string Unseeded(string shape, string starterRoles, bool several)
            => "A " + shape + " tenant needs exactly one administrators' starter role seeded for it, and has none: " + starterRoles
               + (several ? " would be, but are" : " would be, but is") + " declared with SeedOnProvision: false, and the default one, 'administrator', "
               + (several ? "is not added in their place. Leave SeedOnProvision: false off one of them." : "is not added in its place. Leave SeedOnProvision: false off it.");

        Problems(Application(starterRoles: [Administrators with { SeedOnProvision = false }]))
            .Should().Equal(Unseeded("flat", "'host-admin'", several: false), Unseeded("hierarchical", "'host-admin'", several: false));

        Problems(Application(starterRoles:
            [
                Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat },
                Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical, SeedOnProvision = false },
            ]))
            .Should().ContainSingle().Which.Should().Be(Unseeded("hierarchical", "'tree-admin'", several: false));

        // Two that a hierarchical tenant would get: seeding either is enough for it.
        Problems(Application(starterRoles:
            [
                Administrators with { SeedOnProvision = false },
                Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical, SeedOnProvision = false },
            ]))
            .Should().Equal(Unseeded("flat", "'host-admin'", several: false), Unseeded("hierarchical", "'host-admin', 'tree-admin'", several: true));
    }

    [Fact]
    public void A_starter_role_that_takes_the_default_administrators_permission_or_name_is_refused()
    {
        const string Added = "the default administrators' starter role, added because the application declares no administrators' starter role";
        const string Fix = ", or declare it with Administers: true to make it the administrators' starter role instead.";

        Problems(Application(starterRoles: [new StarterRole("administrator", "Keeper", "Keeps widgets", [HostCatalogue.WidgetRead])]))
            .Should().ContainSingle().Which.Should().Be("The starter role 'administrator' has the permission of " + Added + ". Give the starter role another permission" + Fix);

        Problems(Application(starterRoles: [new StarterRole("chief", " ADMINISTRATOR ", "Runs widgets", [HostCatalogue.WidgetChange])]))
            .Should().ContainSingle().Which.Should().Be(
                "The starter role 'chief' is named 'ADMINISTRATOR', and " + Added + ", is named 'Administrator'; a tenant's roles have names of their own, "
                + "ignoring case. Give the starter role another name" + Fix);

        Problems(Application(starterRoles: [new StarterRole("administrator", "Administrator", "Runs widgets", [HostCatalogue.WidgetChange])]))
            .Should().HaveCount(2, "the permission and the name are each taken");
    }

    [Fact]
    public void A_starter_role_named_like_the_default_administrators_starter_role_in_dutch_is_refused_as_well()
    {
        // A tenant provisioned in Dutch gets the default's role as Beheerder, from the package's own texts, and a starter role
        // the application named Beheerder would then be refused half way through provisioning, by a name the
        // application never wrote.
        Problems(Application(starterRoles: [new StarterRole("keeper", " beheerder ", "Keeps widgets", [HostCatalogue.WidgetRead])]))
            .Should().ContainSingle().Which.Should().Be(
                "The starter role 'keeper' is named 'beheerder', and the default administrators' starter role, added because the application declares no "
                + "administrators' starter role, is named 'Beheerder' in Dutch; a tenant's roles have names of their own, ignoring case. Give the starter role "
                + "another name, or declare it with Administers: true to make it the administrators' starter role instead.");

        // An administrators' starter role of the application's own may be called anything: no default is added next to it.
        TenancyCatalogue.Build(Application(starterRoles: [Administrators with { Name = "Beheerder" }]), []).HasDefaultAdministrators.Should().BeFalse();

        // Declared as the administrators' starter role, it is the application's own, and nothing is added.
        var own = TenancyCatalogue.Build(Application(starterRoles: [new StarterRole("administrator", "Administrator", "Runs widgets", [], Administers: true)]), []);
        own.HasDefaultAdministrators.Should().BeFalse();
        own.StarterRoles.Should().ContainSingle().Which.Description.Should().Be("Runs widgets");
    }

    [Fact]
    public void Starter_roles_for_a_shape_are_the_seeded_ones_of_that_shape_in_order()
    {
        var catalogue = New.Catalogue();

        catalogue.StarterRolesFor(TenantShape.Hierarchical).Select(starterRole => starterRole.Key).Should().Equal("host-admin", "supervisor", "operator", "watcher");
        catalogue.StarterRolesFor(TenantShape.Flat).Select(starterRole => starterRole.Key).Should().Equal("host-admin", "operator", "watcher");
        catalogue.StarterRoles.Single(starterRole => starterRole.Key == "operator").Permissions
            .Should().Equal([HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead], "a starter role's permissions are stored expanded");
    }

    [Fact]
    public void The_administrators_starter_role_holds_every_live_permission()
    {
        PermissionContribution[] contributions =
        [
            new([new Permission("gadget.read", "Gadgets", "See gadgets")]),
            new([new Permission("gizmo.read", "Gizmos", "See gizmos")]),
        ];
        var catalogue = TenancyCatalogue.Build(
            Application(permissions: [.. HostCatalogue.Permissions, new Permission("widget.old", "Widgets", "Old", Retired: true)]),
            contributions.SelectMany(contribution => contribution.Permissions));

        var administrators = catalogue.AdministratorStarterRoleFor(TenantShape.Hierarchical);

        administrators.Permissions.Should().Equal(catalogue.LivePermissions);
        administrators.Permissions.Should().Contain([TenancyPermissions.RolesManage, HostCatalogue.WidgetCreate, "gadget.read", "gizmo.read"],
            "Tenancy's, the application's and every contribution's")
            .And.NotContain("widget.old", "a retired permission is not live")
            .And.BeInAscendingOrder(StringComparer.Ordinal);
        catalogue.Permissions.Select(permission => permission.Module).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void An_administering_starter_role_may_list_permissions()
    {
        var assign = new Permission("widget.assign", "Widgets", "Hand widgets to people", Implies: [HostCatalogue.WidgetRead], ManagesAccess: true);
        var catalogue = TenancyCatalogue.Build(
            Application(
                starterRoles: [Administrators with { Permissions = [.. TenancysOwn, " widget.assign ", TenancyPermissions.RolesManage] }],
                permissions: [.. HostCatalogue.Permissions, assign]),
            [new Permission("gauges.read", "Gauges", "Read gauges")]);

        var administrators = catalogue.AdministratorStarterRoleFor(TenantShape.Hierarchical);

        administrators.Permissions.Should().Equal(
            [
                TenancyPermissions.GrantsManage, TenancyPermissions.HistoryView, TenancyPermissions.RolesManage, TenancyPermissions.SeatsManage, TenancyPermissions.SettingsManage, TenancyPermissions.UnitsManage,
                "widget.assign", HostCatalogue.WidgetRead,
            ],
            "it holds what it lists, expanded as any starter role is: trimmed, each permission once, with what a permission implies, in ordinal order");
        administrators.Permissions.Should().NotContain([HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, "gauges.read"], "a live permission it does not list is not its");
        catalogue.LivePermissions.Should().Contain([HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, "gauges.read"]);
        administrators.Administers.Should().BeTrue();
        catalogue.AdministratorStarterRoleFor(TenantShape.Flat).Should().BeSameAs(administrators, "the one starter role is for every shape");
        catalogue.StarterRoles.Should().ContainSingle().Which.Should().BeSameAs(administrators);

        // What it lists is checked as any starter role's list is.
        Problems(Application(
                starterRoles: [Administrators with { Permissions = [.. TenancysOwn, "widget.fly", "widget.old"] }],
                permissions: [.. HostCatalogue.Permissions, new Permission("widget.old", "Widgets", "Old", Retired: true)]))
            .Should().BeEquivalentTo(
                "The starter role 'host-admin' lists 'widget.fly', which is unknown to the catalogue.",
                "The starter role 'host-admin' lists 'widget.old', which is retired.");
    }

    [Fact]
    public void A_listing_administering_starter_role_must_hold_every_tenancy_permission()
    {
        Problems(Application(starterRoles: [Administrators with { Permissions = [.. TenancysOwn.Where(permission => permission != TenancyPermissions.SettingsManage)] }]))
            .Should().ContainSingle().Which.Should().Be(
                "The administrators' starter role 'host-admin' lists permissions but not 'tenancy.settings.manage', which is one of Tenancy's own: "
                + "an administrator holds every permission that manages access, and Tenancy's own.");

        // Every permission it leaves out is named, each in a problem of its own, the administrator's permission among them.
        Problems(Application(starterRoles: [Administrators with { Permissions = [HostCatalogue.WidgetRead, TenancyPermissions.GrantsManage] }]))
            .Should().Equal(
                LeftOut("host-admin", TenancyPermissions.HistoryView, "is one of Tenancy's own"),
                LeftOut("host-admin", TenancyPermissions.RolesManage, "is one of Tenancy's own"),
                LeftOut("host-admin", TenancyPermissions.SeatsManage, "is one of Tenancy's own"),
                LeftOut("host-admin", TenancyPermissions.SettingsManage, "is one of Tenancy's own"),
                LeftOut("host-admin", TenancyPermissions.UnitsManage, "is one of Tenancy's own"));

        // Each starter role answers for its own list: the one that lists nothing is not asked, and a flat tenant's
        // administrators hold the units permission like any other of Tenancy's.
        Problems(Application(starterRoles:
            [
                Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat, Permissions = [.. TenancysOwn.Where(permission => permission != TenancyPermissions.UnitsManage)] },
                Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical },
            ]))
            .Should().ContainSingle().Which.Should().Be(LeftOut("flat-admin", TenancyPermissions.UnitsManage, "is one of Tenancy's own"));
    }

    [Fact]
    public void A_listing_administering_starter_role_must_hold_every_permission_that_manages_access()
    {
        var assign = new Permission("widget.assign", "Widgets", "Hand widgets to people", ManagesAccess: true);
        var retired = new Permission("widget.old", "Widgets", "Old", Retired: true, ManagesAccess: true);
        var locks = new Permission("gauges.lock", "Gauges", "Lock gauges", ManagesAccess: true);
        var hands = new Permission("gauges.assign", "Gauges", "Hand gauges to people");

        // Marked where the application declares it, where a module does, and by the application for a module.
        ApplicationCatalogue Listing(params string[] permissions)
            => Application(
                starterRoles: [Administrators with { Permissions = [.. TenancysOwn, .. permissions] }],
                permissions: [.. HostCatalogue.Permissions, assign, retired]) with { AccessManagingPermissions = ["gauges.assign"] };

        Problems(Listing(HostCatalogue.WidgetCreate), locks, hands)
            .Should().Equal(
                LeftOut("host-admin", "gauges.assign", "manages access"),
                LeftOut("host-admin", "gauges.lock", "manages access"),
                LeftOut("host-admin", "widget.assign", "manages access"));
        Problems(Listing("gauges.assign", "gauges.lock"), locks, hands)
            .Should().ContainSingle().Which.Should().Be(
                "The administrators' starter role 'host-admin' lists permissions but not 'widget.assign', which manages access: "
                + "an administrator holds every permission that manages access, and Tenancy's own.");

        var catalogue = TenancyCatalogue.Build(Listing("gauges.assign", "gauges.lock", "widget.assign"), [locks, hands]);
        catalogue.AdministratorStarterRoleFor(TenantShape.Flat).Permissions
            .Should().Equal(
                catalogue.AccessManagingPermissions.Append(TenancyPermissions.HistoryView).Order(StringComparer.Ordinal),
                "this starter role lists the permissions that manage access and Tenancy's own, and no others")
            .And.NotContain([HostCatalogue.WidgetRead, "widget.old"], "neither a permission of the application that manages no access nor a retired one is asked of it");

        // A permission it holds through one that implies it counts: what is checked is what the role made from it holds.
        var rule = new Permission("widget.rule", "Widgets", "Rule widgets", Implies: ["widget.assign"], ManagesAccess: true);
        TenancyCatalogue.Build(
                Application(starterRoles: [Administrators with { Permissions = [.. TenancysOwn, "widget.rule"] }], permissions: [.. HostCatalogue.Permissions, assign, rule]),
                [])
            .AdministratorStarterRoleFor(TenantShape.Flat).Permissions.Should().Contain(["widget.assign", "widget.rule"]);

        // A permission that starts to manage access stops a catalogue that built until then: the starter role lists it first.
        var tenancysOnly = Application(starterRoles: [Administrators with { Permissions = TenancysOwn }]);
        TenancyCatalogue.Build(tenancysOnly, []).AdministratorStarterRoleFor(TenantShape.Flat).Permissions.Should().BeEquivalentTo(TenancysOwn);
        Problems(tenancysOnly with { AccessManagingPermissions = [HostCatalogue.WidgetCreate] })
            .Should().ContainSingle().Which.Should().Be(LeftOut("host-admin", HostCatalogue.WidgetCreate, "manages access"));
    }

    [Fact]
    public void An_administering_starter_role_that_lists_nothing_still_holds_every_live_permission()
    {
        var application = Application(
            starterRoles:
            [
                Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat },
                Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical, Permissions = [.. TenancysOwn, "widget.assign"] },
            ],
            permissions:
            [
                .. HostCatalogue.Permissions,
                new Permission("widget.assign", "Widgets", "Hand widgets to people", ManagesAccess: true),
                new Permission("widget.old", "Widgets", "Old", Retired: true),
            ]);

        var catalogue = TenancyCatalogue.Build(application, []);

        catalogue.AdministratorStarterRoleFor(TenantShape.Flat).Permissions.Should().Equal(catalogue.LivePermissions, "a starter role that lists nothing holds every live permission, next to one that lists its own")
            .And.Contain([HostCatalogue.WidgetRead, HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, "widget.assign"])
            .And.NotContain("widget.old", "a retired permission is not live");
        catalogue.AdministratorStarterRoleFor(TenantShape.Hierarchical).Permissions.Should().BeEquivalentTo([.. TenancysOwn, "widget.assign"]);

        // A list that is not there is one that lists nothing.
        var unlisted = TenancyCatalogue.Build(Application(starterRoles: [Administrators with { Permissions = null! }]), []);
        unlisted.AdministratorStarterRoleFor(TenantShape.Hierarchical).Permissions.Should().Equal(unlisted.LivePermissions);

        // A permission declared later comes with the starter role that lists nothing, and not with the one that lists.
        var later = TenancyCatalogue.Build(application, [new Permission("gauges.read", "Gauges", "Read gauges")]);
        later.AdministratorStarterRoleFor(TenantShape.Flat).Permissions.Should().Equal(later.LivePermissions).And.Contain("gauges.read");
        later.AdministratorStarterRoleFor(TenantShape.Hierarchical).Permissions.Should().Equal(catalogue.AdministratorStarterRoleFor(TenantShape.Hierarchical).Permissions);
    }

    [Fact]
    public void Implications_are_one_hop_and_never_self()
    {
        Problems(Application(permissions: [new Permission("widget.spin", "Widgets", "Spin", Implies: ["widget.spin"])]))
            .Should().ContainSingle().Which.Should().Be("'widget.spin' implies itself.");

        Problems(Application(permissions: [new Permission("widget.spin", "Widgets", "Spin", Implies: ["widget.fly"])]))
            .Should().ContainSingle().Which.Should().Contain("unknown to the catalogue");

        Problems(Application(permissions:
            [
                new Permission("widget.read", "Widgets", "See"),
                new Permission("widget.change", "Widgets", "Change", Implies: ["widget.read"]),
                new Permission("widget.admin", "Widgets", "Everything", Implies: ["widget.change"]),
            ]))
            .Should().ContainSingle().Which.Should().Contain("'widget.change' is implied by another permission and implies permissions itself");
    }

    [Fact]
    public void An_application_that_adds_nothing_builds_from_the_empty_catalogue_and_its_modules_permissions()
    {
        var gadgets = new Permission("gadget.use", "Gadgets", "Use gadgets");

        var catalogue = TenancyCatalogue.Build(new ApplicationCatalogue(), [gadgets]);

        catalogue.LivePermissions.Should().BeEquivalentTo([.. TenancyPermissions.All.Select(permission => permission.Name), gadgets.Name]);
        catalogue.StarterRoles.Should().ContainSingle().Which.Key.Should().Be(TenancyStarterRoles.DefaultAdministratorsKey);
        catalogue.StarterRoles[0].Permissions.Should().Contain(gadgets.Name, "the default administrators' starter role holds every live permission, a module's included");
        catalogue.HasDefaultAdministrators.Should().BeTrue();
        new ApplicationCatalogue().StarterRoles.Should().BeEmpty("every part of it is optional, and a starter role list that is not given is empty");

        // An export that builds the catalogue without the registration names the modules' permissions alone, and gets the same.
        var fromTheModules = TenancyCatalogue.Build([gadgets]);
        fromTheModules.LivePermissions.Should().Equal(catalogue.LivePermissions);
        fromTheModules.AccessManagingPermissions.Should().Equal(catalogue.AccessManagingPermissions);
        fromTheModules.StarterRoles.Select(starterRole => (starterRole.Key, starterRole.Permissions.Count)).Should().Equal(catalogue.StarterRoles.Select(starterRole => (starterRole.Key, starterRole.Permissions.Count)));
        fromTheModules.HasDefaultAdministrators.Should().BeTrue();
    }

    [Fact]
    public void An_unknown_permission_is_not_live()
    {
        var catalogue = TenancyCatalogue.Build(Application(permissions: [.. HostCatalogue.Permissions, new Permission("widget.old", "Widgets", "Old", Retired: true)]), []);

        catalogue.Knows("widget.fly").Should().BeFalse();
        catalogue.IsLive("widget.fly").Should().BeFalse();
        catalogue.Knows("widget.old").Should().BeTrue();
        catalogue.IsLive("widget.old").Should().BeFalse();
        catalogue.IsLive(HostCatalogue.WidgetRead).Should().BeTrue();
        catalogue.LivePermissions.Should().NotContain("widget.old");
        catalogue.Permissions.Should().Contain(permission => permission.Name == "widget.old", "a retired permission stays declared");

        FluentActions.Invoking(() => catalogue.RequireAskable("widget.fly")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => catalogue.RequireAskable("widget.old")).Should().NotThrow("a retired permission may be asked about, and holds nowhere");
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var exception = FluentActions.Invoking(() => TenancyCatalogue.Build(
                new ApplicationCatalogue(
                    StarterRoles:
                    [
                        new StarterRole("watcher", "Watcher", "Looks", ["widget.fly"]),
                        new StarterRole("flat-admin", "Flat administrator", "Runs a flat tenant", [], SeededFor: TenantShape.Flat, Administers: true),
                    ],
                    Permissions: [new Permission("tenancy.extra", "Tenancy", "Taken")]),
                [new Permission("Bad Key", "Gadgets", "Bad")]))
            .Should().Throw<TenancyCatalogueException>().Which;

        exception.Problems.Should().HaveCount(4, "a tenancy permission, a malformed permission, an unknown starter role key, and no administrators' starter role for a hierarchical tenant");
        exception.Message.Should().StartWith("The Tenancy catalogue has 4 problems:");
        foreach (var problem in exception.Problems)
        {
            exception.Message.Should().Contain(problem);
        }
    }

    [Fact]
    public void Starter_role_names_follow_the_role_rules_and_are_unique_ignoring_case()
    {
        var problems = Problems(Application(starterRoles:
        [
            Administrators,
            new StarterRole("watcher", "Watcher", "Looks", [HostCatalogue.WidgetRead]),
            new StarterRole("lookout", " watcher ", "Looks too", [HostCatalogue.WidgetRead]),
            new StarterRole("blank-name", " ", "Has no name", [HostCatalogue.WidgetRead]),
            new StarterRole("wordy", "Wordy", new string('d', 1001), [HostCatalogue.WidgetRead]),
            new StarterRole("long", new string('n', 121), "Long", [HostCatalogue.WidgetRead]),
        ]));

        problems.Should().BeEquivalentTo(
            "The starter roles 'watcher' and 'lookout' are both named 'watcher', ignoring case; a tenant's roles have names of their own.",
            "The starter role 'blank-name' has a name of 0 characters; a role's name is 1 to 120.",
            "The starter role 'wordy' has a description longer than 1000 characters.",
            "The starter role 'long' has a name of 121 characters; a role's name is 1 to 120.");
    }

    [Fact]
    public void Permissions_longer_than_a_storage_keeps_them_are_refused()
    {
        // The longest permission that fits: a word, a dot and a word, 128 characters in all.
        var fits = "widget." + new string('k', Permission.MaxNameLength - "widget.".Length);
        var tooLong = fits + "k";
        var longStarterRole = new string('p', StarterRole.MaxKeyLength + 1);

        TenancyCatalogue.Build(Application(permissions: [.. HostCatalogue.Permissions, new Permission(fits, "Widgets", "Fits")]), [])
            .LivePermissions.Should().Contain(fits);

        Problems(Application(
                starterRoles: [Administrators, new StarterRole(longStarterRole, "Long", "Long permission", [HostCatalogue.WidgetRead])],
                permissions: [.. HostCatalogue.Permissions, new Permission(tooLong, "Widgets", "Too long")]),
                new Permission("gadget." + tooLong, "Gadgets", "Too long as well"))
            .Should().BeEquivalentTo(
                "The application declares the permission '" + tooLong + "', which is longer than 128 characters.",
                "A contribution declares the permission 'gadget." + tooLong + "', which is longer than 128 characters.",
                "The starter role '" + longStarterRole + "' has a key longer than 64 characters.");
    }

    [Fact]
    public void An_application_permission_implying_a_tenancy_permission_is_refused()
    {
        var ruler = new Permission("widget.rule", "Widgets", "Rule widgets", Implies: [TenancyPermissions.RolesManage]);

        Problems(Application(permissions: [.. HostCatalogue.Permissions, ruler]))
            .Should().ContainSingle().Which.Should().Be("'widget.rule' implies 'tenancy.roles.manage': only Tenancy's own permissions imply permissions under 'tenancy.'.");
        Problems(Application(), ruler with { Name = "gadget.rule", Module = "Gadgets" })
            .Should().ContainSingle().Which.Should().StartWith("'gadget.rule' implies 'tenancy.roles.manage'");
    }

    [Fact]
    public void Tenancys_own_permissions_manage_access()
    {
        var catalogue = TenancyCatalogue.Build(Application(permissions: []), []);

        // Every one of them but the permission to read the history: reading what happened changes nobody's rights.
        TenancyPermissions.All.Where(permission => permission.Name != TenancyPermissions.HistoryView).Should().OnlyContain(permission => permission.ManagesAccess);
        catalogue.AccessManagingPermissions.Should().Equal(
            TenancyPermissions.GrantsManage, TenancyPermissions.RolesManage, TenancyPermissions.SeatsManage, TenancyPermissions.SettingsManage, TenancyPermissions.UnitsManage);
        catalogue.Permissions.Where(permission => !permission.ManagesAccess).Select(permission => permission.Name).Should().Equal(TenancyPermissions.HistoryView);
        catalogue.ManagesAccess(TenancyPermissions.HistoryView).Should().BeFalse("a role that holds it is given like any other");
        catalogue.IsLive(TenancyPermissions.HistoryView).Should().BeTrue();
        New.Catalogue().ManagesAccess(HostCatalogue.WidgetChange).Should().BeFalse("a permission nobody marks manages no access");
    }

    [Fact]
    public void A_permission_manages_access_only_when_it_is_marked_and_live()
    {
        var catalogue = TenancyCatalogue.Build(Application(permissions:
        [
            .. HostCatalogue.Permissions,
            new Permission("widget.assign", "Widgets", "Hand widgets to people", ManagesAccess: true),
            new Permission("widget.old", "Widgets", "Old", Retired: true, ManagesAccess: true),
        ]), []);

        catalogue.ManagesAccess("widget.assign").Should().BeTrue();
        catalogue.ManagesAccess(HostCatalogue.WidgetRead).Should().BeFalse("a permission nobody marks manages no access");
        catalogue.ManagesAccess("widget.old").Should().BeFalse("a retired permission manages nothing");
        catalogue.ManagesAccess("widget.fly").Should().BeFalse("nor does a permission the catalogue does not know");
        catalogue.AccessManagingPermissions.Should().Contain("widget.assign").And.NotContain(["widget.old", HostCatalogue.WidgetRead]);
        catalogue.Permissions.Single(permission => permission.Name == "widget.old").ManagesAccess.Should().BeTrue("the mark stays on the permission, and counts once it is live again");
    }

    [Fact]
    public void The_application_marks_permissions_of_its_modules_as_managing_access()
    {
        var assign = new Permission("gauges.assign", "Gauges", "Hand gauges to people");
        var marked = new Permission("gauges.lock", "Gauges", "Lock gauges", ManagesAccess: true);

        var catalogue = TenancyCatalogue.Build(
            Application() with { AccessManagingPermissions = ["gauges.assign", "gauges.lock", TenancyPermissions.RolesManage] },
            [assign, marked]);

        catalogue.ManagesAccess("gauges.assign").Should().BeTrue("the application marks a permission a module declares");
        catalogue.Permissions.Single(permission => permission.Name == "gauges.assign").ManagesAccess.Should().BeTrue("the built catalogue shows every mark");
        catalogue.AccessManagingPermissions.Should().Contain(["gauges.assign", "gauges.lock", TenancyPermissions.RolesManage])
            .And.BeInAscendingOrder(StringComparer.Ordinal)
            .And.OnlyHaveUniqueItems("listing a permission that is marked already, Tenancy's included, changes nothing");
        catalogue.ManagesAccess(HostCatalogue.WidgetCreate).Should().BeFalse();
    }

    [Fact]
    public void Marks_on_blank_unknown_or_repeated_permissions_are_refused()
    {
        Problems(Application() with { AccessManagingPermissions = ["widget.fly", " ", null!, HostCatalogue.WidgetCreate, HostCatalogue.WidgetCreate] })
            .Should().BeEquivalentTo(
                "The application marks 'widget.fly' as managing access, which is unknown to the catalogue.",
                "The application marks a blank permission as managing access.",
                "The application marks a blank permission as managing access.",
                "The application marks 'widget.create' as managing access more than once.");
    }

    [Fact]
    public void The_permissions_of_a_role_that_manage_access_are_its_marked_live_permissions()
    {
        var catalogue = TenancyCatalogue.Build(
            Application(permissions: [.. HostCatalogue.Permissions, new Permission("widget.old", "Widgets", "Old", Retired: true, ManagesAccess: true)])
                with { AccessManagingPermissions = [HostCatalogue.WidgetCreate] },
            []);
        string[] permissions = [TenancyPermissions.UnitsManage, HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate, "widget.old", "widget.gone", TenancyPermissions.GrantsManage, TenancyPermissions.UnitsManage];

        catalogue.AccessManagingPermissionsOf(new RoleFacts(true, permissions))
            .Should().Equal(TenancyPermissions.GrantsManage, TenancyPermissions.UnitsManage, HostCatalogue.WidgetCreate);
        catalogue.AccessManagingPermissionsOf(new RoleFacts(false, permissions)).Should().BeEmpty("an archived role grants nothing, so manages nothing");
        catalogue.AccessManagingPermissionsOf(new RoleFacts(true, [HostCatalogue.WidgetRead, "widget.old"])).Should().BeEmpty();
    }

    [Fact]
    public void A_permission_that_manages_no_access_implying_one_that_does_is_refused()
    {
        var assign = new Permission("widget.assign", "Widgets", "Hand widgets to people", ManagesAccess: true);
        var rule = new Permission("widget.rule", "Widgets", "Rule widgets", Implies: ["widget.assign"]);

        Problems(Application(permissions: [.. HostCatalogue.Permissions, assign, rule]))
            .Should().ContainSingle().Which.Should().Be(
                "'widget.rule' implies 'widget.assign', which manages access: a permission that manages no access implies none that does.");
        Problems(Application(permissions: [.. HostCatalogue.Permissions, assign with { ManagesAccess = false }, rule]) with { AccessManagingPermissions = ["widget.assign"] })
            .Should().ContainSingle("a permission the application marks counts as one marked where it is declared")
            .Which.Should().StartWith("'widget.rule' implies 'widget.assign', which manages access");

        // A permission that manages access may imply one that does, and one that does not.
        TenancyCatalogue.Build(
                Application(permissions: [.. HostCatalogue.Permissions, assign, rule with { ManagesAccess = true, Implies = ["widget.assign", HostCatalogue.WidgetRead] }]),
                [])
            .Expand(["widget.rule"]).Should().Equal("widget.assign", "widget.read", "widget.rule");
    }

    [Fact]
    public void A_retired_permission_implying_one_that_manages_access_is_not_refused()
    {
        var assign = new Permission("widget.assign", "Widgets", "Hand widgets to people", ManagesAccess: true);
        var rule = new Permission("widget.rule", "Widgets", "Rule widgets", Implies: ["widget.assign"]);

        var retiredSource = TenancyCatalogue.Build(Application(permissions: [.. HostCatalogue.Permissions, assign, rule with { Retired = true }]), []);
        var retiredTarget = TenancyCatalogue.Build(Application(permissions: [.. HostCatalogue.Permissions, assign with { Retired = true }, rule]), []);

        retiredSource.IsLive("widget.rule").Should().BeFalse();
        retiredTarget.Expand(["widget.rule"]).Should().Equal(["widget.rule"], "nothing follows an implication to a retired permission");
        retiredTarget.ManagesAccess("widget.rule").Should().BeFalse();
    }
}
