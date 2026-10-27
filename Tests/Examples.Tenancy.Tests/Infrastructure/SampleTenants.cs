using DDDToolkit.Supporting.Tenancy;
using Examples.Tenancy.Projects.Application.Access;
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
    /// administrator's seat and the roles of its packs, and nothing else. The ids are made here, as seeding makes them,
    /// since the tenant manager answers the tenant alone.
    /// </summary>
    /// <param name="host">The host, started.</param>
    /// <param name="slug">The new tenant's slug.</param>
    /// <param name="name">Its name, and its root's.</param>
    /// <returns>The ids of the tenant, its root and its administrator's seat.</returns>
    public static async Task<(TenantId Tenant, OrganizationUnitId RootUnit, SeatId AdminSeat)> ProvisionAsync(SampleFactory host, string slug, string name)
    {
        ArgumentNullException.ThrowIfNull(host);
        var root = OrganizationUnitId.Create();
        var administrator = SeatId.Create();
        using (SampleAccess.Manager.BeginSystem())
        {
            await using var scope = host.Services.CreateAsyncScope();
            var tenant = await scope.ServiceProvider.GetRequiredService<TenantManager>().ProvisionAsync(
                new TenantToProvision(
                    slug,
                    name,
                    TenantShape.Flat,
                    name,
                    Guid.NewGuid(),
                    RootId: root,
                    AdminSeatId: administrator,
                    ConfigureRoot: unit => unit.SetKind(DemoTenant.RootKind),
                    ConfigureFirstSeat: seat => seat.Rename("Its administrator")),
                Cancellation);
            return (tenant.Id, root, administrator);
        }
    }

    /// <summary>
    /// Sends <paramref name="command"/> as the Projects module's system work in <paramref name="tenant"/>, in that
    /// module's own scope, which the projects' rules let through; in a service scope of its own.
    /// </summary>
    public static async Task<TResponse> SendAsSystemAsync<TResponse>(SampleFactory host, TenantId tenant, ICommand<TResponse> command)
    {
        ArgumentNullException.ThrowIfNull(host);
        using (SampleAccess.Manager.BeginSystemIn(tenant, scope: ProjectMembership.Scope))
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Cancellation);
        }
    }

    /// <inheritdoc cref="SendAsSystemAsync{TResponse}(SampleFactory, TenantId, ICommand{TResponse})"/>
    public static async Task SendAsSystemAsync(SampleFactory host, TenantId tenant, ICommand command)
    {
        ArgumentNullException.ThrowIfNull(host);
        using (SampleAccess.Manager.BeginSystemIn(tenant, scope: ProjectMembership.Scope))
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Cancellation);
        }
    }

    /// <summary>Asks <paramref name="query"/> as the Projects module's system work in <paramref name="tenant"/>, in a service scope of its own.</summary>
    public static async Task<TResponse> AskAsSystemAsync<TResponse>(SampleFactory host, TenantId tenant, IQuery<TResponse> query)
    {
        ArgumentNullException.ThrowIfNull(host);
        using (SampleAccess.Manager.BeginSystemIn(tenant, scope: ProjectMembership.Scope))
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(query, Cancellation);
        }
    }
}
