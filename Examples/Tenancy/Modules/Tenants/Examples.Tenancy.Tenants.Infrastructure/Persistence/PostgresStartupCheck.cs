using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Supporting.Tenancy.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Examples.Tenancy.Tenants.Infrastructure.Persistence;

/// <summary>
/// Checks, before anything else of the host starts, that the database is the one the host was built for and is
/// set up as its row level security relies on. A host that would run with a hole in that lock does not start,
/// and the failure names what is wrong and the statement that puts it right.
/// </summary>
/// <remarks>
/// The module registers it before <see cref="TenancyStartupCheck"/>, always: there is no host of the sample
/// without the roles and the policies it checks. In order:
/// <list type="bullet">
/// <item><b>The role the host logs in as may become every caller.</b> It may switch to the role of a signed-in user,
/// of a caller without a token, of the operators' token role, of system work in a tenant and of the bookkeeping, as
/// the migration the export writes for it grants. First, and asked as that role itself: every check after it runs
/// as the system caller, which switches to the bookkeeping role, and a login role that may not would fail there
/// without saying why.</item>
/// <item><b>Every migration is applied.</b> The host applies none: whoever owns the database does, from the files
/// the export writes. A host started against a database that misses one stops here, naming it.</item>
/// <item><b>The host says who is calling.</b> Every flow of work names its caller, and a token role Tenancy seats
/// reaches the database as a signed-in user.</item>
/// <item><b>Every context</b> the host registered, not a list of them: it runs its commands as the caller, the role
/// the host logs in as owns nothing and holds no privilege in the context's schemas, and every function there
/// that runs as its owner is owned by a role the forced policies let through.</item>
/// <item><b>Tenancy's second lock.</b> System work in a tenant cannot leave it, the few reads across tenants
/// answer without running past the policies, and the policies, the functions and the unique index on a tenant's
/// root are in place as the model and the options say.</item>
/// </list>
/// The checks are the application's own bookkeeping, so they run as the system caller, on the connections kept
/// for the background. They read the database's catalogs and no tenant's rows.
/// </remarks>
/// <param name="services">The host's services.</param>
public sealed class PostgresStartupCheck(IServiceProvider services) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var system = Callers.Begin(Caller.System);

        await using (var scope = services.CreateAsyncScope())
        {
            foreach (var contextType in EntityFrameworkChecks.RegisteredContexts(scope.ServiceProvider))
            {
                await PostgresRowAccessChecks.EnsureLoginRoleMaySwitchToCallersAsync((DbContext)scope.ServiceProvider.GetRequiredService(contextType), cancellationToken);
            }
        }

        await services.EnsureSupabaseMigrationsAppliedAsync(cancellationToken);

        TenancyPostgresChecks.EnsureExplicitCallers(services);
        TenancyPostgresChecks.EnsureSeatedTokenRolesAreSignedInUsers(services);

        await using (var scope = services.CreateAsyncScope())
        {
            foreach (var contextType in EntityFrameworkChecks.RegisteredContexts(scope.ServiceProvider))
            {
                var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
                PostgresRowAccessChecks.EnsureRowLevelSecurityWired(context);
                await PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(context, cancellationToken);
                await PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(context, cancellationToken);
            }
        }

        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(services, cancellationToken);
        await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services, cancellationToken);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
