using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// The application side: the host registers the contexts Supabase migrates, and checks at start-up that Supabase has
/// applied all of them.
/// <code>
/// // in the host: every context marked [SupabaseMigrations] it references, in one call the build writes into it, in
/// // the namespace named after the host's assembly, which a top-level Program.cs imports
/// using Shop.Host;
///
/// builder.Services.AddSupabaseMigrations();
///
/// // the check runs with every other start-up check, before the server binds its port
/// builder.Services.RunStartupChecks();
/// </code>
/// <c>AddSupabaseMigrations()</c> is not declared here: the package's generator writes it into every application that
/// is not a test project, with the list of the marked contexts the application references, each with its design-time
/// factory. It calls <see cref="AddSupabaseMigrations(IServiceCollection, SupabaseMigrationSource)"/> for each, which a
/// host that registers some contexts and not others calls itself, or <see cref="AddSupabaseMigrations{TContext, TFactory}"/>
/// with the factory the build wrote beside the context:
/// <c>services.AddSupabaseMigrations&lt;OrderingContext, OrderingContextDesignTimeFactory&gt;()</c>.
/// A host that runs its checks by hand calls <see cref="EnsureSupabaseMigrationsAppliedAsync"/> after <c>Build()</c>
/// instead, as before.
/// And, for applications that want Supabase's row level security to apply to their own queries, the
/// registrations in <c>DependencyInjection.RowLevelSecurity.cs</c>.
/// </summary>
public static partial class DependencyInjection
{
    /// <summary>
    /// Registers <paramref name="source"/> as a context whose migrations Supabase applies, so
    /// <see cref="EnsureSupabaseMigrationsAppliedAsync"/> checks it. Registering the same context twice
    /// registers it once.
    /// <para>
    /// It brings the start-up check <see cref="SupabaseMigrations.AppliedCheck"/>, once however many contexts are
    /// registered, which a host runs with <c>services.RunStartupChecks()</c>.
    /// </para>
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

        services.AddStartupCheck(new StartupCheck(
            SupabaseMigrations.AppliedCheck,
            StartupCheckStage.Migrations,
            static (provider, cancellationToken) => provider.EnsureSupabaseMigrationsAppliedAsync(cancellationToken)));

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
    /// only database work it does, and it does it as the application itself, <see cref="Caller.System"/>,
    /// whatever caller is current: the history is the application's to read, also on a context with row
    /// level security in a host that requires explicit callers.
    /// </para>
    /// <para>
    /// Where a context misses migrations, it also asks whether the context reads its history from the table its
    /// design-time factory's context records the migrations in, which is where every exported file records its
    /// migration. Where the two differ, the migrations may well be applied, and recorded where the application does
    /// not look, and the exception says so: the options of the factory and those of the application name the history
    /// differently, <c>UseDDDToolkit</c> in the one without <c>UseDDDToolkitDesignTime</c> in the other, say.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A registered context is not one <paramref name="services"/> resolve.</exception>
    /// <exception cref="SupabaseMigrationsPendingException">A registered context has migrations the database does not.</exception>
    public static async Task EnsureSupabaseMigrationsAppliedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        using var system = Callers.Begin(Caller.System);
        var pending = new List<(string Context, IReadOnlyList<string> Migrations)>();
        var elsewhere = new List<string>();
        var sources = services.GetSupabaseMigrationSources();

        // A context the check cannot ask is a registration that does not match the container, which is said in the
        // toolkit's words before any connection is opened, rather than in the container's.
        var unregistered = sources.Where(source => source.Resolve(scope.ServiceProvider) is null).Select(static source => source.ContextType).ToList();
        if (unregistered.Count > 0)
        {
            throw new InvalidOperationException(Unregistered(unregistered));
        }

        foreach (var source in sources)
        {
            var context = source.Resolve(scope.ServiceProvider)!;
            var missing = (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();

            if (missing.Count > 0)
            {
                pending.Add((source.ContextType.Name, missing));
                if (SupabaseMigrationsPendingException.HistoryElsewhere(source, context) is { } said)
                {
                    elsewhere.Add(said);
                }
            }
        }

        if (pending.Count > 0)
        {
            throw new SupabaseMigrationsPendingException(pending, elsewhere);
        }
    }

    /// <summary>
    /// What the check says of contexts registered as Supabase's that the application's services cannot resolve: most
    /// likely a context <c>AddSupabaseMigrations()</c> found in a project the host references and does not use, since
    /// the generated list follows the references, not the registrations.
    /// </summary>
    private static string Unregistered(IReadOnlyList<Type> contexts)
    {
        var named = string.Join(", ", contexts.Select(static context => "'" + context.FullName + "'"));
        return $"The check that Supabase applied every migration asks each registered context for its history, and the application's services do not register {named}. "
               + "AddSupabaseMigrations() registers every context marked [SupabaseMigrations] in the projects the application references, whether the application uses it or not. "
               + "Register the context where the application uses it, or register only the contexts it uses, one by one, with AddSupabaseMigrations<TContext, TFactory>() "
               + "in place of AddSupabaseMigrations().";
    }
}
