using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Access;

/// <summary>
/// Answers who is acting now, for a record that keeps it: an event log asks it for every event it keeps.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a singleton that reads who is acting each time it is asked</b>, from what follows the flow of work:
/// the user begun for the flow, the current request. It keeps nothing between two answers and takes no scoped service.
/// Whoever asks it takes it from the provider its context's options were built with, once per save, and with a
/// context pool that provider is the application's root one, where a scoped service cannot be resolved.
/// </para>
/// <para>
/// The default, <see cref="CurrentUserActedByAccessor"/>, answers from <see cref="ICurrentUser"/>. A package or a host
/// that tells more kinds of actor apart registers its own in its place, or wraps the default and answers for
/// the callers it knows.
/// </para>
/// </remarks>
public interface IActedByAccessor
{
    /// <summary>Who is acting now.</summary>
    /// <exception cref="NoCurrentUserException">Nobody is calling, and the host requires every flow of work to say who it runs as.</exception>
    ActedBy Current { get; }
}

/// <summary>
/// Who is acting, read from <see cref="ICurrentUser"/>: a signed-in user by its id, scoped system work as the system
/// with its scope, system work without a scope as the system, and a caller who has not signed in as anonymous
/// (<see cref="ActedBy.From"/>).
/// </summary>
public sealed class CurrentUserActedByAccessor : IActedByAccessor
{
    private readonly ICurrentUser _user;

    /// <summary>An accessor that asks <paramref name="user"/> who is acting.</summary>
    /// <param name="user">The application's current user, a singleton. Left out, a <see cref="CurrentUser"/> with no options and no source.</param>
    public CurrentUserActedByAccessor(ICurrentUser? user = null) => _user = user ?? new CurrentUser();

    /// <inheritdoc />
    public ActedBy Current => ActedBy.From(_user.Snapshot);
}
