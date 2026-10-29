using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Auth.Supabase.AzureFunctions;

/// <summary>
/// Supabase Auth in an Azure Functions app on the isolated worker.
/// <code>
/// var builder = FunctionsApplication.CreateBuilder(args);
/// builder.UseSupabaseAuth();
/// builder.Services.AddSupabaseAuth("https://&lt;ref&gt;.supabase.co");
/// builder.Services.AddDDDToolkitEntityFramework();   // UseDDDToolkit is DDDToolkit.EntityFramework's
/// builder.Services.AddSupabaseRowLevelSecurity();   // or AddPostgresRowLevelSecurity() off Supabase
/// builder.Services.AddDbContext&lt;OrderingContext&gt;((provider, options) => options
///     .UseNpgsql(connectionString)
///     .UseDDDToolkit(provider));                   // runs the context as the current user, with the toolkit
/// </code>
/// </summary>
public static class SupabaseFunctions
{
    /// <summary>
    /// Adds <see cref="SupabaseAuthMiddleware"/> to the worker's pipeline: every HTTP-triggered function
    /// runs as the user its request's Supabase access token names. Needs <c>services.AddSupabaseAuth(projectUrl)</c>.
    /// It registers <see cref="ICurrentUser"/> and <see cref="AccessManager"/> as well, unless they are registered already.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static IFunctionsWorkerApplicationBuilder UseSupabaseAuth(this IFunctionsWorkerApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCurrentUser();
        return builder.UseMiddleware<SupabaseAuthMiddleware>();
    }

    /// <summary>
    /// Who this invocation runs as: the user of a valid token, <see cref="CurrentUserSnapshot.Anonymous"/> for an
    /// HTTP request without one, and otherwise what <see cref="ICurrentUser"/> answers: the user the function began, or
    /// system work. For a function that answers a caller without a user with a 401.
    /// </summary>
    /// <remarks>
    /// Where the host requires every flow of work to begin its user (<see cref="CurrentUserOptions"/> in the
    /// invocation's services, registered by <see cref="CurrentUserServiceCollectionExtensions.RequireExplicitCurrentUser"/>),
    /// an invocation that is not an HTTP request and began no user throws instead of answering system work.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="NoCurrentUserException">Nothing began a user, and the host requires every invocation to say who it runs as.</exception>
    public static CurrentUserSnapshot GetSupabaseUser(this FunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(SupabaseAuthMiddleware.UserKey, out var kept) && kept is CurrentUserSnapshot requestUser)
        {
            return requestUser;
        }

        // The worker hands every invocation its services; a context built by hand may have none. Only a begun user
        // counts here: a host's source knows no function invocation.
        var options = context.InstanceServices?.GetService<CurrentUserOptions>();
        var user = context.InstanceServices?.GetService<ICurrentUser>() ?? new CurrentUser(options);
        if (user.IsBegun)
        {
            return user.Snapshot;
        }

        return options is { RequireExplicitCurrentUser: true }
            ? throw new NoCurrentUserException(
                "This invocation is not an HTTP request and began no user, and this host requires one (RequireExplicitCurrentUser). " +
                "Begin a user in the function with AccessManager: BeginSystem(scope) for a module's system work, BeginSystemBypassingRowAccess() for system work past every row access rule, " +
                "or BeginAs(userId) for the user a queued message names.")
            : CurrentUserSnapshot.System;
    }
}
