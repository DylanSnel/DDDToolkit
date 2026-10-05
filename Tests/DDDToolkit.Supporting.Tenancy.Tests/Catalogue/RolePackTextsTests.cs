using System.Collections;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// A role made from a pack is named in the language the application passes, when it has texts for the pack in
/// that language: at provisioning, and when a change of shape copies the packs the tenant has no copy of yet.
/// The texts are chosen once; afterwards the role is the tenant's, like any other. A translated name is checked
/// like any role's name.
/// </summary>
public class RolePackTextsTests
{
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl");

    private static HostTenancy.TenantToProvision Harbor(TenantShape shape, CultureInfo? language)
        => new("harbor", "Harbor Works", shape, "Harbor Works", "company", Guid.NewGuid(), "Ada", Language: language);

    private static Task<HostTenancy.ProvisionedTenant> Provision(Harness harness, HostTenancy.TenantToProvision command)
        => harness.Run(HostCaller.System, h => h.Tenants.ProvisionAsync(command, default));

    /// <summary>The host's four packs in Dutch.</summary>
    private static PackTexts DutchTexts() => new PackTexts()
        .In("nl", HostCatalogue.AdministratorPack, "Hoofdgebruiker", "Regelt de tenant")
        .In("nl", HostCatalogue.SupervisorPack, "Afdelingshoofd", "Leidt een deel van de organisatie")
        .In("nl", HostCatalogue.OperatorPack, "Bediener", "Werkt met widgets")
        .In("nl", HostCatalogue.WatcherPack, "Toeschouwer", "Bekijkt widgets");

    [Fact]
    public async Task A_tenant_provisioned_in_a_language_gets_its_packs_in_that_language()
    {
        var harness = new Harness(New.Catalogue()) { PackTexts = DutchTexts() };

        var provisioned = await Provision(harness, Harbor(TenantShape.Hierarchical, Dutch));

        var roles = harness.Store.RolesOf(provisioned.Tenant);
        roles.Select(role => (role.FromPack, role.Name, role.Description)).Should().BeEquivalentTo(
        [
            (HostCatalogue.AdministratorPack, "Hoofdgebruiker", "Regelt de tenant"),
            (HostCatalogue.SupervisorPack, "Afdelingshoofd", "Leidt een deel van de organisatie"),
            (HostCatalogue.OperatorPack, "Bediener", "Werkt met widgets"),
            (HostCatalogue.WatcherPack, "Toeschouwer", "Bekijkt widgets"),
        ]);

        // Only the texts differ: the keys are the pack's, and the first seat holds the administrators' role.
        foreach (var role in roles)
        {
            role.Keys.Should().Equal(harness.Catalogue.Packs.Single(pack => pack.Key == role.FromPack).Keys);
        }

        harness.Store.Role(provisioned.AdministratorRole).Name.Should().Be("Hoofdgebruiker");
        harness.Store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task A_role_keeps_the_texts_it_was_made_with()
    {
        var texts = DutchTexts();
        var harness = new Harness(New.Catalogue()) { PackTexts = texts };
        var provisioned = await Provision(harness, Harbor(TenantShape.Hierarchical, Dutch));
        var administrator = HostCaller.InSeat(provisioned.Tenant, provisioned.AdminSeat);
        var watcher = provisioned.RolesByPack[HostCatalogue.WatcherPack];

        // The application's texts change afterwards, and the tenant renames a role itself.
        texts.In("nl", HostCatalogue.OperatorPack, "Machinist", "Bedient widgets");
        await harness.Run(administrator, h => h.Roles.RenameAsync(watcher, "Bezoeker", "Volgt widgets", default));

        harness.Store.Role(provisioned.RolesByPack[HostCatalogue.OperatorPack]).Name.Should().Be("Bediener", "the texts were chosen once, when the role was made");
        harness.Store.Role(watcher).Name.Should().Be("Bezoeker", "a role made from a pack is the tenant's own afterwards");
    }

    [Fact]
    public async Task Without_pack_texts_the_catalogue_names_stay()
    {
        var catalogueNames = new[] { "Administrator", "Supervisor", "Operator", "Watcher" };

        // No texts registered: a language changes nothing.
        var plain = new Harness(New.Catalogue());
        var first = await Provision(plain, Harbor(TenantShape.Hierarchical, Dutch));
        plain.Store.RolesOf(first.Tenant).Select(role => role.Name).Should().BeEquivalentTo(catalogueNames);

        // Texts registered, and no language: nothing is asked.
        var texts = DutchTexts();
        var silent = new Harness(New.Catalogue()) { PackTexts = texts };
        var second = await Provision(silent, Harbor(TenantShape.Hierarchical, language: null));
        silent.Store.RolesOf(second.Tenant).Select(role => role.Name).Should().BeEquivalentTo(catalogueNames);
        texts.Asked.Should().BeEmpty("without a language there is no culture to ask the texts in");

        // Texts for one pack only, and a language the application has none in: the others keep the catalogue's.
        var partly = new Harness(New.Catalogue()) { PackTexts = new PackTexts().In("nl", HostCatalogue.WatcherPack, "Toeschouwer", "Bekijkt widgets") };
        var third = await Provision(partly, Harbor(TenantShape.Hierarchical, Dutch));
        partly.Store.RolesOf(third.Tenant).Select(role => role.Name).Should().BeEquivalentTo("Administrator", "Supervisor", "Operator", "Toeschouwer");

        var german = new Harness(New.Catalogue()) { PackTexts = texts };
        var fourth = await Provision(german, Harbor(TenantShape.Hierarchical, CultureInfo.GetCultureInfo("de")));
        german.Store.RolesOf(fourth.Tenant).Select(role => (role.Name, role.Description)).Should().Contain(("Watcher", "Looks at widgets"));
    }

    [Fact]
    public async Task The_texts_are_asked_for_each_pack_of_the_shape_in_the_language_passed()
    {
        var texts = DutchTexts();
        var harness = new Harness(New.Catalogue()) { PackTexts = texts };

        await Provision(harness, Harbor(TenantShape.Flat, CultureInfo.GetCultureInfo("nl-BE")));

        texts.Asked.Should().BeEquivalentTo(
        [
            (HostCatalogue.AdministratorPack, "nl-BE"), (HostCatalogue.OperatorPack, "nl-BE"), (HostCatalogue.WatcherPack, "nl-BE"),
        ], "a flat tenant gets no supervisor, and the culture reaches the application as it was passed");
        harness.Store.RolesOf(new TenantId(101)).Select(role => role.Name).Should().BeEquivalentTo("Hoofdgebruiker", "Bediener", "Toeschouwer");
    }

    [Fact]
    public async Task A_shape_change_copies_new_packs_in_the_language_passed()
    {
        var harness = new Harness(New.Catalogue()) { PackTexts = DutchTexts() };
        var provisioned = await Provision(harness, Harbor(TenantShape.Flat, language: null));
        var administrator = HostCaller.InSeat(provisioned.Tenant, provisioned.AdminSeat);

        await harness.Run(administrator, h => h.Tenants.ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, Dutch, default));

        var roles = harness.Store.RolesOf(provisioned.Tenant);
        roles.Should().ContainSingle(role => role.FromPack == HostCatalogue.SupervisorPack)
            .Which.Should().Match<HostRole>(role => role.Name == "Afdelingshoofd" && role.Description == "Leidt een deel van de organisatie");
        roles.Where(role => role.FromPack != HostCatalogue.SupervisorPack).Select(role => role.Name)
            .Should().BeEquivalentTo(["Administrator", "Operator", "Watcher"], "the roles the tenant had keep their names");
    }

    [Fact]
    public async Task A_shape_change_without_a_language_copies_the_catalogues_names()
    {
        var harness = new Harness(New.Catalogue()) { PackTexts = DutchTexts() };
        var provisioned = await Provision(harness, Harbor(TenantShape.Flat, Dutch));
        var administrator = HostCaller.InSeat(provisioned.Tenant, provisioned.AdminSeat);

        await harness.Run(administrator, h => h.Tenants.ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, default));

        harness.Store.RolesOf(provisioned.Tenant).Select(role => role.Name).Should().BeEquivalentTo("Hoofdgebruiker", "Bediener", "Toeschouwer", "Supervisor");
    }

    [Fact]
    public async Task Two_packs_translated_alike_are_refused_as_a_taken_name()
    {
        // The catalogue keeps its own names apart; a translation can still give two packs one name, in any case.
        var texts = DutchTexts().In("nl", HostCatalogue.WatcherPack, "BEDIENER", "Bekijkt widgets");
        var harness = new Harness(New.Catalogue()) { PackTexts = texts };

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.RoleNameTaken, () => Provision(harness, Harbor(TenantShape.Hierarchical, Dutch)));

        refusal.Arguments["Name"].Should().Be("BEDIENER");
        harness.Store.SaveCount.Should().Be(0);
        harness.Store.HasTenant(new TenantId(101)).Should().BeFalse("a refused provisioning leaves nothing behind");
    }

    [Fact]
    public async Task A_new_pack_translated_as_a_role_the_tenant_has_is_refused_at_a_shape_change()
    {
        var harness = new Harness(New.Catalogue()) { PackTexts = DutchTexts().In("nl", HostCatalogue.SupervisorPack, "bediener", "Leidt een deel van de organisatie") };
        var provisioned = await Provision(harness, Harbor(TenantShape.Flat, Dutch));
        var administrator = HostCaller.InSeat(provisioned.Tenant, provisioned.AdminSeat);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.RoleNameTaken,
            () => harness.Run(administrator, h => h.Tenants.ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, Dutch, default)));

        refusal.Arguments["Name"].Should().Be("bediener");
        harness.Store.Tenant(provisioned.Tenant).Shape.Should().Be(TenantShape.Flat);
        harness.Store.RolesOf(provisioned.Tenant).Should().HaveCount(3);
    }

    [Theory]
    [InlineData(" ", "Regelt de tenant", "role-name", "name")]
    [InlineData(null, "Regelt de tenant", "role-name", "name")]
    [InlineData("Hoofdgebruiker", null, null, null)]
    public async Task A_translated_text_is_checked_like_any_roles(string? name, string? description, string? what, string? field)
    {
        var texts = DutchTexts().In("nl", HostCatalogue.AdministratorPack, name!, description!);
        var harness = new Harness(New.Catalogue()) { PackTexts = texts };

        if (what is null)
        {
            var provisioned = await Provision(harness, Harbor(TenantShape.Flat, Dutch));
            harness.Store.Role(provisioned.AdministratorRole).Description.Should().BeEmpty("a description may be left out, as for a role made by hand");
            return;
        }

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NameInvalid, () => Provision(harness, Harbor(TenantShape.Flat, Dutch)));

        refusal.Arguments["What"].Should().Be(what);
        refusal.Arguments["Field"].Should().Be(field);
        harness.Store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task A_translated_text_that_is_too_long_is_refused()
    {
        var name = new Harness(New.Catalogue())
        {
            PackTexts = DutchTexts().In("nl", HostCatalogue.WatcherPack, new string('k', HostRole.MaxNameLength + 1), "Bekijkt widgets"),
        };
        (await Refused.WithCodeAsync(TenancyRefusals.NameInvalid, () => Provision(name, Harbor(TenantShape.Flat, Dutch))))
            .Arguments["What"].Should().Be("role-name");

        var description = new Harness(New.Catalogue())
        {
            PackTexts = DutchTexts().In("nl", HostCatalogue.WatcherPack, "Toeschouwer", new string('k', HostRole.MaxDescriptionLength + 1)),
        };
        var provisioned = await Provision(description, Harbor(TenantShape.Flat, language: null));
        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NameInvalid,
            () => Provision(description, Harbor(TenantShape.Flat, Dutch) with { Slug = "wharf" }));
        refusal.Arguments["What"].Should().Be("role-description");
        refusal.Arguments["Field"].Should().Be("description");
        description.Store.SaveCount.Should().Be(1, "only the tenant provisioned without a language was saved");
        description.Store.HasTenant(provisioned.Tenant).Should().BeTrue();
    }

    [Fact]
    public async Task The_texts_an_application_registers_reach_the_use_cases()
    {
        async Task<string[]> RoleNamesWith(Action<IServiceCollection> register)
        {
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FixedClock());
            services.AddTenancyCore<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>(options =>
            {
                options.Catalogue = HostCatalogue.Application;
                options.NewTenantId = () => new TenantId(7);
                options.NewSeatId = SeatId.CreateSequential;
                options.NewUnitId = OrganizationUnitId.CreateSequential;
                options.NewRoleId = RoleId.CreateSequential;
            });
            services.AddSingleton(provider => new InMemoryTenancyStore(provider.GetRequiredService<TenancyCatalogue>()));
            services.AddSingleton<HostTenancy.IStore>(provider => provider.GetRequiredService<InMemoryTenancyStore>());
            services.AddScoped<ISeatDirectory<TenantId, SeatId>, ListedSeats>();
            register(services);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await using var scope = provider.CreateAsyncScope();
            var store = provider.GetRequiredService<InMemoryTenancyStore>();
            using (TenancyCallers.Begin(HostCaller.System))
            {
                store.BeginUnitOfWork();
                await scope.ServiceProvider.GetRequiredService<HostTenancy.TenantCommands>()
                    .ProvisionAsync(Harbor(TenantShape.Flat, Dutch), TestContext.Current.CancellationToken);
            }

            return [.. store.RolesOf(new TenantId(7)).Select(role => role.Name)];
        }

        (await RoleNamesWith(services => services.AddSingleton<IRolePackTexts>(DutchTexts())))
            .Should().BeEquivalentTo("Hoofdgebruiker", "Bediener", "Toeschouwer");
        (await RoleNamesWith(_ => { }))
            .Should().BeEquivalentTo(["Administrator", "Operator", "Watcher"], "an application that registers no texts gets the catalogue's, as before there were any");
    }

    [Fact]
    public async Task The_default_administrators_pack_is_named_by_the_package_in_dutch_unless_the_application_names_it()
    {
        var catalogue = TenancyCatalogue.Build(HostCatalogue.Application with { Packs = [] }, []);

        async Task<(string Name, string Description)> AdministratorsIn(CultureInfo? language, IRolePackTexts? texts = null)
        {
            var harness = new Harness(catalogue) { PackTexts = texts };
            var provisioned = await Provision(harness, Harbor(TenantShape.Flat, language));
            var role = harness.Store.Role(provisioned.AdministratorRole);
            return (role.Name, role.Description);
        }

        var english = (TenancyPacks.DefaultAdministrators.Name, TenancyPacks.DefaultAdministrators.Description);
        var dutch = ("Beheerder", "Heeft elk recht: beheert de organisatie, de mensen en hun toegang");

        // The application registered no texts: the package ships the pack's name in Dutch, as it ships its refusals.
        (await AdministratorsIn(Dutch)).Should().Be(dutch);
        (await AdministratorsIn(CultureInfo.GetCultureInfo("nl-BE"))).Should().Be(dutch, "a regional culture falls back to its language");
        (await AdministratorsIn(CultureInfo.GetCultureInfo("de"))).Should().Be(english, "a language the package does not ship gets the catalogue's English");
        (await AdministratorsIn(language: null)).Should().Be(english, "without a language nothing is translated");

        // The application's texts come first, by the pack's key; where they have none for it, the package's stand.
        (await AdministratorsIn(Dutch, new PackTexts().In("nl", TenancyPacks.DefaultAdministratorsKey, "Hoofdbeheerder", "Regelt alles")))
            .Should().Be(("Hoofdbeheerder", "Regelt alles"));
        (await AdministratorsIn(CultureInfo.GetCultureInfo("de"), new PackTexts().In("de", TenancyPacks.DefaultAdministratorsKey, "Verwalter", "Verwaltet alles")))
            .Should().Be(("Verwalter", "Verwaltet alles"), "the application names it in a language the package does not ship");
        var asked = DutchTexts();
        (await AdministratorsIn(Dutch, asked)).Should().Be(dutch);
        asked.Asked.Should().Equal([(TenancyPacks.DefaultAdministratorsKey, "nl")], "the application is asked about the pack like any other");
    }

    [Fact]
    public async Task A_pack_of_the_applications_own_under_the_default_key_keeps_the_applications_texts()
    {
        // The application declares an administrators' pack keyed as the default is: it is the application's, and the
        // package has no texts for it.
        var catalogue = TenancyCatalogue.Build(
            HostCatalogue.Application with { Packs = [new RolePack(TenancyPacks.DefaultAdministratorsKey, "Harbor master", "Runs the harbor", [], Administers: true)] },
            []);
        var harness = new Harness(catalogue);

        var provisioned = await Provision(harness, Harbor(TenantShape.Flat, Dutch));

        harness.Store.Role(provisioned.AdministratorRole).Name.Should().Be("Harbor master");
    }

    [Fact]
    public void The_package_ships_the_default_administrators_texts_in_english_and_dutch()
    {
        var resources = new ResourceManager("DDDToolkit.Supporting.Tenancy.TenancyPackTexts", typeof(TenancyFailures).Assembly);
        Dictionary<string, string> Resx(CultureInfo culture)
            => resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
                .Cast<DictionaryEntry>()
                .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);

        var english = Resx(CultureInfo.InvariantCulture);
        var dutch = Resx(Dutch);

        english.Should().Equal(new Dictionary<string, string>
        {
            ["administrator.name"] = TenancyPacks.DefaultAdministrators.Name,
            ["administrator.description"] = TenancyPacks.DefaultAdministrators.Description,
        }, "the English texts are the pack's own, as the catalogue declares it");
        dutch.Keys.Should().BeEquivalentTo(english.Keys, "every text has a Dutch one under the same name");
        dutch.Values.Should().OnlyContain(text => !string.IsNullOrWhiteSpace(text)).And.NotIntersectWith(english.Values);
    }

    /// <summary>Pack texts from a table, by language and pack: what an application reads from its own resources.</summary>
    private sealed class PackTexts : IRolePackTexts
    {
        private readonly Dictionary<(string Language, string Pack), (string Name, string Description)> _texts = [];
        private readonly List<(string Pack, string Culture)> _asked = [];

        /// <summary>Every pack the use cases asked for, with the culture they asked in.</summary>
        public IReadOnlyList<(string Pack, string Culture)> Asked => _asked;

        /// <summary>Sets the texts of a pack in a language, replacing what was there.</summary>
        public PackTexts In(string language, string pack, string name, string description)
        {
            _texts[(language, pack)] = (name, description);
            return this;
        }

        /// <inheritdoc />
        public (string Name, string Description)? For(RolePack pack, CultureInfo culture)
        {
            _asked.Add((pack.Key, culture.Name));
            return _texts.TryGetValue((culture.TwoLetterISOLanguageName, pack.Key), out var texts) ? texts : null;
        }
    }
}
