namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The catalogue is checked once, as a whole, and every problem is reported together, so a catalogue that
/// does not hold together stops start-up rather than a provisioning half way.
/// </summary>
public class CatalogueTests
{
    private static readonly RolePack Administrators = new("host-admin", "Administrator", "Runs the tenant", [], Administers: true);

    /// <summary>Tenancy's own keys, as an administrators' pack that lists its keys writes them.</summary>
    private static readonly string[] TenancysOwn = [.. TenancyKeys.Permissions.Select(permission => permission.Key)];

    /// <summary>The problem of an administrators' pack that lists keys and leaves <paramref name="key"/> out.</summary>
    private static string LeftOut(string pack, string key, string because)
        => "The administrators' pack '" + pack + "' lists keys but not '" + key + "', which " + because
           + ": an administrator holds every key that manages access, and Tenancy's own.";

    private static ApplicationCatalogue Application(
        IReadOnlyList<RolePack>? packs = null,
        IReadOnlyList<Permission>? permissions = null)
        => new(packs ?? [Administrators], permissions ?? HostCatalogue.Permissions);

    private static IReadOnlyList<string> Problems(ApplicationCatalogue application, params Permission[] contributed)
        => FluentActions.Invoking(() => TenancyCatalogue.Build(application, contributed))
            .Should().Throw<TenancyCatalogueException>().Which.Problems;

    [Fact]
    public void Tenancy_keys_are_always_present()
    {
        var catalogue = TenancyCatalogue.Build(Application(permissions: []), []);

        catalogue.Permissions.Select(permission => permission.Key).Should().Equal(
            TenancyKeys.SettingsManage, TenancyKeys.UnitsManage, TenancyKeys.SeatsManage, TenancyKeys.GrantsManage, TenancyKeys.RolesManage,
            TenancyKeys.HistoryView);
        catalogue.Permissions.Should().OnlyContain(permission => permission.Module == "Tenancy");
        catalogue.LiveKeys.Should().BeEquivalentTo(TenancyKeys.Permissions.Select(permission => permission.Key));
        TenancyKeys.AdministratorKey.Should().Be(TenancyKeys.RolesManage);
    }

    [Fact]
    public void An_application_key_under_tenancy_is_refused()
    {
        Problems(Application(permissions: [new Permission("tenancy.extras.manage", "Extras", "Extra things")]))
            .Should().ContainSingle().Which.Should().Contain("'tenancy.extras.manage'").And.Contain("belong to the Tenancy package");

        Problems(Application(), new Permission(TenancyKeys.SettingsManage, "Widgets", "Duplicate"))
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
            "The same declaration of 'gauges.read', 'gauges.lock' is added more than once. A module whose list is marked [TenancyPermissions] has it "
            + "added by the host's AddTenancyPermissionsOfModules(), and adds it with AddTenancyPermissions no more; and either is called once.");

        // Two declarations of one key that are not one and the same are refused as a duplicate, as before.
        Problems(Application(), [.. gauges, new Permission("gauges.read", "Gauges", "Read gauges")])
            .Should().ContainSingle().Which.Should().Be("'gauges.read' is declared more than once.");
    }

    [Fact]
    public void A_pack_naming_an_unknown_or_retired_key_is_refused()
    {
        var problems = Problems(Application(
            packs: [Administrators, new RolePack("painter", "Painter", "Paints", ["widget.paint", "widget.old"])],
            permissions: [.. HostCatalogue.Permissions, new Permission("widget.old", "Widgets", "Old", Retired: true)]));

        problems.Should().BeEquivalentTo(
            "The pack 'painter' lists 'widget.old', which is retired.",
            "The pack 'painter' lists 'widget.paint', which is unknown to the catalogue.");
    }

    [Fact]
    public void A_key_unknown_to_the_catalogue_is_told_with_how_a_modules_keys_reach_it()
    {
        // What a host that leaves out services.AddTenancyPermissionsOfModules() meets: a pack names a module's key
        // that never reached the catalogue, or, with no pack naming one, the first question about it.
        const string Cure = "*[TenancyPermissions]*services.AddTenancyPermissionsOfModules()*";
        var refused = FluentActions.Invoking(() => TenancyCatalogue.Build(Application(packs: [Administrators, new RolePack("viewer", "Viewer", "Looks", ["orders.view"])]), []))
            .Should().Throw<TenancyCatalogueException>().Which;

        refused.Problems.Should().ContainSingle().Which.Should().Be(
            "The pack 'viewer' lists 'orders.view', which is unknown to the catalogue.", "the problems stay what is wrong, one sentence each");
        refused.Message.Should().Match(Cure, "the cure is said once, after the problems")
            .And.EndWith("in a project that references the module.");

        FluentActions.Invoking(() => TenancyCatalogue.Build([]).RequireAskable("orders.view"))
            .Should().Throw<ArgumentException>().WithMessage("'orders.view' is not a key of the permission catalogue. " + Cure);

        // A catalogue that names no unknown key hears nothing about modules.
        FluentActions.Invoking(() => TenancyCatalogue.Build(Application(packs: [Administrators, Administrators]), []))
            .Should().Throw<TenancyCatalogueException>().Which.Message.Should().NotContain("[TenancyPermissions]");
    }

    [Fact]
    public void A_duplicate_pack_is_refused()
    {
        var watcher = new RolePack("watcher", "Watcher", "Looks", [HostCatalogue.WidgetRead]);

        Problems(Application(packs: [Administrators, watcher, watcher with { Name = "Other watcher" }]))
            .Should().ContainSingle().Which.Should().Be("The pack 'watcher' is declared more than once.");
    }

    [Fact]
    public void Each_shape_has_exactly_one_administrators_pack()
    {
        // The cure keeps one for the shape, however many there are and whichever shapes they are seeded for.
        static string MoreThanOne(string shape, string packs)
            => "A " + shape + " tenant needs exactly one administrators' pack seeded for it, and has " + packs + ". Keep one of them for it: seed the "
               + "others for another shape with SeededFor, or declare them without Administers.";

        Problems(Application(packs: [Administrators, Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat }]))
            .Should().ContainSingle().Which.Should().Be(MoreThanOne("flat", "2: host-admin, flat-admin"));

        // Two seeded for the same shape already.
        Problems(Application(packs:
            [
                Administrators with { Key = "owner", Name = "Owner", SeededFor = TenantShape.Flat },
                Administrators with { Key = "manager", Name = "Manager", SeededFor = TenantShape.Flat },
                Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical },
            ]))
            .Should().ContainSingle().Which.Should().Be(MoreThanOne("flat", "2: owner, manager"));

        // Three for two shapes: no SeededFor gives each shape one of them, so one goes without Administers.
        Problems(Application(packs:
            [
                Administrators,
                Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat },
                Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical },
            ]))
            .Should().Equal(MoreThanOne("flat", "2: host-admin, flat-admin"), MoreThanOne("hierarchical", "2: host-admin, tree-admin"));

        var perShape = TenancyCatalogue.Build(Application(packs:
        [
            // Named apart: a flat tenant that turns hierarchical is given the other pack's role next to its own.
            Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat },
            Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical },
        ]), []);
        perShape.AdministratorPackFor(TenantShape.Flat).Key.Should().Be("flat-admin");
        perShape.AdministratorPackFor(TenantShape.Hierarchical).Key.Should().Be("tree-admin");
        perShape.HasDefaultAdministrators.Should().BeFalse();
    }

    [Fact]
    public void An_application_that_declares_no_packs_gets_the_default_administrators_pack_for_every_shape()
    {
        var catalogue = TenancyCatalogue.Build(Application(packs: []), []);

        var administrators = catalogue.Packs.Should().ContainSingle().Which;
        administrators.Should().BeEquivalentTo(TenancyPacks.DefaultAdministrators with { Keys = catalogue.LiveKeys }, options => options.ComparingByMembers<RolePack>());
        administrators.Key.Should().Be("administrator");
        administrators.Name.Should().Be("Administrator");
        administrators.Description.Should().NotBeNullOrWhiteSpace("the screens that assign roles show it");
        administrators.Should().Match<RolePack>(pack => pack.Administers && pack.SeedOnProvision && pack.SeededFor == null);
        administrators.Keys.Should().Equal(catalogue.LiveKeys, "it lists nothing, and so holds every live key, Tenancy's and the application's");
        catalogue.HasDefaultAdministrators.Should().BeTrue();

        foreach (var shape in Enum.GetValues<TenantShape>())
        {
            catalogue.AdministratorPackFor(shape).Should().BeSameAs(administrators);
            catalogue.PacksFor(shape).Should().Equal([administrators], "a new tenant of every shape gets a copy");
        }

        // The application leaves the packs out altogether, and names its keys.
        var withoutPacks = TenancyCatalogue.Build(new ApplicationCatalogue(Permissions: HostCatalogue.Permissions), []);
        withoutPacks.Packs.Should().BeEquivalentTo(catalogue.Packs, options => options.ComparingByMembers<RolePack>());
        withoutPacks.LiveKeys.Should().Equal(catalogue.LiveKeys).And.Contain(HostCatalogue.WidgetCreate);

        var marked = new ApplicationCatalogue(Permissions: HostCatalogue.Permissions, AccessManagingKeys: [HostCatalogue.WidgetCreate]);
        marked.Packs.Should().BeEmpty();
        marked.Permissions.Should().BeSameAs(HostCatalogue.Permissions);
        TenancyCatalogue.Build(marked, []).AccessManagingKeys.Should().Contain(HostCatalogue.WidgetCreate, "the keys it marks reach the catalogue without a pack of its own");

        New.Catalogue().HasDefaultAdministrators.Should().BeFalse("the host declares an administrators' pack of its own");
        New.Catalogue().Packs.Should().NotContain(pack => pack.Key == TenancyPacks.DefaultAdministratorsKey);
    }

    [Fact]
    public void The_default_administrators_pack_comes_first_next_to_the_packs_the_application_declares()
    {
        var watcher = new RolePack("watcher", "Watcher", "Looks", [HostCatalogue.WidgetChange], Order: 10);
        var supervisor = new RolePack("supervisor", "Supervisor", "Runs a part", [TenancyKeys.UnitsManage], SeededFor: TenantShape.Hierarchical, Order: 20);

        var catalogue = TenancyCatalogue.Build(Application(packs: [watcher, supervisor]), []);

        catalogue.HasDefaultAdministrators.Should().BeTrue("no pack the application declares administers");
        catalogue.Packs.Select(pack => pack.Key).Should().Equal(TenancyPacks.DefaultAdministratorsKey, "watcher", "supervisor");
        catalogue.PacksFor(TenantShape.Hierarchical).Select(pack => pack.Key).Should().Equal(TenancyPacks.DefaultAdministratorsKey, "watcher", "supervisor");
        catalogue.PacksFor(TenantShape.Flat).Select(pack => pack.Key).Should().Equal(TenancyPacks.DefaultAdministratorsKey, "watcher");
        catalogue.Packs.Single(pack => pack.Key == "watcher").Keys.Should().Equal([HostCatalogue.WidgetChange, HostCatalogue.WidgetRead], "the application's packs are built as they were");
        catalogue.AdministratorPackFor(TenantShape.Flat).Keys.Should().Equal(catalogue.LiveKeys);
    }

    [Fact]
    public void The_default_administrators_pack_holds_a_key_declared_later_as_a_pack_that_lists_none_does()
    {
        var application = Application(packs: [], permissions: [.. HostCatalogue.Permissions, new Permission("widget.old", "Widgets", "Old", Retired: true)]);

        var before = TenancyCatalogue.Build(application, []);
        var later = TenancyCatalogue.Build(application, [new Permission("gauges.read", "Gauges", "Read gauges"), new Permission("gauges.lock", "Gauges", "Lock gauges", ManagesAccess: true)]);

        before.AdministratorPackFor(TenantShape.Flat).Keys.Should().Equal(before.LiveKeys).And.NotContain("widget.old", "a retired key is not live");
        later.AdministratorPackFor(TenantShape.Hierarchical).Keys.Should().Equal(later.LiveKeys)
            .And.Contain(["gauges.read", "gauges.lock"], "a module's key comes with it, one that manages access included, as the application never lists it")
            .And.Contain(before.LiveKeys);
    }

    [Fact]
    public void Administrators_packs_seeded_for_some_shapes_and_not_others_are_refused()
    {
        // The problem names the property to write, with the shape the catalogue lacks a pack for.
        static string NoneFor(string shape, string property)
            => "A " + shape + " tenant needs exactly one administrators' pack seeded for it, and has none. The default one, 'administrator', is added only "
               + "when the application declares no administrators' pack at all: declare one with SeededFor: " + property + " as well, or declare none.";

        Problems(Application(packs: [Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat }]))
            .Should().ContainSingle().Which.Should().Be(NoneFor("hierarchical", "TenantShape.Hierarchical"));

        // A pack that administers counts as declared even when something else about it is refused.
        Problems(Application(packs: [Administrators with { Key = " " }]))
            .Should().BeEquivalentTo("A pack has no key.", NoneFor("flat", "TenantShape.Flat"), NoneFor("hierarchical", "TenantShape.Hierarchical"));
    }

    [Fact]
    public void An_administrators_pack_seeded_for_a_shape_but_not_on_provision_is_named_as_the_one_it_lacks()
    {
        // Not seeded, it gives a new tenant no administrator, and the default is not added in its place. Its
        // SeededFor is right already, so the problem names SeedOnProvision, not another pack to declare.
        static string Unseeded(string shape, string packs, bool several)
            => "A " + shape + " tenant needs exactly one administrators' pack seeded for it, and has none: " + packs
               + (several ? " would be, but are" : " would be, but is") + " declared with SeedOnProvision: false, and the default one, 'administrator', "
               + (several ? "is not added in their place. Leave SeedOnProvision: false off one of them." : "is not added in its place. Leave SeedOnProvision: false off it.");

        Problems(Application(packs: [Administrators with { SeedOnProvision = false }]))
            .Should().Equal(Unseeded("flat", "'host-admin'", several: false), Unseeded("hierarchical", "'host-admin'", several: false));

        Problems(Application(packs:
            [
                Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat },
                Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical, SeedOnProvision = false },
            ]))
            .Should().ContainSingle().Which.Should().Be(Unseeded("hierarchical", "'tree-admin'", several: false));

        // Two that a hierarchical tenant would get: seeding either is enough for it.
        Problems(Application(packs:
            [
                Administrators with { SeedOnProvision = false },
                Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical, SeedOnProvision = false },
            ]))
            .Should().Equal(Unseeded("flat", "'host-admin'", several: false), Unseeded("hierarchical", "'host-admin', 'tree-admin'", several: true));
    }

    [Fact]
    public void A_pack_that_takes_the_default_administrators_key_or_name_is_refused()
    {
        const string Added = "the default administrators' pack, added because the application declares no administrators' pack";
        const string Fix = ", or declare it with Administers: true to make it the administrators' pack instead.";

        Problems(Application(packs: [new RolePack("administrator", "Keeper", "Keeps widgets", [HostCatalogue.WidgetRead])]))
            .Should().ContainSingle().Which.Should().Be("The pack 'administrator' has the key of " + Added + ". Give the pack another key" + Fix);

        Problems(Application(packs: [new RolePack("chief", " ADMINISTRATOR ", "Runs widgets", [HostCatalogue.WidgetChange])]))
            .Should().ContainSingle().Which.Should().Be(
                "The pack 'chief' is named 'ADMINISTRATOR', and " + Added + ", is named 'Administrator'; a tenant's roles have names of their own, "
                + "ignoring case. Give the pack another name" + Fix);

        Problems(Application(packs: [new RolePack("administrator", "Administrator", "Runs widgets", [HostCatalogue.WidgetChange])]))
            .Should().HaveCount(2, "the key and the name are each taken");
    }

    [Fact]
    public void A_pack_named_like_the_default_administrators_pack_in_dutch_is_refused_as_well()
    {
        // A tenant provisioned in Dutch gets the default's role as Beheerder, from the package's own texts, and a pack
        // the application named Beheerder would then be refused half way through provisioning, by a name the
        // application never wrote.
        Problems(Application(packs: [new RolePack("keeper", " beheerder ", "Keeps widgets", [HostCatalogue.WidgetRead])]))
            .Should().ContainSingle().Which.Should().Be(
                "The pack 'keeper' is named 'beheerder', and the default administrators' pack, added because the application declares no "
                + "administrators' pack, is named 'Beheerder' in Dutch; a tenant's roles have names of their own, ignoring case. Give the pack "
                + "another name, or declare it with Administers: true to make it the administrators' pack instead.");

        // An administrators' pack of the application's own may be called anything: no default is added next to it.
        TenancyCatalogue.Build(Application(packs: [Administrators with { Name = "Beheerder" }]), []).HasDefaultAdministrators.Should().BeFalse();

        // Declared as the administrators' pack, it is the application's own, and nothing is added.
        var own = TenancyCatalogue.Build(Application(packs: [new RolePack("administrator", "Administrator", "Runs widgets", [], Administers: true)]), []);
        own.HasDefaultAdministrators.Should().BeFalse();
        own.Packs.Should().ContainSingle().Which.Description.Should().Be("Runs widgets");
    }

    [Fact]
    public void Packs_for_a_shape_are_the_seeded_ones_of_that_shape_in_order()
    {
        var catalogue = New.Catalogue();

        catalogue.PacksFor(TenantShape.Hierarchical).Select(pack => pack.Key).Should().Equal("host-admin", "supervisor", "operator", "watcher");
        catalogue.PacksFor(TenantShape.Flat).Select(pack => pack.Key).Should().Equal("host-admin", "operator", "watcher");
        catalogue.Packs.Single(pack => pack.Key == "operator").Keys
            .Should().Equal([HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead], "a pack's keys are stored expanded");
    }

    [Fact]
    public void The_administrators_pack_holds_every_live_key()
    {
        PermissionContribution[] contributions =
        [
            new([new Permission("gadget.read", "Gadgets", "See gadgets")]),
            new([new Permission("gizmo.read", "Gizmos", "See gizmos")]),
        ];
        var catalogue = TenancyCatalogue.Build(
            Application(permissions: [.. HostCatalogue.Permissions, new Permission("widget.old", "Widgets", "Old", Retired: true)]),
            contributions.SelectMany(contribution => contribution.Permissions));

        var administrators = catalogue.AdministratorPackFor(TenantShape.Hierarchical);

        administrators.Keys.Should().Equal(catalogue.LiveKeys);
        administrators.Keys.Should().Contain([TenancyKeys.RolesManage, HostCatalogue.WidgetCreate, "gadget.read", "gizmo.read"],
            "Tenancy's, the application's and every contribution's")
            .And.NotContain("widget.old", "a retired key is not live")
            .And.BeInAscendingOrder(StringComparer.Ordinal);
        catalogue.Permissions.Select(permission => permission.Module).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void An_administering_pack_may_list_keys()
    {
        var assign = new Permission("widget.assign", "Widgets", "Hand widgets to people", Implies: [HostCatalogue.WidgetRead], ManagesAccess: true);
        var catalogue = TenancyCatalogue.Build(
            Application(
                packs: [Administrators with { Keys = [.. TenancysOwn, " widget.assign ", TenancyKeys.RolesManage] }],
                permissions: [.. HostCatalogue.Permissions, assign]),
            [new Permission("gauges.read", "Gauges", "Read gauges")]);

        var administrators = catalogue.AdministratorPackFor(TenantShape.Hierarchical);

        administrators.Keys.Should().Equal(
            [
                TenancyKeys.GrantsManage, TenancyKeys.HistoryView, TenancyKeys.RolesManage, TenancyKeys.SeatsManage, TenancyKeys.SettingsManage, TenancyKeys.UnitsManage,
                "widget.assign", HostCatalogue.WidgetRead,
            ],
            "it holds what it lists, expanded as any pack is: trimmed, each key once, with what a key implies, in ordinal order");
        administrators.Keys.Should().NotContain([HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, "gauges.read"], "a live key it does not list is not its");
        catalogue.LiveKeys.Should().Contain([HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, "gauges.read"]);
        administrators.Administers.Should().BeTrue();
        catalogue.AdministratorPackFor(TenantShape.Flat).Should().BeSameAs(administrators, "the one pack is for every shape");
        catalogue.Packs.Should().ContainSingle().Which.Should().BeSameAs(administrators);

        // What it lists is checked as any pack's list is.
        Problems(Application(
                packs: [Administrators with { Keys = [.. TenancysOwn, "widget.fly", "widget.old"] }],
                permissions: [.. HostCatalogue.Permissions, new Permission("widget.old", "Widgets", "Old", Retired: true)]))
            .Should().BeEquivalentTo(
                "The pack 'host-admin' lists 'widget.fly', which is unknown to the catalogue.",
                "The pack 'host-admin' lists 'widget.old', which is retired.");
    }

    [Fact]
    public void A_listing_administering_pack_must_hold_every_tenancy_key()
    {
        Problems(Application(packs: [Administrators with { Keys = [.. TenancysOwn.Where(key => key != TenancyKeys.SettingsManage)] }]))
            .Should().ContainSingle().Which.Should().Be(
                "The administrators' pack 'host-admin' lists keys but not 'tenancy.settings.manage', which is one of Tenancy's own: "
                + "an administrator holds every key that manages access, and Tenancy's own.");

        // Every key it leaves out is named, each in a problem of its own, the administrator's key among them.
        Problems(Application(packs: [Administrators with { Keys = [HostCatalogue.WidgetRead, TenancyKeys.GrantsManage] }]))
            .Should().Equal(
                LeftOut("host-admin", TenancyKeys.HistoryView, "is one of Tenancy's own"),
                LeftOut("host-admin", TenancyKeys.RolesManage, "is one of Tenancy's own"),
                LeftOut("host-admin", TenancyKeys.SeatsManage, "is one of Tenancy's own"),
                LeftOut("host-admin", TenancyKeys.SettingsManage, "is one of Tenancy's own"),
                LeftOut("host-admin", TenancyKeys.UnitsManage, "is one of Tenancy's own"));

        // Each pack answers for its own list: the one that lists nothing is not asked, and a flat tenant's
        // administrators hold the units key like any other of Tenancy's.
        Problems(Application(packs:
            [
                Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat, Keys = [.. TenancysOwn.Where(key => key != TenancyKeys.UnitsManage)] },
                Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical },
            ]))
            .Should().ContainSingle().Which.Should().Be(LeftOut("flat-admin", TenancyKeys.UnitsManage, "is one of Tenancy's own"));
    }

    [Fact]
    public void A_listing_administering_pack_must_hold_every_key_that_manages_access()
    {
        var assign = new Permission("widget.assign", "Widgets", "Hand widgets to people", ManagesAccess: true);
        var retired = new Permission("widget.old", "Widgets", "Old", Retired: true, ManagesAccess: true);
        var locks = new Permission("gauges.lock", "Gauges", "Lock gauges", ManagesAccess: true);
        var hands = new Permission("gauges.assign", "Gauges", "Hand gauges to people");

        // Marked where the application declares it, where a module does, and by the application for a module.
        ApplicationCatalogue Listing(params string[] keys)
            => Application(
                packs: [Administrators with { Keys = [.. TenancysOwn, .. keys] }],
                permissions: [.. HostCatalogue.Permissions, assign, retired]) with { AccessManagingKeys = ["gauges.assign"] };

        Problems(Listing(HostCatalogue.WidgetCreate), locks, hands)
            .Should().Equal(
                LeftOut("host-admin", "gauges.assign", "manages access"),
                LeftOut("host-admin", "gauges.lock", "manages access"),
                LeftOut("host-admin", "widget.assign", "manages access"));
        Problems(Listing("gauges.assign", "gauges.lock"), locks, hands)
            .Should().ContainSingle().Which.Should().Be(
                "The administrators' pack 'host-admin' lists keys but not 'widget.assign', which manages access: "
                + "an administrator holds every key that manages access, and Tenancy's own.");

        var catalogue = TenancyCatalogue.Build(Listing("gauges.assign", "gauges.lock", "widget.assign"), [locks, hands]);
        catalogue.AdministratorPackFor(TenantShape.Flat).Keys
            .Should().Equal(
                catalogue.AccessManagingKeys.Append(TenancyKeys.HistoryView).Order(StringComparer.Ordinal),
                "this pack lists the keys that manage access and Tenancy's own, and no others")
            .And.NotContain([HostCatalogue.WidgetRead, "widget.old"], "neither a key of the application that manages no access nor a retired one is asked of it");

        // A key it holds through one that implies it counts: what is checked is what the role made from it holds.
        var rule = new Permission("widget.rule", "Widgets", "Rule widgets", Implies: ["widget.assign"], ManagesAccess: true);
        TenancyCatalogue.Build(
                Application(packs: [Administrators with { Keys = [.. TenancysOwn, "widget.rule"] }], permissions: [.. HostCatalogue.Permissions, assign, rule]),
                [])
            .AdministratorPackFor(TenantShape.Flat).Keys.Should().Contain(["widget.assign", "widget.rule"]);

        // A key that starts to manage access stops a catalogue that built until then: the pack lists it first.
        var tenancysOnly = Application(packs: [Administrators with { Keys = TenancysOwn }]);
        TenancyCatalogue.Build(tenancysOnly, []).AdministratorPackFor(TenantShape.Flat).Keys.Should().BeEquivalentTo(TenancysOwn);
        Problems(tenancysOnly with { AccessManagingKeys = [HostCatalogue.WidgetCreate] })
            .Should().ContainSingle().Which.Should().Be(LeftOut("host-admin", HostCatalogue.WidgetCreate, "manages access"));
    }

    [Fact]
    public void An_administering_pack_that_lists_nothing_still_holds_every_live_key()
    {
        var application = Application(
            packs:
            [
                Administrators with { Key = "flat-admin", Name = "Flat administrator", SeededFor = TenantShape.Flat },
                Administrators with { Key = "tree-admin", Name = "Tree administrator", SeededFor = TenantShape.Hierarchical, Keys = [.. TenancysOwn, "widget.assign"] },
            ],
            permissions:
            [
                .. HostCatalogue.Permissions,
                new Permission("widget.assign", "Widgets", "Hand widgets to people", ManagesAccess: true),
                new Permission("widget.old", "Widgets", "Old", Retired: true),
            ]);

        var catalogue = TenancyCatalogue.Build(application, []);

        catalogue.AdministratorPackFor(TenantShape.Flat).Keys.Should().Equal(catalogue.LiveKeys, "a pack that lists nothing holds every live key, next to one that lists its own")
            .And.Contain([HostCatalogue.WidgetRead, HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, "widget.assign"])
            .And.NotContain("widget.old", "a retired key is not live");
        catalogue.AdministratorPackFor(TenantShape.Hierarchical).Keys.Should().BeEquivalentTo([.. TenancysOwn, "widget.assign"]);

        // A list that is not there is one that lists nothing.
        var unlisted = TenancyCatalogue.Build(Application(packs: [Administrators with { Keys = null! }]), []);
        unlisted.AdministratorPackFor(TenantShape.Hierarchical).Keys.Should().Equal(unlisted.LiveKeys);

        // A key declared later comes with the pack that lists nothing, and not with the one that lists.
        var later = TenancyCatalogue.Build(application, [new Permission("gauges.read", "Gauges", "Read gauges")]);
        later.AdministratorPackFor(TenantShape.Flat).Keys.Should().Equal(later.LiveKeys).And.Contain("gauges.read");
        later.AdministratorPackFor(TenantShape.Hierarchical).Keys.Should().Equal(catalogue.AdministratorPackFor(TenantShape.Hierarchical).Keys);
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
            .Should().ContainSingle().Which.Should().Contain("'widget.change' is implied by another key and implies keys itself");
    }

    [Fact]
    public void An_application_that_adds_nothing_builds_from_the_empty_catalogue_and_its_modules_keys()
    {
        var gadgets = new Permission("gadget.use", "Gadgets", "Use gadgets");

        var catalogue = TenancyCatalogue.Build(new ApplicationCatalogue(), [gadgets]);

        catalogue.LiveKeys.Should().BeEquivalentTo([.. TenancyKeys.Permissions.Select(permission => permission.Key), gadgets.Key]);
        catalogue.Packs.Should().ContainSingle().Which.Key.Should().Be(TenancyPacks.DefaultAdministratorsKey);
        catalogue.Packs[0].Keys.Should().Contain(gadgets.Key, "the default administrators' pack holds every live key, a module's included");
        catalogue.HasDefaultAdministrators.Should().BeTrue();
        new ApplicationCatalogue().Packs.Should().BeEmpty("every part of it is optional, and a pack list that is not given is empty");

        // An export that builds the catalogue without the registration names the modules' keys alone, and gets the same.
        var fromTheModules = TenancyCatalogue.Build([gadgets]);
        fromTheModules.LiveKeys.Should().Equal(catalogue.LiveKeys);
        fromTheModules.AccessManagingKeys.Should().Equal(catalogue.AccessManagingKeys);
        fromTheModules.Packs.Select(pack => (pack.Key, pack.Keys.Count)).Should().Equal(catalogue.Packs.Select(pack => (pack.Key, pack.Keys.Count)));
        fromTheModules.HasDefaultAdministrators.Should().BeTrue();
    }

    [Fact]
    public void An_unknown_key_is_not_live()
    {
        var catalogue = TenancyCatalogue.Build(Application(permissions: [.. HostCatalogue.Permissions, new Permission("widget.old", "Widgets", "Old", Retired: true)]), []);

        catalogue.Knows("widget.fly").Should().BeFalse();
        catalogue.IsLive("widget.fly").Should().BeFalse();
        catalogue.Knows("widget.old").Should().BeTrue();
        catalogue.IsLive("widget.old").Should().BeFalse();
        catalogue.IsLive(HostCatalogue.WidgetRead).Should().BeTrue();
        catalogue.LiveKeys.Should().NotContain("widget.old");
        catalogue.Permissions.Should().Contain(permission => permission.Key == "widget.old", "a retired key stays declared");

        FluentActions.Invoking(() => catalogue.RequireAskable("widget.fly")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => catalogue.RequireAskable("widget.old")).Should().NotThrow("a retired key may be asked about, and holds nowhere");
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var exception = FluentActions.Invoking(() => TenancyCatalogue.Build(
                new ApplicationCatalogue(
                    Packs:
                    [
                        new RolePack("watcher", "Watcher", "Looks", ["widget.fly"]),
                        new RolePack("flat-admin", "Flat administrator", "Runs a flat tenant", [], SeededFor: TenantShape.Flat, Administers: true),
                    ],
                    Permissions: [new Permission("tenancy.extra", "Tenancy", "Taken")]),
                [new Permission("Bad Key", "Gadgets", "Bad")]))
            .Should().Throw<TenancyCatalogueException>().Which;

        exception.Problems.Should().HaveCount(4, "a tenancy key, a malformed key, an unknown pack key, and no administrators' pack for a hierarchical tenant");
        exception.Message.Should().StartWith("The Tenancy catalogue has 4 problems:");
        foreach (var problem in exception.Problems)
        {
            exception.Message.Should().Contain(problem);
        }
    }

    [Fact]
    public void Pack_names_follow_the_role_rules_and_are_unique_ignoring_case()
    {
        var problems = Problems(Application(packs:
        [
            Administrators,
            new RolePack("watcher", "Watcher", "Looks", [HostCatalogue.WidgetRead]),
            new RolePack("lookout", " watcher ", "Looks too", [HostCatalogue.WidgetRead]),
            new RolePack("blank-name", " ", "Has no name", [HostCatalogue.WidgetRead]),
            new RolePack("wordy", "Wordy", new string('d', 1001), [HostCatalogue.WidgetRead]),
            new RolePack("long", new string('n', 121), "Long", [HostCatalogue.WidgetRead]),
        ]));

        problems.Should().BeEquivalentTo(
            "The packs 'watcher' and 'lookout' are both named 'watcher', ignoring case; a tenant's roles have names of their own.",
            "The pack 'blank-name' has a name of 0 characters; a role's name is 1 to 120.",
            "The pack 'wordy' has a description longer than 1000 characters.",
            "The pack 'long' has a name of 121 characters; a role's name is 1 to 120.");
    }

    [Fact]
    public void Keys_longer_than_a_storage_keeps_them_are_refused()
    {
        // The longest key that fits: a word, a dot and a word, 128 characters in all.
        var fits = "widget." + new string('k', Permission.MaxKeyLength - "widget.".Length);
        var tooLong = fits + "k";
        var longPack = new string('p', RolePack.MaxKeyLength + 1);

        TenancyCatalogue.Build(Application(permissions: [.. HostCatalogue.Permissions, new Permission(fits, "Widgets", "Fits")]), [])
            .LiveKeys.Should().Contain(fits);

        Problems(Application(
                packs: [Administrators, new RolePack(longPack, "Long", "Long key", [HostCatalogue.WidgetRead])],
                permissions: [.. HostCatalogue.Permissions, new Permission(tooLong, "Widgets", "Too long")]),
                new Permission("gadget." + tooLong, "Gadgets", "Too long as well"))
            .Should().BeEquivalentTo(
                "The application declares the key '" + tooLong + "', which is longer than 128 characters.",
                "A contribution declares the key 'gadget." + tooLong + "', which is longer than 128 characters.",
                "The pack '" + longPack + "' has a key longer than 64 characters.");
    }

    [Fact]
    public void An_application_key_implying_a_tenancy_key_is_refused()
    {
        var ruler = new Permission("widget.rule", "Widgets", "Rule widgets", Implies: [TenancyKeys.RolesManage]);

        Problems(Application(permissions: [.. HostCatalogue.Permissions, ruler]))
            .Should().ContainSingle().Which.Should().Be("'widget.rule' implies 'tenancy.roles.manage': only Tenancy's own keys imply keys under 'tenancy.'.");
        Problems(Application(), ruler with { Key = "gadget.rule", Module = "Gadgets" })
            .Should().ContainSingle().Which.Should().StartWith("'gadget.rule' implies 'tenancy.roles.manage'");
    }

    [Fact]
    public void Tenancys_own_keys_manage_access()
    {
        var catalogue = TenancyCatalogue.Build(Application(permissions: []), []);

        // Every one of them but the key to read the history: reading what happened changes nobody's rights.
        TenancyKeys.Permissions.Where(permission => permission.Key != TenancyKeys.HistoryView).Should().OnlyContain(permission => permission.ManagesAccess);
        catalogue.AccessManagingKeys.Should().Equal(
            TenancyKeys.GrantsManage, TenancyKeys.RolesManage, TenancyKeys.SeatsManage, TenancyKeys.SettingsManage, TenancyKeys.UnitsManage);
        catalogue.Permissions.Where(permission => !permission.ManagesAccess).Select(permission => permission.Key).Should().Equal(TenancyKeys.HistoryView);
        catalogue.ManagesAccess(TenancyKeys.HistoryView).Should().BeFalse("a role that holds it is given like any other");
        catalogue.IsLive(TenancyKeys.HistoryView).Should().BeTrue();
        New.Catalogue().ManagesAccess(HostCatalogue.WidgetChange).Should().BeFalse("a key nobody marks manages no access");
    }

    [Fact]
    public void A_key_manages_access_only_when_it_is_marked_and_live()
    {
        var catalogue = TenancyCatalogue.Build(Application(permissions:
        [
            .. HostCatalogue.Permissions,
            new Permission("widget.assign", "Widgets", "Hand widgets to people", ManagesAccess: true),
            new Permission("widget.old", "Widgets", "Old", Retired: true, ManagesAccess: true),
        ]), []);

        catalogue.ManagesAccess("widget.assign").Should().BeTrue();
        catalogue.ManagesAccess(HostCatalogue.WidgetRead).Should().BeFalse("a key nobody marks manages no access");
        catalogue.ManagesAccess("widget.old").Should().BeFalse("a retired key manages nothing");
        catalogue.ManagesAccess("widget.fly").Should().BeFalse("nor does a key the catalogue does not know");
        catalogue.AccessManagingKeys.Should().Contain("widget.assign").And.NotContain(["widget.old", HostCatalogue.WidgetRead]);
        catalogue.Permissions.Single(permission => permission.Key == "widget.old").ManagesAccess.Should().BeTrue("the mark stays on the key, and counts once it is live again");
    }

    [Fact]
    public void The_application_marks_keys_of_its_modules_as_managing_access()
    {
        var assign = new Permission("gauges.assign", "Gauges", "Hand gauges to people");
        var marked = new Permission("gauges.lock", "Gauges", "Lock gauges", ManagesAccess: true);

        var catalogue = TenancyCatalogue.Build(
            Application() with { AccessManagingKeys = ["gauges.assign", "gauges.lock", TenancyKeys.RolesManage] },
            [assign, marked]);

        catalogue.ManagesAccess("gauges.assign").Should().BeTrue("the application marks a key a module declares");
        catalogue.Permissions.Single(permission => permission.Key == "gauges.assign").ManagesAccess.Should().BeTrue("the built catalogue shows every mark");
        catalogue.AccessManagingKeys.Should().Contain(["gauges.assign", "gauges.lock", TenancyKeys.RolesManage])
            .And.BeInAscendingOrder(StringComparer.Ordinal)
            .And.OnlyHaveUniqueItems("listing a key that is marked already, Tenancy's included, changes nothing");
        catalogue.ManagesAccess(HostCatalogue.WidgetCreate).Should().BeFalse();
    }

    [Fact]
    public void Marks_on_blank_unknown_or_repeated_keys_are_refused()
    {
        Problems(Application() with { AccessManagingKeys = ["widget.fly", " ", null!, HostCatalogue.WidgetCreate, HostCatalogue.WidgetCreate] })
            .Should().BeEquivalentTo(
                "The application marks 'widget.fly' as managing access, which is unknown to the catalogue.",
                "The application marks a blank key as managing access.",
                "The application marks a blank key as managing access.",
                "The application marks 'widget.create' as managing access more than once.");
    }

    [Fact]
    public void The_keys_of_a_role_that_manage_access_are_its_marked_live_keys()
    {
        var catalogue = TenancyCatalogue.Build(
            Application(permissions: [.. HostCatalogue.Permissions, new Permission("widget.old", "Widgets", "Old", Retired: true, ManagesAccess: true)])
                with { AccessManagingKeys = [HostCatalogue.WidgetCreate] },
            []);
        string[] keys = [TenancyKeys.UnitsManage, HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate, "widget.old", "widget.gone", TenancyKeys.GrantsManage, TenancyKeys.UnitsManage];

        catalogue.AccessManagingKeysOf(new RoleFacts(true, keys))
            .Should().Equal(TenancyKeys.GrantsManage, TenancyKeys.UnitsManage, HostCatalogue.WidgetCreate);
        catalogue.AccessManagingKeysOf(new RoleFacts(false, keys)).Should().BeEmpty("an archived role grants nothing, so manages nothing");
        catalogue.AccessManagingKeysOf(new RoleFacts(true, [HostCatalogue.WidgetRead, "widget.old"])).Should().BeEmpty();
    }

    [Fact]
    public void A_key_that_manages_no_access_implying_one_that_does_is_refused()
    {
        var assign = new Permission("widget.assign", "Widgets", "Hand widgets to people", ManagesAccess: true);
        var rule = new Permission("widget.rule", "Widgets", "Rule widgets", Implies: ["widget.assign"]);

        Problems(Application(permissions: [.. HostCatalogue.Permissions, assign, rule]))
            .Should().ContainSingle().Which.Should().Be(
                "'widget.rule' implies 'widget.assign', which manages access: a key that manages no access implies none that does.");
        Problems(Application(permissions: [.. HostCatalogue.Permissions, assign with { ManagesAccess = false }, rule]) with { AccessManagingKeys = ["widget.assign"] })
            .Should().ContainSingle("a key the application marks counts as one marked where it is declared")
            .Which.Should().StartWith("'widget.rule' implies 'widget.assign', which manages access");

        // A key that manages access may imply one that does, and one that does not.
        TenancyCatalogue.Build(
                Application(permissions: [.. HostCatalogue.Permissions, assign, rule with { ManagesAccess = true, Implies = ["widget.assign", HostCatalogue.WidgetRead] }]),
                [])
            .Expand(["widget.rule"]).Should().Equal("widget.assign", "widget.read", "widget.rule");
    }

    [Fact]
    public void A_retired_key_implying_one_that_manages_access_is_not_refused()
    {
        var assign = new Permission("widget.assign", "Widgets", "Hand widgets to people", ManagesAccess: true);
        var rule = new Permission("widget.rule", "Widgets", "Rule widgets", Implies: ["widget.assign"]);

        var retiredSource = TenancyCatalogue.Build(Application(permissions: [.. HostCatalogue.Permissions, assign, rule with { Retired = true }]), []);
        var retiredTarget = TenancyCatalogue.Build(Application(permissions: [.. HostCatalogue.Permissions, assign with { Retired = true }, rule]), []);

        retiredSource.IsLive("widget.rule").Should().BeFalse();
        retiredTarget.Expand(["widget.rule"]).Should().Equal(["widget.rule"], "nothing follows an implication to a retired key");
        retiredTarget.ManagesAccess("widget.rule").Should().BeFalse();
    }
}
