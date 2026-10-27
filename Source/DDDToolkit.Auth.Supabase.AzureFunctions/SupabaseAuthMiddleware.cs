using DDDToolkit.Abstractions.Access;
using System.Text.Json;
using DDDToolkit.Access;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace DDDToolkit.Auth.Supabase.AzureFunctions;

/// <summary>
/// Makes the user of an HTTP-triggered invocation its current user. It validates the Supabase Auth access token in
/// the request's <c>Authorization</c> header, and runs the function inside
/// <see cref="ICurrentUser.BeginAs(CurrentUserSnapshot)"/>, so every query it makes runs as that user, or as
/// <c>anon</c> without a valid token. Other triggers pass through untouched and run as system work, unless the
/// function begins a user itself, say from claims a queued message carries. Where the host requires every flow of
/// work to begin its user (<see cref="CurrentUserServiceCollectionExtensions.RequireExplicitCurrentUser"/>), they run
/// as nobody until the function begins one.
/// </summary>
/// <remarks>
/// Register it with <c>builder.UseSupabaseAuth()</c>. The token is read from the invocation's trigger
/// data rather than from an <c>HttpRequest</c>, so it works with the built-in HTTP model and with the
/// ASP.NET Core integration alike, and nothing here needs either.
/// </remarks>
/// <param name="validator">Validates the request's token.</param>
/// <param name="user">The application's current user, which begins the request's user. Left out, a <see cref="CurrentUser"/>.</param>
public sealed class SupabaseAuthMiddleware(SupabaseTokenValidator validator, ICurrentUser? user = null) : IFunctionsWorkerMiddleware
{
    /// <summary>Where the invocation's user is kept, for <see cref="SupabaseFunctions.GetSupabaseUser"/>.</summary>
    internal const string UserKey = "DDDToolkit.Auth.Supabase.User";

    private readonly ICurrentUser _user = user ?? new CurrentUser();

    /// <inheritdoc />
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!IsHttpTriggered(context))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var requestUser = await validator.UserOfAsync(AuthorizationOf(context)).ConfigureAwait(false);
        context.Items[UserKey] = requestUser;

        using (_user.BeginAs(requestUser))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    private static bool IsHttpTriggered(FunctionContext context)
        => context.FunctionDefinition.InputBindings.Values
            .Any(binding => string.Equals(binding.Type, "httpTrigger", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The request's <c>Authorization</c> header, from the trigger data the host hands the worker: a
    /// JSON object of the request's headers under <c>Headers</c>.
    /// </summary>
    private static string? AuthorizationOf(FunctionContext context)
    {
        if (!context.BindingContext.BindingData.TryGetValue("Headers", out var headers) || headers is null)
        {
            return null;
        }

        var json = headers switch
        {
            string text => text,
            JsonElement element => element.GetRawText(),
            _ => headers.ToString(),
        };

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        foreach (var header in document.RootElement.EnumerateObject())
        {
            if (header.NameEquals("Authorization") || header.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                return header.Value.ValueKind is JsonValueKind.String ? header.Value.GetString() : header.Value.ToString();
            }
        }

        return null;
    }
}
