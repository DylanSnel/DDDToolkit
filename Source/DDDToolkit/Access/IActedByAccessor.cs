using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Access;

/// <summary>
/// Answers who is acting now, for a record that keeps it: an event log asks it for every event it keeps.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a singleton that reads who is acting each time it is asked</b>, from what follows the flow of work:
/// the ambient caller, the current request. It keeps nothing between two answers and takes no scoped service.
/// Whoever asks it takes it from the provider its context's options were built with, once per save, and with a
/// context pool that provider is the application's root one, where a scoped service cannot be resolved.
/// </para>
/// <para>
/// The default, <see cref="CallerActedByAccessor"/>, answers from the toolkit's own caller. A package or a host
/// that tells more kinds of actor apart registers its own in its place, or wraps the default and answers for
/// the callers it knows.
/// </para>
/// </remarks>
public interface IActedByAccessor
{
    /// <summary>Who is acting now.</summary>
    /// <exception cref="NoCallerException">Nobody is calling, and the host requires every flow of work to say who it runs as.</exception>
    ActedBy Current { get; }
}

/// <summary>
/// Who is acting, read from the toolkit's own caller: a signed-in user by its id, scoped system work as the
/// system with its scope, the system as the system, and an anonymous caller as anonymous
/// (<see cref="ActedBy.Of"/>). The caller is whoever the host's <see cref="ICallerAccessor"/> names, and
/// without one the caller <see cref="Callers.Begin"/> made current, or the system outside any.
/// </summary>
public sealed class CallerActedByAccessor : IActedByAccessor
{
    private readonly ICallerAccessor? _callers;

    /// <summary>An accessor that asks <paramref name="callers"/> who is calling, or reads the ambient caller where the host registered none.</summary>
    /// <param name="callers">The host's accessor, a singleton that answers for the current flow of work; none when left out.</param>
    public CallerActedByAccessor(ICallerAccessor? callers = null) => _callers = callers;

    /// <inheritdoc />
    public ActedBy Current => ActedBy.Of(_callers?.Current ?? Callers.Ambient ?? Caller.System);
}
