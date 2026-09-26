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
    /// Add it to each context with <see cref="UseSupabaseRowLevelSecurity"/>.
    /// <code>
    /// services.AddSupabaseRowLevelSecurity();
    /// services.AddDbContext&lt;OrderingContext&gt;((provider, options) => options
    ///     .UseNpgsql(connectionString)
    ///     .UseSupabaseRowLevelSecurity(provider));
    /// </code>
    /// Who is calling is whatever a host registered: in ASP.NET Core, the request's user, once
    /// <c>AddSupabaseJwtBearer</c> from <c>DDDToolkit.Auth.Supabase.AspNetCore</c> is there. Without one,
    /// it is the caller <see cref="Callers.Begin"/> made current, or <see cref="Caller.System"/> outside any.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="configure">Changes what Supabase's roles would be, such as <see cref="PostgresRowLevelSecurityOptions.SystemRole"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">A role left empty by <paramref name="configure"/>.</exception>
    public static IServiceCollection AddSupabaseRowLevelSecurity(this IServiceCollection services, Action<PostgresRowLevelSecurityOptions>? configure = null)
        => services.AddPostgresRowLevelSecurity(options => WithSupabaseRoles(options, configure));

    /// <summary>
    /// <see cref="AddSupabaseRowLevelSecurity(IServiceCollection, Action{PostgresRowLevelSecurityOptions}?)"/>,
    /// asking <typeparamref name="TAccessor"/> who is calling, whatever else was registered before.
    /// </summary>
    /// <typeparam name="TAccessor">Says who is calling; asked every time a context opens a connection.</typeparam>
    /// <param name="services">The application's services.</param>
    /// <param name="configure">Changes what Supabase's roles would be.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">A role left empty by <paramref name="configure"/>.</exception>
    public static IServiceCollection AddSupabaseRowLevelSecurity<TAccessor>(this IServiceCollection services, Action<PostgresRowLevelSecurityOptions>? configure = null)
        where TAccessor : class, ICallerAccessor
        => services.AddPostgresRowLevelSecurity<TAccessor>(options => WithSupabaseRoles(options, configure));

    /// <summary>
    /// Runs this context's queries under the caller's role and claims, so Supabase's policies apply to them
    /// as they do to the Data API. Needs <see cref="AddSupabaseRowLevelSecurity(IServiceCollection, Action{PostgresRowLevelSecurityOptions}?)"/>.
    /// </summary>
    /// <param name="optionsBuilder">The context's options.</param>
    /// <param name="serviceProvider">The provider handed to the <c>AddDbContext</c> callback.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">Row level security was not registered.</exception>
    public static DbContextOptionsBuilder UseSupabaseRowLevelSecurity(this DbContextOptionsBuilder optionsBuilder, IServiceProvider serviceProvider)
        => optionsBuilder.UsePostgresRowLevelSecurity(serviceProvider);

    private static void WithSupabaseRoles(PostgresRowLevelSecurityOptions options, Action<PostgresRowLevelSecurityOptions>? configure)
    {
        // Named here rather than left to Postgres's defaults, which only happen to be the same: these are
        // the roles PostgREST switches to on every Supabase project, whatever the defaults become.
        options.UserRole = SupabaseRowLevelSecurity.AuthenticatedRole;
        options.AnonymousRole = SupabaseRowLevelSecurity.AnonRole;
        configure?.Invoke(options);
    }
}
