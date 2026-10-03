using System.Data.Common;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>Registers row level security for the contexts that should run under the caller's role.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="PostgresRowLevelSecurityInterceptor"/>. Add it to each context that should run
    /// under the caller's role with <see cref="UsePostgresRowLevelSecurity"/>.
    /// <code>
    /// services.AddPostgresRowLevelSecurity();
    /// services.AddDbContext&lt;OrderingContext&gt;((provider, options) => options
    ///     .UseNpgsql(connectionString)
    ///     .UsePostgresRowLevelSecurity(provider));
    /// </code>
    /// Who is calling is whatever a host registered: in ASP.NET Core, the request's user, once
    /// <c>AddSupabaseJwtBearer</c> from <c>DDDToolkit.Auth.Supabase.AspNetCore</c> is there. Without one,
    /// it is the caller <see cref="Callers.Begin"/> made current, or <see cref="Caller.System"/> outside any
    /// (<see cref="AmbientCallerAccessor"/>), which is what an Azure Function, a worker service or a test wants.
    /// With <see cref="CallerServiceCollectionExtensions.RequireExplicitCallers"/>, outside any it is nobody,
    /// and the context's first command fails instead of running as the system.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="configure">
    /// Changes the roles each kind of caller gets, the defaults being PostgREST's; how long the settings last,
    /// <see cref="PostgresRowLevelSecurityOptions.Scope"/>; and the statement timeout of each kind of caller.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">A role left empty by <paramref name="configure"/>.</exception>
    public static IServiceCollection AddPostgresRowLevelSecurity(this IServiceCollection services, Action<PostgresRowLevelSecurityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd: a host that knows its callers better, ASP.NET Core with its requests, keeps its own.
        services.TryAddSingleton<ICallerAccessor, AmbientCallerAccessor>();
        return services.AddInterceptor(configure);
    }

    /// <summary>
    /// Registers <see cref="PostgresRowLevelSecurityInterceptor"/>, asking <typeparamref name="TAccessor"/>
    /// who is calling, whatever else was registered before.
    /// </summary>
    /// <typeparam name="TAccessor">
    /// Says who is calling. Registered as a singleton and asked every time a context opens a connection,
    /// so it has to answer for the current request or flow of work.
    /// </typeparam>
    /// <param name="services">The application's services.</param>
    /// <param name="configure">Changes the roles each kind of caller gets; the defaults are PostgREST's.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">A role left empty by <paramref name="configure"/>.</exception>
    public static IServiceCollection AddPostgresRowLevelSecurity<TAccessor>(this IServiceCollection services, Action<PostgresRowLevelSecurityOptions>? configure = null)
        where TAccessor : class, ICallerAccessor
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Singleton<ICallerAccessor, TAccessor>());
        return services.AddInterceptor(configure);
    }

    /// <summary>
    /// Registers <typeparamref name="TSettings"/>, whose settings the interceptor sets on every connection
    /// next to the caller's role and claims, in the same statement. Registering the same type twice
    /// registers it once; two providers that declare the same setting fail when the interceptor is built.
    /// <code>
    /// services.AddPostgresRowLevelSecurity();
    /// services.AddRowLevelSecuritySettings&lt;TeamSetting&gt;();
    /// </code>
    /// </summary>
    /// <typeparam name="TSettings">The settings, a singleton; see <see cref="IRowLevelSecuritySettings"/>.</typeparam>
    /// <param name="services">The application's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddRowLevelSecuritySettings<TSettings>(this IServiceCollection services)
        where TSettings : class, IRowLevelSecuritySettings
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRowLevelSecuritySettings, TSettings>());
        return services;
    }

    /// <summary>
    /// Registers <see cref="CallerConnections"/> over the data source <paramref name="dataSource"/> gives, such
    /// as the one the host's contexts connect through: connections for SQL a module sends outside Entity
    /// Framework, with the caller set as a context's connections have it. Registering twice means the second
    /// data source. Needs <see cref="AddPostgresRowLevelSecurity(IServiceCollection, Action{PostgresRowLevelSecurityOptions}?)"/>.
    /// <code>
    /// services.AddPostgresRowLevelSecurity();
    /// services.AddCallerConnections(provider => provider.GetRequiredService&lt;NpgsqlDataSource&gt;());
    /// </code>
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="dataSource">The data source the connections come from; asked once, when the first is wanted.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IServiceCollection AddCallerConnections(this IServiceCollection services, Func<IServiceProvider, DbDataSource> dataSource)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dataSource);

        // A singleton, as the interceptor is: who is calling is asked every time a connection is opened.
        services.Replace(ServiceDescriptor.Singleton(provider => new CallerConnections(
            dataSource(provider) ?? throw new InvalidOperationException($"The data source given to {nameof(AddCallerConnections)} is null."),
            provider.GetService<PostgresRowLevelSecurityInterceptor>()
                ?? throw new InvalidOperationException("Row level security is not registered. Call services.AddPostgresRowLevelSecurity() first."))));

        return services;
    }

    /// <summary>
    /// Runs this context's queries under the caller's role and claims, so Postgres's row level security
    /// applies to them. Needs <see cref="AddPostgresRowLevelSecurity(IServiceCollection, Action{PostgresRowLevelSecurityOptions}?)"/>.
    /// </summary>
    /// <param name="optionsBuilder">The context's options.</param>
    /// <param name="serviceProvider">
    /// The provider handed to the options callback, of <c>AddDbContext</c> or of a context pool: the
    /// interceptor is a singleton that asks who is calling each time it is used, so the root provider a pool
    /// hands its callback serves as well as a scope's.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">Row level security was not registered.</exception>
    public static DbContextOptionsBuilder UsePostgresRowLevelSecurity(this DbContextOptionsBuilder optionsBuilder, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var interceptor = serviceProvider.GetService<PostgresRowLevelSecurityInterceptor>()
            ?? throw new InvalidOperationException("Row level security is not registered. Call services.AddPostgresRowLevelSecurity() first.");

        return optionsBuilder.AddInterceptors(interceptor);
    }

    private static IServiceCollection AddInterceptor(this IServiceCollection services, Action<PostgresRowLevelSecurityOptions>? configure)
    {
        var options = new PostgresRowLevelSecurityOptions();
        configure?.Invoke(options);
        options.Validate();

        // Replace, not add: registering twice means the second one's roles.
        services.Replace(ServiceDescriptor.Singleton(options));
        services.TryAddSingleton<PostgresRowLevelSecurityInterceptor>();

        return services;
    }
}
