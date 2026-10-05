using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Startup;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// <c>AddTenancyPostgres</c> brings Tenancy's start-up checks of the host and of the database, once however often it
/// is called, each in the stage that says when it can run; and the three that prove what the reads across tenants
/// rely on run before the read of the stored keys, whatever order Tenancy and this were registered in.
/// </summary>
public sealed class StartupCheckRegistrationTests
{
    [Fact]
    public void Tenancy_on_Postgres_brings_its_checks_in_the_order_a_host_runs_them()
    {
        // Tenancy first, so the read of the stored keys is registered before the checks it relies on.
        var services = new ServiceCollection();
        TestHostTenancy.Add(services);
        services.AddPostgresRowLevelSecurity();
        services.AddTenancyPostgres();
        services.AddTenancyPostgres();

        services.GetStartupChecks().InOrder().Select(check => (check.Name, check.Stage)).Should().Equal(
            (TenancyChecks.CatalogueBuildsCheck, StartupCheckStage.Services),
            (TenancyChecks.ContextsWiredCheck, StartupCheckStage.Services),
            (PostgresRowAccessChecks.RowLevelSecurityWiredCheck, StartupCheckStage.Services),
            (TenancyPostgresChecks.ExplicitCallersCheck, StartupCheckStage.Services),
            (TenancyPostgresChecks.SeatedTokenRolesCheck, StartupCheckStage.Services),
            (PostgresRowAccessChecks.LoginRoleMaySwitchToCallersCheck, StartupCheckStage.Login),
            (PostgresRowAccessChecks.LoginRoleOwnsNothingCheck, StartupCheckStage.Database),
            (PostgresRowAccessChecks.DefinerOwnersBypassCheck, StartupCheckStage.Database),
            (TenancyPostgresChecks.SystemInRoleConfinedCheck, StartupCheckStage.Database),
            (TenancyPostgresChecks.SystemReadsAcrossTenantsCheck, StartupCheckStage.Database),
            (TenancyPostgresChecks.PoliciesInPlaceCheck, StartupCheckStage.Database),
            (TenancyChecks.UnknownStoredKeysCheck, StartupCheckStage.Database));
    }
}
