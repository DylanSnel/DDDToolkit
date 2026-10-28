using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Access;

/// <summary>
/// The toolkit's <see cref="ICurrentUser"/>. It answers from the user begun for the current flow of work, then from the
/// host's <see cref="ICurrentUserSource"/>, and otherwise with system work. With
/// <see cref="CurrentUserOptions.RequireExplicitCurrentUser"/> on, it throws <see cref="NoCurrentUserException"/>
/// instead of answering system work.
/// </summary>
/// <remarks>
/// It keeps no state of its own, so one instance serves the whole application, and the toolkit registers it as a
/// singleton. Row level security asks it every time a context opens a connection, from a singleton that a context pool
/// shares. What <see cref="AccessManager"/> begins follows the flow of work, and every instance sees the same.
/// <para>
/// A host that knows its requests registers an <see cref="ICurrentUserSource"/>. <c>DDDToolkit.Auth.Supabase.AspNetCore</c>
/// registers one that answers the user of the request's validated token. A worker service, an Azure Function or a test
/// needs none: it begins the user its work runs as, with <see cref="AccessManager"/>.
/// </para>
/// </remarks>
/// <param name="options">Whether the host requires every flow of work to begin its user; registered by <see cref="CurrentUserServiceCollectionExtensions.RequireExplicitCurrentUser"/>.</param>
/// <param name="source">The host's answer where nothing was begun, such as the request's user; none when left out.</param>
public sealed class CurrentUser(CurrentUserOptions? options = null, ICurrentUserSource? source = null) : ICurrentUser
{
    /// <inheritdoc />
    public CurrentUserSnapshot Snapshot => BegunUser.Value
        ?? source?.Current
        ?? (options?.RequireExplicitCurrentUser == true ? throw new NoCurrentUserException() : CurrentUserSnapshot.System);

    /// <inheritdoc />
    public bool IsBegun => BegunUser.Value is not null;

    /// <inheritdoc />
    public CurrentUserKind Kind => Snapshot.Kind;

    /// <inheritdoc />
    public Guid? Id => Snapshot.Id;

    /// <inheritdoc />
    public bool IsAuthenticated => Snapshot.IsAuthenticated;

    /// <inheritdoc />
    public bool IsSystem => Snapshot.IsSystem;

    /// <inheritdoc />
    public string? SystemScope => Snapshot.SystemScope;

    /// <inheritdoc />
    public string? Role => Snapshot.Role;

    /// <inheritdoc />
    public string? FindClaim(string path) => Snapshot.FindClaim(path);

    /// <inheritdoc />
    /// <exception cref="RefusalException"><see cref="ToolkitRefusals.SystemOnly"/> for anyone but begun system work.</exception>
    /// <exception cref="NoCurrentUserException">Nothing began a user for the work, and the host requires every flow of work to begin one.</exception>
    public CurrentUserSnapshot RequireSystemWork()
    {
        // The host is asked first, so a host that requires every flow to begin its user fails such work with
        // NoCurrentUserException rather than a refusal. A begun user always wins over the host's answer, so system work
        // the flow began is what Snapshot answers; the host's own answer of system work is refused.
        var current = Snapshot;
        if (!current.IsSystem || !IsBegun)
        {
            throw ToolkitRefusals.Refuse(ToolkitRefusals.SystemOnly);
        }

        return current;
    }
}
