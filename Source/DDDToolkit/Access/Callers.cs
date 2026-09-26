using System.Text.Json;
using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Access;

/// <summary>
/// Makes callers, and makes one the caller of a flow of work. An <see cref="ICallerAccessor"/> says who is
/// calling; this is what a host without requests says it with, and how code runs as somebody on purpose.
/// </summary>
public static class Callers
{
    private static readonly AsyncLocal<Caller?> AmbientCaller = new();

    /// <summary>
    /// The caller <see cref="Begin"/> made current for this flow of work, or <see langword="null"/>
    /// outside any. It follows the flow the way an <see cref="AsyncLocal{T}"/> does: into awaited calls
    /// and into tasks started inside, not back out.
    /// </summary>
    public static Caller? Ambient => AmbientCaller.Value;

    /// <summary>
    /// Makes <paramref name="caller"/> the caller for everything that runs until the result is disposed,
    /// and then the one before it again. For a host without requests, such as a queue-triggered function,
    /// and for work that should run as somebody on purpose: a job queued for a user, carrying their
    /// claims, or a step a request takes as the system.
    /// <code>
    /// using (Callers.Begin(Callers.FromClaims(claims)))
    /// {
    ///     await handler.HandleAsync(job, cancellationToken);
    /// }
    /// </code>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="caller"/> is null.</exception>
    public static IDisposable Begin(Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var previous = AmbientCaller.Value;
        AmbientCaller.Value = caller;
        return new Scope(previous);
    }

    /// <summary>
    /// The user an access token names, from its claims: <c>sub</c> is the user's id, <c>role</c> their
    /// role, and <see cref="Caller.Claim"/> reads the rest. The claims go to the database as they are, so
    /// <c>auth.jwt()</c> there answers what the token says.
    /// </summary>
    /// <param name="claims">
    /// The payload of an access token that has already been validated: the JSON object between the two
    /// dots, decoded. Nothing here checks a signature, so never pass claims that did not come out of a
    /// validated token.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="claims"/> is empty or not a JSON object.</exception>
    public static Caller FromClaims(string claims)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claims);

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(claims);
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The claims of an access token are a JSON object, and these are not JSON.", nameof(claims), exception);
        }

        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw new ArgumentException("The claims of an access token are a JSON object.", nameof(claims));
        }

        var user = root.TryGetProperty("sub", out var sub) && sub.ValueKind is JsonValueKind.String && Guid.TryParse(sub.GetString(), out var id) ? id : (Guid?)null;
        var role = root.TryGetProperty("role", out var claimed) && claimed.ValueKind is JsonValueKind.String ? claimed.GetString() : null;

        return Caller.User(user, role, path => ClaimOf(root, path), claims);
    }

    /// <summary>The claim at <paramref name="path"/>, <c>app_metadata.role</c>, as text; <see langword="null"/> when there is none.</summary>
    private static string? ClaimOf(JsonElement root, string path)
    {
        var node = root;
        foreach (var name in path.Split('.'))
        {
            if (node.ValueKind is not JsonValueKind.Object || !node.TryGetProperty(name, out node))
            {
                return null;
            }
        }

        return node.ValueKind switch
        {
            JsonValueKind.String => node.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => node.GetRawText(),
        };
    }

    /// <summary>Puts back the caller from before <see cref="Begin"/>, once.</summary>
    private sealed class Scope(Caller? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            AmbientCaller.Value = previous;
        }
    }
}
