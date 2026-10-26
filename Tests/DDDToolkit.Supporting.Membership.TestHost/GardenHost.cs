using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.TestHost.Gardens;
using DDDToolkit.Supporting.Membership.TestHost.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Supporting.Membership.TestHost;

/// <summary>
/// The registration of the plots and the sheds, kept in <see cref="GardenContext"/>. A resource whose roles
/// are kept is registered like any other, with the one call named after it: its rules say the roles are kept,
/// the context maps the role class, and nothing else is registered for the roles. Two such resources in one
/// project are two calls.
/// </summary>
public static class GardenHost
{
    /// <summary>Registers the plots and the sheds, and the host's own use case that gives a garden its first roles.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="sheds">The rules the sheds are registered with; the host's own when left out.</param>
    public static IServiceCollection Add(IServiceCollection services, MembershipRules? sheds = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPlotMembership<GardenContext>(PlotMembership.Rules);
        services.AddShedMembership<GardenContext>(sheds ?? ShedMembership.Rules);
        services.TryAddScoped<GardenRoles>();

        return services;
    }
}
