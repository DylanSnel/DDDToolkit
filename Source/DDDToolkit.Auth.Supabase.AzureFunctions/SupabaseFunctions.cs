using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;

namespace DDDToolkit.Auth.Supabase.AzureFunctions;

/// <summary>
/// Supabase Auth in an Azure Functions app on the isolated worker.
/// <code>
/// var builder = FunctionsApplication.CreateBuilder(args);
/// builder.UseSupabaseAuth();
/// builder.Services.AddSupabaseAuth("https://&lt;ref&gt;.supabase.co");
/// builder.Services.AddSupabaseRowLevelSecurity();   // or AddPostgresRowLevelSecurity() off Supabase
/// builder.Services.AddDbContext&lt;OrderingContext&gt;((provider, options) => options
///     .UseNpgsql(connectionString)
///     .UseSupabaseRowLevelSecurity(provider));
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
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public static Caller GetSupabaseCaller(this FunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(SupabaseAuthMiddleware.CallerKey, out var kept) && kept is Caller caller
            ? caller
            : Callers.Ambient ?? Caller.System;
    }
}
