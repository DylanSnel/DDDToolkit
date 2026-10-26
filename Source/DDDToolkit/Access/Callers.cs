using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;

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
    /// Makes nobody the caller until the result is disposed, and the one before it again afterwards. The
    /// toolkit runs the handlers it calls from its own background work inside this when the host requires
    /// explicit callers (<see cref="CallerServiceCollectionExtensions.RequireExplicitCallers"/>), so a
    /// handler does not inherit the system caller its bookkeeping ran as: it runs as the caller something
    /// begins for it, or, asking an <see cref="AmbientCallerAccessor"/>, fails with
    /// <see cref="NoCallerException"/>.
    /// <para>
    /// It hides the ambient caller only. An accessor that knows the request, such as the one
    /// <c>DDDToolkit.Auth.Supabase.AspNetCore</c> registers, still answers for the request inside it.
    /// </para>
    /// </summary>
    public static IDisposable BeginNone()
    {
        var previous = AmbientCaller.Value;
        AmbientCaller.Value = null;
        return new Scope(previous);
    }

    /// <summary>
    /// Refuses everyone but the application's own work that trusted code began on purpose for this flow:
    /// <see cref="Caller.System"/> or a scoped <see cref="Caller.SystemIn"/>, made current with <see cref="Begin"/>
    /// or a supporting domain's own way, such as Tenancy's <c>TenancyUseCases.BeginSystem()</c>. For a handler, a
    /// job's step or a use case that only system work may run: it asks past the door, where the request has passed
    /// its requirement, who may send it, and the handler says what only the application itself may do.
    /// <code>
    /// public async Task HandleAsync(ImportPrices command, CancellationToken cancellationToken)
    /// {
    ///     Callers.RequireSystemWork(callers);          // access.system-only for anyone else
    ///     // ... the import
    /// }
    /// </code>
    /// </summary>
    /// <remarks>
    /// Begun means begun: the host's accessor answers system work, and the caller begun for the flow is system work
    /// too. A flow nobody began a caller for is refused, even where the accessor answers <see cref="Caller.System"/>
    /// for it, as an <see cref="AmbientCallerAccessor"/> does in a host that does not require explicit callers: in
    /// such a host that answer is also what a web request gets when no accessor knows requests, so asking
    /// <c>callers.Current.Kind</c> alone would let it through. Work inside <see cref="BeginNone"/> is refused too, and
    /// system work begun beside an accessor that answers somebody else for the flow does not run as the system, so it
    /// is refused as well. The host is asked first, so a host that does require explicit callers fails un-begun work
    /// with <see cref="NoCallerException"/> instead.
    /// <para>
    /// It is no requirement, and a request declares none for system work: the request says who may send it, and the
    /// scope is begun around the send or inside the handler (see the access requirements guide).
    /// </para>
    /// </remarks>
    /// <param name="callers">The host's accessor, which says who the work runs as.</param>
    /// <returns>The caller the work runs as: <see cref="Caller.System"/>, or the <see cref="Caller.SystemIn"/> of its scope.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="callers"/> is null.</exception>
    /// <exception cref="RefusalException">
    /// <see cref="ToolkitRefusals.SystemOnly"/> for a user, whatever they hold, a caller who did not sign in,
    /// and work nobody began a caller for.
    /// </exception>
    /// <exception cref="NoCallerException">Nobody is calling, and the host requires every flow of work to say who it runs as.</exception>
    public static Caller RequireSystemWork(ICallerAccessor callers)
    {
        ArgumentNullException.ThrowIfNull(callers);

        var current = callers.Current;
        if (!IsSystemWork(current) || !IsSystemWork(AmbientCaller.Value))
        {
            throw ToolkitRefusals.Refuse(ToolkitRefusals.SystemOnly);
        }

        return current;
    }

    /// <summary>Whether <paramref name="caller"/> is the application's own work, above the policies or in a scope.</summary>
    private static bool IsSystemWork(Caller? caller) => caller?.Kind is CallerKind.System or CallerKind.SystemIn;

    /// <summary>
    /// The user an access token names, from its claims: <c>sub</c> is the user's id, <c>role</c> their
    /// role, and <see cref="Caller.Claim"/> reads the rest. The claims go to the database as they are, so
    /// <c>auth.jwt()</c> there answers what the token says. A token without a <c>role</c> claim, or with
    /// <c>null</c> for one, gives a caller without a role; a <c>role</c> that is not text is the caller's role
    /// as the token writes it, which is never a role a host knows.
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

        // A role claim that is there but is no text, a list of roles say, is kept as it is written: a role like
        // any other, which no host listed. Left out, it would read as a token that says nothing about a role,
        // and that is a signed-in user.
        var role = root.TryGetProperty("role", out var claimed)
            ? claimed.ValueKind switch
            {
                JsonValueKind.String => claimed.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => claimed.GetRawText(),
            }
            : null;

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
