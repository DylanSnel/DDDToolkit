using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// The application side: modules register the contexts Supabase migrates, and the host checks at start-up
/// that Supabase has applied all of them.
/// <code>
/// // in each module
/// services.AddSupabaseMigrations&lt;OrderingContext, OrderingContextFactory&gt;();
///
/// // in the host, after Build()
/// await app.Services.EnsureSupabaseMigrationsAppliedAsync();
/// </code>
/// And, for applications that want Supabase's row level security to apply to their own queries, the
/// registrations in <c>DependencyInjection.RowLevelSecurity.cs</c>.
/// </summary>
public static partial class DependencyInjection
{
    /// <summary>
    /// Registers <paramref name="source"/> as a context whose migrations Supabase applies, so
    /// <see cref="EnsureSupabaseMigrationsAppliedAsync"/> checks it. Registering the same context twice
    /// registers it once.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="source"/> is null.</exception>
    public static IServiceCollection AddSupabaseMigrations(this IServiceCollection services, SupabaseMigrationSource source)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(source);

        var registered = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SupabaseMigrationSource>()
            .Any(existing => existing.ContextType == source.ContextType);

        if (!registered)
        {
            services.AddSingleton(source);
        }

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/>, built for the export by <typeparamref name="TFactory"/>.
    /// Shorthand for <c>AddSupabaseMigrations(SupabaseMigrationSource.For&lt;TContext, TFactory&gt;())</c>.
    /// </summary>
    public static IServiceCollection AddSupabaseMigrations<TContext, TFactory>(this IServiceCollection services)
        where TContext : DbContext
        where TFactory : IDesignTimeDbContextFactory<TContext>, new()
        => services.AddSupabaseMigrations(SupabaseMigrationSource.For<TContext, TFactory>());

    /// <summary>The contexts registered with <see cref="AddSupabaseMigrations(IServiceCollection, SupabaseMigrationSource)"/>.</summary>
    public static IReadOnlyList<SupabaseMigrationSource> GetSupabaseMigrationSources(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return [.. services.GetServices<SupabaseMigrationSource>()];
    }

    /// <summary>
    /// Throws unless every registered context has every one of its migrations applied. Call it at
    /// start-up in place of <c>Database.Migrate()</c>: on Supabase the migrations are the CLI's to apply,
    /// and an application that applied them as well would make the CLI apply them a second time.
    /// <para>
    /// It asks each context's own migration history, so it opens one connection per context. That is the
    /// only database work it does.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="SupabaseMigrationsPendingException">A registered context has migrations the database does not.</exception>
    public static async Task EnsureSupabaseMigrationsAppliedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var pending = new List<(string Context, IReadOnlyList<string> Migrations)>();

        foreach (var source in services.GetSupabaseMigrationSources())
        {
            var context = source.Resolve(scope.ServiceProvider);
            var missing = (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();

            if (missing.Count > 0)
            {
                pending.Add((source.ContextType.Name, missing));
            }
        }

        if (pending.Count > 0)
        {
            throw new SupabaseMigrationsPendingException(pending);
        }
    }
}
