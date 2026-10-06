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
///     .UseDDDToolkit(provider));                   // runs the context as its caller, with the toolkit
/// </code>
/// </summary>
public static class SupabaseFunctions
{
    /// <summary>
    /// Adds <see cref="SupabaseAuthMiddleware"/> to the worker's pipeline: every HTTP-triggered function
    /// runs as the user its request's Supabase access token names. Needs <c>services.AddSupabaseAuth(projectUrl)</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static IFunctionsWorkerApplicationBuilder UseSupabaseAuth(this IFunctionsWorkerApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<SupabaseAuthMiddleware>();
    }

    /// <summary>
    /// Who this invocation runs as: the user of a valid token, <see cref="Caller.Anonymous"/> for an
    /// HTTP request without one, and otherwise the caller made current with <see cref="Callers.Begin"/>,
    /// or <see cref="Caller.System"/>. For a function that answers a caller without a user with a 401.
    /// </summary>
    /// <remarks>
    /// Where the host requires explicit callers (<see cref="CallerOptions"/> in the invocation's services,
    /// registered by <see cref="CallerServiceCollectionExtensions.RequireExplicitCallers"/>), an invocation
    /// that is not an HTTP request and began no caller throws instead of answering the system.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="NoCallerException">Nobody is calling, and the host requires every invocation to say who it runs as.</exception>
    public static Caller GetSupabaseCaller(this FunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(SupabaseAuthMiddleware.CallerKey, out var kept) && kept is Caller caller)
        {
            return caller;
        }

        if (Callers.Ambient is { } ambient)
        {
            return ambient;
        }

        // The worker hands every invocation its services; a context built by hand may have none.
        return context.InstanceServices?.GetService<CallerOptions>() is { RequireExplicitCallers: true }
            ? throw new NoCallerException(
                "This invocation is not an HTTP request and began no caller, and this host requires one (RequireExplicitCallers). " +
                "Begin a caller in the function: Callers.Begin(Caller.System) for the application's own work, or the user a queued message names.")
            : Caller.System;
    }
}
