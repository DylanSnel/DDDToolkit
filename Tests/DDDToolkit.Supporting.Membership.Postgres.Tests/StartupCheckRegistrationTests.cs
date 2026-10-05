using DDDToolkit.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// <c>AddMembershipPostgres</c> brings the package's start-up check, the functions of every resource's membership,
/// with the checks of the database, once however often it is called; a host runs it with its other checks.
/// </summary>
public sealed class StartupCheckRegistrationTests
{
    [Fact]
    public void AddMembershipPostgres_brings_the_check_of_the_functions_once()
    {
        var services = new ServiceCollection().AddMembershipPostgres().AddMembershipPostgres();

        services.GetStartupChecks().Registered.Select(check => (check.Name, check.Stage, check.OnByDefault))
            .Should().Equal((MembershipPostgresChecks.FunctionsInPlaceCheck, StartupCheckStage.Database, false));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IHostedService), "it runs once the host asks for its checks");
    }
}
