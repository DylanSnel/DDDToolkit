using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// One context whose migrations Supabase applies: how to build it without a host, for the export, and
/// how to find it in a running application, for the start-up check.
/// <para>
/// You rarely build one yourself. The build writes one per <c>[SupabaseMigrations]</c> factory into the
/// project that turns the export on, and <c>services.AddSupabaseMigrations&lt;TContext, TFactory&gt;()</c>
/// builds one for the start-up check. Build one by hand to export from a test or a tool of your own. The
/// export needs nothing but the design-time factory <c>dotnet ef</c> already uses, so it runs without the
/// host: no configuration, no Azure App Configuration, no hosted services.
/// </para>
/// <code>
/// var ordering = SupabaseMigrationSource.For&lt;OrderingContext, OrderingContextFactory&gt;();
/// SupabaseMigrations.EnsureInSync([ordering]);
/// </code>
/// <para>
/// Both ways in are generic, so a source is built without reflection: the factory is created with
/// <c>new TFactory()</c> and the context resolved as <c>TContext</c>.
/// </para>
/// </summary>
public sealed class SupabaseMigrationSource
{
    private readonly Func<DbContext> _createDesignTime;
    private readonly Func<IServiceProvider, DbContext> _resolve;

    private SupabaseMigrationSource(Type contextType, string? module, Func<DbContext> createDesignTime, Func<IServiceProvider, DbContext> resolve)
    {
        ContextType = contextType;
        Module = module is null ? SupabaseMigrations.ModuleNameOf(contextType) : SupabaseMigrations.NormalizeModuleName(module);
        _createDesignTime = createDesignTime;
        _resolve = resolve;
    }

    /// <summary>The context whose migrations this is.</summary>
    public Type ContextType { get; }

    /// <summary>
    /// The module the files are named after, as in <c>20260922120000_AddOrders.ordering.ddd.sql</c>. The
    /// one given, or else <see cref="SupabaseMigrations.ModuleNameOf"/> the context.
    /// </summary>
    public string Module { get; }

    /// <summary>
    /// The context <typeparamref name="TContext"/>, built for the export by <typeparamref name="TFactory"/>:
    /// the same design-time factory <c>dotnet ef migrations add</c> uses, which configures Npgsql with a
    /// connection string that points nowhere.
    /// </summary>
    /// <param name="module">The module the files are named after, or <see langword="null"/> to take it from the context's assembly.</param>
    public static SupabaseMigrationSource For<TContext, TFactory>(string? module = null)
        where TContext : DbContext
        where TFactory : IDesignTimeDbContextFactory<TContext>, new()
        => new(typeof(TContext), module, static () => new TFactory().CreateDbContext([]), static services => services.GetRequiredService<TContext>());

    /// <summary>
    /// The context <typeparamref name="TContext"/>, built for the export by
    /// <paramref name="createDesignTime"/>, for a context without a design-time factory.
    /// </summary>
    /// <param name="createDesignTime">Builds a context on Npgsql; it is never opened.</param>
    /// <param name="module">The module the files are named after, or <see langword="null"/> to take it from the context's assembly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="createDesignTime"/> is null.</exception>
    public static SupabaseMigrationSource For<TContext>(Func<TContext> createDesignTime, string? module = null) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(createDesignTime);
        return new(typeof(TContext), module, createDesignTime, static services => services.GetRequiredService<TContext>());
    }

    /// <summary>A new context for the export. The caller disposes it; it is never opened.</summary>
    public DbContext CreateDesignTimeContext() => _createDesignTime();

    /// <summary>The application's context from <paramref name="services"/>, for the start-up check.</summary>
    internal DbContext Resolve(IServiceProvider services) => _resolve(services);

    /// <inheritdoc />
    public override string ToString() => ContextType.Name;
}
