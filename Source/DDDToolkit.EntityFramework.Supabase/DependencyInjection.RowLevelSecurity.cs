using System.Reflection;
using System.Runtime.CompilerServices;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Supabase;

public static partial class DependencyInjection
{
    /// <summary>
    /// Postgres's row level security, <c>DDDToolkit.EntityFramework.Postgres</c>, with the roles every
    /// Supabase project has, so the policies that guard the Data API guard the application's queries too.
    /// <c>UseDDDToolkit</c>, of <c>DDDToolkit.EntityFramework</c>, adds it to every context on Postgres, as the part
    /// <see cref="PostgresRowLevelSecurityInterceptor.PartName"/>.
    /// <code>
    /// services.AddDDDToolkitEntityFramework();
    /// services.AddSupabaseRowLevelSecurity();
    /// services.AddDbContext&lt;OrderingContext&gt;((provider, options) => options
    ///     .UseNpgsql(connectionString)
    ///     .UseDDDToolkit(provider));
    /// </code>
    /// A context without the toolkit takes it with <see cref="UseSupabaseRowLevelSecurity"/>, this package on its
    /// own. One that should run as the role the application logged in as, while the others run as their caller, is
    /// configured with <c>UseDDDToolkitCore</c>.
    /// <para>
    /// Who is calling is whatever a host registered: in ASP.NET Core, the request's user, once
    /// <c>AddSupabaseJwtBearer</c> from <c>DDDToolkit.Auth.Supabase.AspNetCore</c> is there. Without one,
    /// it is the caller <see cref="Callers.Begin"/> made current, or <see cref="Caller.System"/> outside any.
    /// </para>
    /// <para>
    /// <b>The roles are said once, in the project file.</b> Each kind of caller runs as the role the project's
    /// <c>SupabaseRowAccessRoles</c> names for it, the property the export writes the policies from, and as
    /// <see cref="SupabaseRowLevelSecurity.DefaultRoles"/> where it names none: <c>authenticated</c>, <c>anon</c>,
    /// <c>ddd_system_in</c>, and <c>ddd_system</c> for the system caller, whose bookkeeping the access files give that
    /// role. The build records the property in the application, as an assembly attribute, and this reads it from the
    /// assembly whose code calls it, else from the application the host's environment names, the host's also where a
    /// library registers row level security or <c>WebApplicationFactory</c> runs the host in a test, else from the
    /// entry assembly. So a host whose roles are the defaults, or that exports itself, writes no role in code; one
    /// whose export runs in another project says its deviations in its own project file too, or in a
    /// <c>Directory.Build.props</c> both projects share, and the start-up check
    /// <see cref="SupabaseRowAccessChecks.RolesMatchAccessFilesCheck"/>, which this registers, compares the roles with
    /// the ones the database's access files were written for. What <paramref name="configure"/> sets wins over both.
    /// </para>
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="configure">
    /// Changes what the roles would be, such as <see cref="PostgresRowLevelSecurityOptions.SystemRole"/>, and how long
    /// the settings last: <see cref="PostgresRowLevelSecurityOptions.Scope"/> is
    /// <see cref="RowLevelSecurityScope.Transaction"/> for a connection through Supabase's transaction pooler.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">A role left empty by <paramref name="configure"/>.</exception>
    /// <exception cref="InvalidOperationException">The roles the build recorded are ones the export would refuse.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IServiceCollection AddSupabaseRowLevelSecurity(this IServiceCollection services, Action<PostgresRowLevelSecurityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Not inlined, so the calling assembly is the one whose code asked, the host's, and not this one.
        var recorded = SupabaseCallerRoles.RecordedFor(Assembly.GetCallingAssembly(), services);
        services.AddPostgresRowLevelSecurity(options => WithSupabaseRoles(options, recorded, configure));
        SupabaseRowAccessChecks.AddStartupChecks(services);
        return services;
    }

    /// <summary>
    /// <see cref="AddSupabaseRowLevelSecurity(IServiceCollection, Action{PostgresRowLevelSecurityOptions}?)"/>,
    /// asking <typeparamref name="TAccessor"/> who is calling, whatever else was registered before.
    /// </summary>
    /// <typeparam name="TAccessor">Says who is calling; asked every time a context opens a connection.</typeparam>
    /// <param name="services">The application's services.</param>
    /// <param name="configure">Changes what the roles would be.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">A role left empty by <paramref name="configure"/>.</exception>
    /// <exception cref="InvalidOperationException">The roles the build recorded are ones the export would refuse.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IServiceCollection AddSupabaseRowLevelSecurity<TAccessor>(this IServiceCollection services, Action<PostgresRowLevelSecurityOptions>? configure = null)
        where TAccessor : class, ICallerAccessor
    {
        ArgumentNullException.ThrowIfNull(services);

        var recorded = SupabaseCallerRoles.RecordedFor(Assembly.GetCallingAssembly(), services);
        services.AddPostgresRowLevelSecurity<TAccessor>(options => WithSupabaseRoles(options, recorded, configure));
        SupabaseRowAccessChecks.AddStartupChecks(services);
        return services;
    }

    /// <summary>
    /// Runs this context's queries under the caller's role and claims, so Supabase's policies apply to them
    /// as they do to the Data API. Needs <see cref="AddSupabaseRowLevelSecurity(IServiceCollection, Action{PostgresRowLevelSecurityOptions}?)"/>.
    /// <c>UseDDDToolkit</c> already calls it for a context on Postgres; it adds nothing to options that have the
    /// interceptor, so a chain that calls it as well gives it once.
    /// </summary>
    /// <param name="optionsBuilder">The context's options.</param>
    /// <param name="serviceProvider">The provider handed to the options callback, of <c>AddDbContext</c> or of a context pool.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">Row level security was not registered.</exception>
    public static DbContextOptionsBuilder UseSupabaseRowLevelSecurity(this DbContextOptionsBuilder optionsBuilder, IServiceProvider serviceProvider)
        => optionsBuilder.UsePostgresRowLevelSecurity(serviceProvider);

    private static void WithSupabaseRoles(PostgresRowLevelSecurityOptions options, SupabaseCallerRoles recorded, Action<PostgresRowLevelSecurityOptions>? configure)
    {
        // Named here rather than left to Postgres's defaults, which only happen to be the same for a user and an
        // anonymous caller and differ for the system caller: the roles the export writes the policies for unless the
        // project says otherwise, the ones the build recorded where it does. The host's own code comes last, and wins.
        recorded.ApplyTo(options);
        configure?.Invoke(options);
    }
}
