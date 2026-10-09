using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.TestHost.Crates;
using DDDToolkit.Supporting.Membership.TestHost.Depot;
using DDDToolkit.Supporting.Membership.TestHost.Pallets;
using DDDToolkit.Supporting.Membership.TestHost.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Supporting.Membership.TestHost;

/// <summary>
/// The registration of the two kinds of resource that stand beside the depot, kept in <see cref="DepotContext"/>.
/// Each is registered with its rules and with the class that answers what those rules ask of the depot: the
/// registration generated for the resource takes that class next to the context, and is named after the
/// resource like any other.
/// </summary>
public static class DepotHost
{
    /// <summary>Registers the pallets and the crates, each with what the depot answers for it.</summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection Add(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Who the depot knows its callers as, read before a question is asked.
        services.TryAddSingleton<DepotDesk>();

        // A pallet: the depot says who a member is, and nothing else.
        services.AddPalletMembership<DepotContext, PalletsInTheDepot>(PalletMembership.Rules);

        // A crate: the depot says who a member is, which roles there are and what they give, and where a key is held.
        services.AddCrateMembership<DepotContext, CratesInTheDepot>(CrateMembership.Rules);

        return services;
    }
}
