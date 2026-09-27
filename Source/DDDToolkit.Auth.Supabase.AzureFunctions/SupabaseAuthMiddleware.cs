using System.Text.Json;
using DDDToolkit.Access;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace DDDToolkit.Auth.Supabase.AzureFunctions;

/// <summary>
/// Makes the user of an HTTP-triggered invocation its caller: validates the Supabase Auth access token in
/// the request's <c>Authorization</c> header, and runs the function inside
/// <see cref="Callers.Begin"/>, so every query it makes runs as that user, or as <c>anon</c>
/// without a valid token. Other triggers pass through untouched and run as the system, unless the
/// function begins a caller itself, say from claims a queued message carries.
/// </summary>
/// <remarks>
/// Register it with <c>builder.UseSupabaseAuth()</c>. The token is read from the invocation's trigger
/// data rather than from an <c>HttpRequest</c>, so it works with the built-in HTTP model and with the
/// ASP.NET Core integration alike, and nothing here needs either.
/// </remarks>
public sealed class SupabaseAuthMiddleware(SupabaseTokenValidator validator) : IFunctionsWorkerMiddleware
{
    /// <summary>Where the invocation's caller is kept, for <see cref="SupabaseFunctions.GetSupabaseCaller"/>.</summary>
    internal const string CallerKey = "DDDToolkit.Auth.Supabase.Caller";

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

        var caller = await validator.CallerOfAsync(AuthorizationOf(context)).ConfigureAwait(false);
        context.Items[CallerKey] = caller;

        using (Callers.Begin(caller))
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
