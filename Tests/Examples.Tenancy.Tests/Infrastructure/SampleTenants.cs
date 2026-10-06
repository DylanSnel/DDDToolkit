using DDDToolkit.Supporting.Tenancy;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// Tenants made while a host runs, the way whatever makes them in an application would: provisioned as system work
/// outside any tenant, and then sent the commands that set them up as system work in each.
/// </summary>
public static class SampleTenants
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// Provisions a flat tenant with an administrator whose person has no sign-in here: the tenant, its root, its
    /// administrator's seat and the roles of its packs, and nothing else.
    /// </summary>
    /// <param name="host">The host, started.</param>
    /// <param name="slug">The new tenant's slug.</param>
    /// <param name="name">Its name, and its root's.</param>
    public static async Task<TenantsTenancy.ProvisionedTenant> ProvisionAsync(SampleFactory host, string slug, string name)
    {
        ArgumentNullException.ThrowIfNull(host);
        using (TenantsTenancy.BeginSystem())
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TenantsTenancy.TenantCommands>().ProvisionAsync(
                new TenantsTenancy.TenantToProvision(
                    slug,
                    name,
                    TenantShape.Flat,
                    name,
                    Guid.NewGuid(),
                    ConfigureRoot: root => root.SetKind(DemoTenant.RootKind),
                    ConfigureFirstSeat: administrator => administrator.Rename("Its administrator")),
                Cancellation);
        }
    }

    /// <summary>Sends <paramref name="command"/> as system work in <paramref name="tenant"/>, in a scope of its own.</summary>
    public static async Task<TResponse> SendAsSystemAsync<TResponse>(SampleFactory host, TenantId tenant, ICommand<TResponse> command)
    {
        ArgumentNullException.ThrowIfNull(host);
        using (TenantsTenancy.BeginSystemIn(tenant))
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Cancellation);
        }
    }

    /// <inheritdoc cref="SendAsSystemAsync{TResponse}(SampleFactory, TenantId, ICommand{TResponse})"/>
    public static async Task SendAsSystemAsync(SampleFactory host, TenantId tenant, ICommand command)
    {
        ArgumentNullException.ThrowIfNull(host);
        using (TenantsTenancy.BeginSystemIn(tenant))
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Cancellation);
        }
    }

    /// <summary>Asks <paramref name="query"/> as system work in <paramref name="tenant"/>, in a scope of its own.</summary>
    public static async Task<TResponse> AskAsSystemAsync<TResponse>(SampleFactory host, TenantId tenant, IQuery<TResponse> query)
    {
        ArgumentNullException.ThrowIfNull(host);
        using (TenantsTenancy.BeginSystemIn(tenant))
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(query, Cancellation);
        }
    }
}
