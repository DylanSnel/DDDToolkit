using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Access;

/// <summary>
/// Says who the application is acting for at this moment. Row level security asks every time a context
/// opens a connection, so the answer has to follow the current request or flow of work, the way
/// <c>IHttpContextAccessor</c> does, rather than be fixed when it is created.
/// </summary>
/// <remarks>
/// A host registers the one that knows its callers: <c>DDDToolkit.Auth.Supabase.AspNetCore</c> answers
/// from the request, and without one <see cref="AmbientCallerAccessor"/> answers with what
/// <see cref="Callers.Begin"/> made current.
/// </remarks>
public interface ICallerAccessor
{
    /// <summary>
    /// Who the application is acting for now, never null: <see cref="Caller.System"/> for its own work,
    /// unless the host requires explicit callers, in which case an accessor that knows nobody throws
    /// <see cref="NoCallerException"/> instead.
    /// </summary>
    /// <exception cref="NoCallerException">Nobody is calling, and the host requires every flow of work to say who it runs as.</exception>
    Caller Current { get; }
}

/// <summary>
/// The caller <see cref="Callers.Begin"/> made current, or <see cref="Caller.System"/> outside any: the
/// accessor for a host that says who is calling by beginning a caller, such as an Azure Function, a
/// worker service, or a test.
/// </summary>
/// <remarks>
/// With <see cref="CallerOptions.RequireExplicitCallers"/>, outside any caller it throws
/// <see cref="NoCallerException"/> instead of answering the system, so work nobody said anything about
/// fails rather than running with the application's own power.
/// </remarks>
public sealed class AmbientCallerAccessor : ICallerAccessor
{
    private readonly CallerOptions? _options;

    /// <summary>An accessor that answers the system outside any caller, as in every 3.x host.</summary>
    public AmbientCallerAccessor()
        : this(null)
    {
    }

    /// <summary>An accessor that answers as <paramref name="options"/> say outside any caller; the one dependency injection builds.</summary>
    /// <param name="options">Whether the host requires explicit callers; registered by <see cref="CallerServiceCollectionExtensions.RequireExplicitCallers"/>.</param>
    public AmbientCallerAccessor(CallerOptions? options) => _options = options;

    /// <inheritdoc />
    public Caller Current => Callers.Ambient
        ?? (_options?.RequireExplicitCallers == true ? throw new NoCallerException() : Caller.System);
}
