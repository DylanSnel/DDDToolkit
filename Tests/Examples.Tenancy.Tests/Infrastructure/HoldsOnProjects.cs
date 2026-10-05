using DDDToolkit.Supporting.Membership.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>The expert hold, switched on for a sample host's projects as a host would: one line on Projects' context.</summary>
public static class HoldsOnProjects
{
    /// <summary>
    /// Adds <c>UseMemberHolds</c> to the options of Projects' context. The sample's context pools apply what is
    /// configured for a context after the module's own chain, as Entity Framework's own registrations do.
    /// </summary>
    public static IServiceCollection HoldProjectSaves(this IServiceCollection services)
        => services.ConfigureDbContext<ProjectsContext>((application, options) => options.UseMemberHolds(application));
}
