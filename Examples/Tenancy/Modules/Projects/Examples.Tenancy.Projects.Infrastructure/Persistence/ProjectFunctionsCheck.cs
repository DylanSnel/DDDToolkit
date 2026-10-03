using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Membership.Postgres;
using Microsoft.Extensions.Hosting;

namespace Examples.Tenancy.Projects.Infrastructure.Persistence;

/// <summary>
/// Checks, before the host takes a request, that the database answers the projects' membership as the projects'
/// rules say it: the four functions the policies of this module and of others ask, written from the rules the
/// projects are registered with, and the lock on the crew's tables and on a project's owner.
/// </summary>
/// <remarks>
/// The Membership package's own check. A database written from other rules, or missing a function or the lock,
/// would have the policies answer otherwise than the application's own checks, so the host does not start, and
/// the failure says what to apply. Registered after the Tenants module's checks, which have found every
/// migration applied. It reads the database's catalogs and no tenant's rows, as the application's own
/// bookkeeping.
/// </remarks>
/// <param name="services">The host's services.</param>
public sealed class ProjectFunctionsCheck(IServiceProvider services) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var system = Callers.Begin(Caller.System);
        await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(services, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
