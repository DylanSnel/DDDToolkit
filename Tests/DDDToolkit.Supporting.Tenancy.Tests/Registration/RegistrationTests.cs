using DDDToolkit.Abstractions.Access;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Registration needs no option, since every one has a default and each id makes a new one of itself; it builds
/// the catalogue once from the application's part, when it has one, and every module's contribution, and leaves
/// only the store to the storage package.
/// </summary>
public class RegistrationTests
{
    private static IServiceCollection AddTenancyCore(IServiceCollection services, Action<TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId>>? configure)
        => services.AddTenancyCore<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>(configure);

    private static void EveryOption(TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId> options)
        => options.Catalogue = HostCatalogue.Application;

    [Fact]
    public async Task AddTenancyCore_needs_no_option_and_the_ids_make_new_ones_of_themselves()
    {
        var services = AddTenancyCore(new ServiceCollection(), configure: null);
        services.AddSingleton(provider => new InMemoryTenancyStore(provider.GetRequiredService<TenancyCatalogue>()));
        services.AddSingleton<HostTenancy.IStore>(provider => provider.GetRequiredService<InMemoryTenancyStore>());
        services.AddScoped<ISeatDirectory<TenantId, SeatId>, ListedSeats>();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        provider.GetRequiredService<TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId>>().Catalogue
            .Should().BeNull("the catalogue has a default: the application adds nothing to it");

        await using var scope = provider.CreateAsyncScope();
        var store = provider.GetRequiredService<InMemoryTenancyStore>();
        HostTenancy.ProvisionedTenant provisioned;
        using (TenancyCallers.Begin(HostCaller.System))
        {
            store.BeginUnitOfWork();
            provisioned = await scope.ServiceProvider.GetRequiredService<HostTenancy.TenantCommands>().ProvisionAsync(
                new HostTenancy.TenantToProvision("harbor", "Harbor", TenantShape.Flat, "Head office", Guid.NewGuid()),
                TestContext.Current.CancellationToken);
        }

        // TenantId.Create() of the host, a long, and the generator's Create() of the others, time-ordered Guids.
        provisioned.Tenant.Value.Should().BeGreaterThan(1_000_000, "the host's TenantId counts its own numbers");
        provisioned.AdminSeat.Value.Version.Should().Be(7);
        provisioned.RootUnit.Value.Version.Should().Be(7);
        provisioned.AdministratorRole.Value.Version.Should().Be(7);
        store.HasTenant(provisioned.Tenant).Should().BeTrue();
    }

    [Fact]
    public async Task The_token_roles_that_hold_seats_are_an_option_with_a_default()
    {
        var ada = Guid.NewGuid();
        var harbor = new TenantId(1);
        var seat = SeatId.CreateSequential();

        async Task<(HostCaller Member, HostCaller User)> ResolvedWith(Action<TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId>> configure)
        {
            var services = new ServiceCollection();
            AddTenancyCore(services, options =>
            {
                EveryOption(options);
                configure(options);
            });
            services.AddSingleton<ISeatDirectory<TenantId, SeatId>>(new ListedSeats().With(ada, harbor, "harbor", seat));
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var scope = provider.CreateAsyncScope();
            var selection = scope.ServiceProvider.GetRequiredService<TenantSelection<TenantId, SeatId>>();

            return (await selection.ResolveAsync(Caller.User(ada, "member"), "harbor", TestContext.Current.CancellationToken), await selection.ResolveAsync(Caller.User(ada), "harbor", TestContext.Current.CancellationToken));
        }

        // Left alone, the option seats signed-in users, as before there was one.
        (await ResolvedWith(_ => { })).Should().Be((HostCaller.Nobody(TenancyRefusals.NotSeated), HostCaller.InSeat(harbor, seat)));

        // What the host adds reaches the selection the services make.
        (await ResolvedWith(options => options.TenantSelection.SeatedTokenRoles.Add("member")))
            .Should().Be((HostCaller.InSeat(harbor, seat), HostCaller.InSeat(harbor, seat)));
    }

    [Fact]
    public void Contributions_add_up_in_the_built_catalogue()
    {
        var services = new ServiceCollection()
            .AddTenancyPermissions([new Permission("gauges.read", "Gauges", "Read gauges")])
            .AddTenancyPermissions([new Permission("valves.turn", "Valves", "Turn valves")]);
        AddTenancyCore(services, EveryOption);
        using var provider = services.BuildServiceProvider();

        var catalogue = provider.GetRequiredService<TenancyCatalogue>();

        catalogue.Knows("gauges.read").Should().BeTrue();
        catalogue.Knows("valves.turn").Should().BeTrue();
        catalogue.Knows(HostCatalogue.WidgetRead).Should().BeTrue();
        catalogue.Knows(TenancyKeys.RolesManage).Should().BeTrue();
        catalogue.Packs.Single(pack => pack.Administers).Keys.Should().Contain(["gauges.read", "valves.turn"]);
        provider.GetRequiredService<TenancyCatalogue>().Should().BeSameAs(catalogue, "it is built once");

        var clash = new ServiceCollection().AddTenancyPermissions([new Permission("tenancy.everything", "Stray", "All of it")]);
        AddTenancyCore(clash, EveryOption);
        using var broken = clash.BuildServiceProvider();
        FluentActions.Invoking(() => broken.GetRequiredService<TenancyCatalogue>()).Should().Throw<TenancyCatalogueException>();
    }

    [Fact]
    public void A_modules_list_added_by_the_host_and_by_the_module_as_well_stops_the_catalogue_with_what_to_remove()
    {
        // What an application upgrading to [TenancyPermissions] can leave behind: the host's generated
        // AddTenancyPermissionsOfModules() adds the module's list, and the module's own registration still adds it too.
        IReadOnlyList<Permission> gauges = [new Permission("gauges.read", "Gauges", "Read gauges"), new Permission("gauges.lock", "Gauges", "Lock gauges")];
        var services = new ServiceCollection().AddTenancyPermissions(gauges).AddTenancyPermissions(gauges);
        AddTenancyCore(services, EveryOption);
        using var provider = services.BuildServiceProvider();

        FluentActions.Invoking(() => provider.GetRequiredService<TenancyCatalogue>())
            .Should().Throw<TenancyCatalogueException>().Which.Problems.Should().ContainSingle().Which.Should()
            .Contain("'gauges.read', 'gauges.lock'", "one problem for the list, naming its keys")
            .And.Contain("AddTenancyPermissionsOfModules()").And.Contain("AddTenancyPermissions no more");
    }

    [Fact]
    public void An_application_without_a_catalogue_of_its_own_runs_on_its_modules_keys_and_the_default_administrators()
    {
        var services = new ServiceCollection().AddTenancyPermissions([new Permission("gauges.read", "Gauges", "Read gauges")]);
        AddTenancyCore(services, options =>
        {
            EveryOption(options);
            options.Catalogue = null;
        });
        using var provider = services.BuildServiceProvider();

        var catalogue = provider.GetRequiredService<TenancyCatalogue>();

        catalogue.LiveKeys.Should().BeEquivalentTo([.. TenancyKeys.Permissions.Select(permission => permission.Key), "gauges.read"]);
        catalogue.HasDefaultAdministrators.Should().BeTrue();
        catalogue.Packs.Should().ContainSingle().Which.Keys.Should().Equal(catalogue.LiveKeys);
    }

    [Fact]
    public void The_use_cases_resolve_once_a_store_is_registered()
    {
        var services = new ServiceCollection();
        var clock = new FixedClock();
        services.AddSingleton<TimeProvider>(clock);
        AddTenancyCore(services, EveryOption);
        services.AddScoped<HostTenancy.IStore>(provider => new InMemoryTenancyStore(provider.GetRequiredService<TenancyCatalogue>()));
        services.AddScoped<ISeatDirectory<TenantId, SeatId>, ListedSeats>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<HostTenancy.TenantCommands>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<HostTenancy.OrganizationCommands>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<HostTenancy.SeatCommands>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<HostTenancy.RoleCommands>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<HostTenancy.TenancyDirectory>().Catalogue.Knows(TenancyKeys.RolesManage).Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<TenantSelection<TenantId, SeatId>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ITenantSelection>().Should().BeSameAs(
            scope.ServiceProvider.GetRequiredService<TenantSelection<TenantId, SeatId>>(),
            "the selection without its ids is the scope's own selection, asked by another name");
        provider.GetRequiredService<ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>>()
            .Should().BeOfType<TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>>();
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(clock, "a clock registered before stays");
    }

    [Fact]
    public void A_selection_without_ids_the_host_registered_before_stays()
    {
        var services = new ServiceCollection();
        var own = new OwnSelection();
        services.AddScoped<ITenantSelection>(_ => own);
        AddTenancyCore(services, EveryOption);
        services.AddScoped<HostTenancy.IStore>(provider => new InMemoryTenancyStore(provider.GetRequiredService<TenancyCatalogue>()));
        services.AddScoped<ISeatDirectory<TenantId, SeatId>, ListedSeats>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITenantSelection>().Should().BeSameAs(own, "the host's own declaration wins");
        scope.ServiceProvider.GetRequiredService<TenantSelection<TenantId, SeatId>>().Should().NotBeNull("the selection closed over the ids is still there");
    }

    /// <summary>A host's own answer to who a request's caller is in Tenancy: here, always nobody.</summary>
    private sealed class OwnSelection : ITenantSelection
    {
        public Task<ITenancyCaller> ResolveAsync(Caller caller, string? tenantSlug, CancellationToken cancellationToken)
            => Task.FromResult<ITenancyCaller>(HostCaller.Nobody(TenancyRefusals.NotSeated));
    }
}
