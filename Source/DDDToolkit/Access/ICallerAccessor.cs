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
    /// <summary>Who the application is acting for now; <see cref="Caller.System"/> for its own work, never null.</summary>
    Caller Current { get; }
}

/// <summary>
/// The caller <see cref="Callers.Begin"/> made current, or <see cref="Caller.System"/> outside any: the
/// accessor for a host that says who is calling by beginning a caller, such as an Azure Function, a
/// worker service, or a test.
/// </summary>
public sealed class AmbientCallerAccessor : ICallerAccessor
{
    /// <inheritdoc />
    public Caller Current => Callers.Ambient ?? Caller.System;
}
