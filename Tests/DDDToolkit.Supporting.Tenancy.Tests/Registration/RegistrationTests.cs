using DDDToolkit.Abstractions.Access;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Registration asks for the ways to make each id up front and names what is missing, builds the catalogue once
/// from the application's part, when it has one, and every module's contribution, and leaves only the store to
/// the storage package.
/// </summary>
public class RegistrationTests
{
    private static IServiceCollection AddTenancyCore(IServiceCollection services, Action<TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId>> configure)
        => services.AddTenancyCore<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>(configure);

    private static void EveryOption(TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId> options)
    {
        options.Catalogue = HostCatalogue.Application;
        options.NewTenantId = () => new TenantId(1);
        options.NewSeatId = SeatId.CreateSequential;
        options.NewUnitId = OrganizationUnitId.CreateSequential;
        options.NewRoleId = RoleId.CreateSequential;
    }

    [Fact]
    public void AddTenancyCore_names_every_missing_option()
    {
        var missing = FluentActions.Invoking(() => AddTenancyCore(new ServiceCollection(), _ => { }))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*NewTenantId, NewSeatId, NewUnitId, NewRoleId*")
            .Which.Message;
        missing.Should().NotContain("Catalogue", "the catalogue has a default: the application adds nothing to it");

        FluentActions.Invoking(() => AddTenancyCore(new ServiceCollection(), options =>
            {
                EveryOption(options);
                options.NewUnitId = null;
            }))
            .Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("NewUnitId").And.NotContain("NewSeatId");
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
        provider.GetRequiredService<ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>>()
            .Should().BeOfType<TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>>();
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(clock, "a clock registered before stays");
    }
}
