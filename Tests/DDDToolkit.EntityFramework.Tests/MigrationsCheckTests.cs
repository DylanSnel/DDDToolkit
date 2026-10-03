using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The start-up check that Supabase applied every migration, in a host that requires explicit callers and
/// runs the module's context under row level security: it reads the migration history as the application
/// itself, whatever the host requires of other work.
/// </summary>
public sealed class MigrationsCheckTests(ExplicitCallersPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_migrations_check_runs_with_explicit_callers_required()
    {
        var connectionString = await postgres.CreateDatabaseAsync(Cancellation);
        await using (var owner = SupabaseShelfContext.Create(connectionString))
        {
            await owner.Database.MigrateAsync(Cancellation);
        }

        await using var host = new ServiceCollection()
            .AddPostgresRowLevelSecurity()
            .RequireExplicitCallers()
            .AddDbContext<SupabaseShelfContext>((provider, options) => options
                .UseNpgsql(connectionString)
                .UsePostgresRowLevelSecurity(provider))
            .AddSupabaseMigrations(SupabaseMigrationSource.For(() => SupabaseShelfContext.Create()))
            .BuildServiceProvider();

        await using (var scope = host.CreateAsyncScope())
        {
            var ask = () => scope.ServiceProvider.GetRequiredService<SupabaseShelfContext>().Database.GetPendingMigrationsAsync(Cancellation);
            await ask.Should().ThrowAsync<NoCallerException>("asked by anybody else's code, the history is that code's to say who reads it");
        }

        var check = () => host.EnsureSupabaseMigrationsAppliedAsync(Cancellation);

        await check.Should().NotThrowAsync("the check says itself that it is the application's own work");
    }
}
