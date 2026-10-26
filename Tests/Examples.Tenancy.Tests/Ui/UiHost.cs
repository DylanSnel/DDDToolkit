using Microsoft.Extensions.Hosting;

namespace Examples.Tenancy.Tests.Ui;

/// <summary>
/// The builder of a host that registers what the UI registers, for a test that resolves the UI's services without its
/// pages: the service defaults, then <c>AddSampleUi()</c>, as the UI's <c>Program</c> calls them.
/// </summary>
internal static class UiHost
{
    /// <summary>
    /// A host builder in Production, the environment of a host started with nothing set, named here rather than read
    /// from the process. Every test of the run shares the process, and Entity Framework's design-time services, which
    /// <c>MigrationTests</c> runs as <c>dotnet ef</c> does, set <c>DOTNET_ENVIRONMENT</c> to Development while they run.
    /// A builder that read it then checked every registration when it was built, and failed on the UI's session store,
    /// which asks for the browser's session storage that only the UI's Razor components register.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder()
        => Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
}
