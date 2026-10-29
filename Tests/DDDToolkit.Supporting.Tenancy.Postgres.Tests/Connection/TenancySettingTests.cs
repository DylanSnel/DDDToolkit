using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.Postgres;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The tenant of the current Tenancy user travels to Postgres as a setting next to the caller's role and claims,
/// whatever the toolkit's current user is, and is empty for a caller that acts in no tenant.
/// </summary>
public sealed class TenancySettingTests(TenancyPostgres postgres) : IDisposable
{
    private static readonly TenantId Harbor = new(42);
    private static readonly SeatId Ada = SeatId.CreateSequential();

    private readonly ServiceProvider _services = new ServiceCollection()
        .AddPostgresRowLevelSecurity()
        .AddTenancyPostgres()
        .BuildServiceProvider();

    private IRowLevelSecuritySettings Setting => _services.GetServices<IRowLevelSecuritySettings>().Should().ContainSingle().Subject;

    public void Dispose() => _services.Dispose();

    [Fact]
    public void The_tenant_setting_follows_the_tenancy_caller()
    {
        Setting.Names.Should().Equal(TenancyRowLevelSecurity.TenantSetting).And.Equal("tenancy.current_tenant");

        using (TenancyUsers.Begin(HostUser.InSeat(Harbor, Ada)))
        {
            Values(CurrentUserSnapshot.User(Guid.NewGuid())).Should().Equal(Pair("42"));

            // The toolkit's current user changes nothing: which role may reach what with it is for the SQL to decide.
            Values(CurrentUserSnapshot.System).Should().Equal(Pair("42"));

            // But for an anonymous one. A signed-in user whose token role is on no list, in a host that has such a
            // caller run as an anonymous one, is asked for as anonymous: the database is shown no user, and no
            // tenant either, though the application found the person a seat.
            Values(CurrentUserSnapshot.Anonymous).Should().BeEmpty();
        }

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(new TenantId(7)))
        {
            Values(BegunUser!).Should().Equal(Pair("7"));
        }

        // A tenant id over a Guid is written as its D form, whatever the culture.
        var tenant = new GuidTenant(Guid.Parse("a0000000-0000-4000-8000-000000000001"));
        using (TenancyUsers.Begin(TenancyUser<GuidTenant, SeatId>.SystemIn(tenant)))
        {
            Values(CurrentUserSnapshot.SystemIn("projects")).Should().Equal(Pair("a0000000-0000-4000-8000-000000000001"));
        }
    }

    [Fact]
    public void No_tenancy_caller_is_an_empty_setting()
    {
        // Left out, which the interceptor sets to ''.
        Values(CurrentUserSnapshot.Anonymous).Should().BeEmpty();

        using (TenancyUsers.Begin(HostUser.Nobody(TenancyRefusals.NotSeated)))
        {
            Values(CurrentUserSnapshot.User(Guid.NewGuid())).Should().BeEmpty("nobody acts in no tenant");
        }

        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            Values(BegunUser!).Should().BeEmpty("system work outside any tenant acts in none");
        }
    }

    [Fact]
    public void AddTenancyPostgres_requires_explicit_callers_and_the_interceptor_takes_the_setting()
    {
        _services.GetRequiredService<CurrentUserOptions>().RequireExplicitCurrentUser.Should().BeTrue();

        // The interceptor checks every setting's name when it is built.
        _services.GetRequiredService<PostgresRowLevelSecurityInterceptor>().Should().NotBeNull();

        // Twice is once.
        using var twice = new ServiceCollection().AddPostgresRowLevelSecurity().AddTenancyPostgres().AddTenancyPostgres().BuildServiceProvider();
        twice.GetServices<IRowLevelSecuritySettings>().Should().ContainSingle();
        twice.GetServices<ITenancySaveFailures>().Should().ContainSingle("what Postgres refuses is translated once");
        twice.GetServices<CurrentUserOptions>().Should().ContainSingle().Which.RequireExplicitCurrentUser.Should().BeTrue();
        twice.GetRequiredService<PostgresRowLevelSecurityInterceptor>().Should().NotBeNull();
    }

    [Fact]
    public async Task A_long_tenant_id_casts()
    {
        // Past what a double holds exactly: a value that went through one on its way would come back changed.
        var lagoon = new TenantId(9_007_199_254_740_993);
        var identity = Guid.NewGuid();
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, TestContext.Current.CancellationToken);
        await using var services = new TenancyServices(database);

        // Provisioned under the policies, as system work in the tenant it makes: its id travels in the setting.
        ProvisionedTenant provisioned;
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            provisioned = await services.InScopeAsync(scoped => scoped.Tenants().ProvisionWithIdsAsync(scoped.GetRequiredService<TenancyCatalogue>(),
                new TenantToProvision("lagoon", "Lagoon Works", TenantShape.Flat, "Lagoon", identity, TenantId: lagoon),
                TestContext.Current.CancellationToken));
        }

        provisioned.Tenant.Should().Be(lagoon);
        using (TenancyUsers.Begin(HostUser.InSeat(lagoon, provisioned.AdminSeat)))
        {
            Values(CurrentUserSnapshot.User(identity)).Should().Equal(Pair("9007199254740993"));
        }

        await using (var system = await AsUser.SystemInAsync(database, lagoon, TenancyWork.SystemScope, TestContext.Current.CancellationToken))
        {
            (await system.ScalarAsync<long?>("SELECT tenancy.system_tenant()", TestContext.Current.CancellationToken)).Should().Be(lagoon.Value);
            (await system.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Seats\"", TestContext.Current.CancellationToken)).Should().Be(1);
        }

        await using var administrator = await AsUser.PersonAsync(database, identity, lagoon, TestContext.Current.CancellationToken);
        (await administrator.ScalarAsync<long?>("SELECT tenancy.current_tenant()", TestContext.Current.CancellationToken)).Should().Be(lagoon.Value);
        (await administrator.ScalarAsync<Guid?>("SELECT tenancy.current_seat()", TestContext.Current.CancellationToken)).Should().Be(provisioned.AdminSeat.Value);
    }

    private static string Pair(string value) => TenancyRowLevelSecurity.TenantSetting + "=" + value;

    /// <summary>The settings the provider gives <paramref name="caller"/>, each as <c>name=value</c>.</summary>
    private List<string> Values(CurrentUserSnapshot caller) => [.. Setting.For(caller).Select(setting => setting.Key + "=" + setting.Value)];

    /// <summary>A tenant id over a <see cref="Guid"/>, as the sample's is.</summary>
    private readonly record struct GuidTenant(Guid Value) : IEntityId<Guid>;
}
