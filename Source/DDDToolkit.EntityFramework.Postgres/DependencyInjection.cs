using System.Data.Common;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Composition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>Registers row level security for the contexts on Postgres, which then run under the caller's role.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="PostgresRowLevelSecurityInterceptor"/>, and brings it to every context on Postgres
    /// that <c>UseDDDToolkit</c> wires, as the part <see cref="PostgresRowLevelSecurityInterceptor.PartName"/>: each
    /// then runs under its caller's role. <c>UseDDDToolkit</c> is <c>DDDToolkit.EntityFramework</c>'s, registered
    /// with <c>AddDDDToolkitEntityFramework</c>:
    /// <code>
    /// services.AddDDDToolkitEntityFramework();
    /// services.AddPostgresRowLevelSecurity();
    /// services.AddDbContext&lt;OrderingContext&gt;((provider, options) => options
    ///     .UseNpgsql(connectionString)
    ///     .UseDDDToolkit(provider));
    /// </code>
    /// A context without the toolkit takes it with <see cref="UsePostgresRowLevelSecurity"/>, this package on its
    /// own: <c>options.UseNpgsql(connectionString).UsePostgresRowLevelSecurity(provider)</c>. One that should run as
    /// the role the application logged in as, while the others run as their caller, is configured with
    /// <c>UseDDDToolkitCore</c>.
    /// <para>
    /// Who is calling is whatever a host registered: in ASP.NET Core, the request's user, once
    /// <c>AddSupabaseJwtBearer</c> from <c>DDDToolkit.Auth.Supabase.AspNetCore</c> is there. Without one,
    /// it is the caller <see cref="Callers.Begin"/> made current, or <see cref="Caller.System"/> outside any
    /// (<see cref="AmbientCallerAccessor"/>), which is what an Azure Function, a worker service or a test wants.
    /// With <see cref="CallerServiceCollectionExtensions.RequireExplicitCallers"/>, outside any it is nobody,
    /// and the context's first command fails instead of running as the system.
    /// </para>
    /// <para>
    /// It brings the start-up checks of <see cref="PostgresRowAccessChecks"/>, over every registered context on
    /// Postgres, which a host runs with <c>services.RunStartupChecks()</c>.
    /// </para>
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
    /// <para>
    /// <c>UseDDDToolkit</c> calls it for every context on Postgres once row level security is registered, so a
    /// context wired with that one call needs nothing more. It is for a context configured with
    /// <c>UseDDDToolkitCore</c>, which takes the parts it wants one by one, and for one without the toolkit's
    /// interceptors. It adds nothing to options that already have the interceptor, so a chain that calls it after
    /// <c>UseDDDToolkit</c> gives it once.
    /// </para>
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

        var present = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        return present.OfType<PostgresRowLevelSecurityInterceptor>().Any() ? optionsBuilder : optionsBuilder.AddInterceptors(interceptor);
    }

    private static IServiceCollection AddInterceptor(this IServiceCollection services, Action<PostgresRowLevelSecurityOptions>? configure)
    {
        var options = new PostgresRowLevelSecurityOptions();
        configure?.Invoke(options);
        options.Validate();

        // Replace, not add: registering twice means the second one's roles.
        services.Replace(ServiceDescriptor.Singleton(options));
        services.TryAddSingleton<PostgresRowLevelSecurityInterceptor>();

        // Every context on Postgres that UseDDDToolkit wires runs as its caller, without a call of its own.
        services.AddContextPart(new ContextPart<DbContextOptionsBuilder>(
            PostgresRowLevelSecurityInterceptor.PartName,
            PostgresRowLevelSecurityInterceptor.PartPosition,
            (contextOptions, provider) => contextOptions.UsePostgresRowLevelSecurity(provider))
        {
            AppliesTo = MayBeOnPostgres,
        });

        // What the lock relies on in the database and in the contexts, checked before the host starts once it
        // runs its start-up checks.
        PostgresRowAccessChecks.AddStartupChecks(services);

        return services;
    }

    /// <summary>
    /// Whether <paramref name="options"/> may be those of a context on Postgres: false only where a provider is
    /// configured and it is not Npgsql's, the name Entity Framework reports as the context's provider. This package
    /// references no Npgsql, so the name is what it goes by, as in the start-up checks.
    /// <para>
    /// Where no provider is configured yet, because the options callback calls <c>UseDDDToolkit</c> before
    /// <c>UseNpgsql</c>, or the context sets its provider in <c>OnConfiguring</c>, which Entity Framework runs after the
    /// callback, the part goes on: the interceptor passes over a context that turns out not to be on Postgres when it
    /// is used, and runs one that is as its caller. Refusing would break a chain 3.1 accepted, and leaving it off
    /// would run a context on Postgres as the login role, in silence.
    /// </para>
    /// </summary>
    private static bool MayBeOnPostgres(DbContextOptionsBuilder options)
        => options.Options.Extensions.FirstOrDefault(extension => extension.Info.IsDatabaseProvider) is not { } provider
           || string.Equals(provider.GetType().Assembly.GetName().Name, PostgresRowAccessChecks.NpgsqlProvider, StringComparison.Ordinal);
}
