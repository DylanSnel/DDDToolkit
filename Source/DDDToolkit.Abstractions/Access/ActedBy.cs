namespace DDDToolkit.Abstractions.Access;

/// <summary>
/// Who is acting, for a record that keeps it: a kind, and an id whose meaning the kind gives. An event log
/// writes one on every row, so a row says who did what it records and not only that it happened.
/// <para>
/// It is two plain strings on purpose. The toolkit's own kinds are <see cref="ActedByKinds"/>; a package that
/// knows more about its callers adds kinds of its own, and a record written today is still readable when the
/// kinds have grown.
/// </para>
/// </summary>
/// <param name="Kind">What kind of actor it is: one of <see cref="ActedByKinds"/>, or a kind a package adds.</param>
/// <param name="Id">
/// Which actor of that kind: a user's id, the scope scoped system work runs in. <see langword="null"/> where
/// the kind has nobody to tell apart, as the system itself and an anonymous caller.
/// </param>
public readonly record struct ActedBy(string Kind, string? Id)
{
    /// <summary>
    /// Who <paramref name="user"/> is, as a record keeps it: a signed-in user by its id, scoped system work as the
    /// system with its scope, system work without a scope as the system, and a caller who has not signed in as
    /// anonymous. A user whose token names somebody by something else than a <see cref="Guid"/> is kept by the
    /// token's <c>sub</c> claim.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="user"/> is null.</exception>
    public static ActedBy From(CurrentUserSnapshot user)
    {
        if (user is null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        return user.Kind switch
        {
            CurrentUserKind.User => new(ActedByKinds.User, user.Id?.ToString("D") ?? user.FindClaim("sub")),
            CurrentUserKind.SystemIn => new(ActedByKinds.System, user.SystemScope),
            CurrentUserKind.System => new(ActedByKinds.System, null),
            _ => new(ActedByKinds.Anonymous, null),
        };
    }
}

/// <summary>
/// The kinds of actor the toolkit itself writes. A package adds kinds of its own next to them, for callers
/// only it can tell apart.
/// </summary>
public static class ActedByKinds
{
    /// <summary>A signed-in user; the id is the user's.</summary>
    public const string User = "user";

    /// <summary>The application itself; the id is the scope its work runs in, or nothing outside any.</summary>
    public const string System = "system";

    /// <summary>A caller who did not sign in; there is no id.</summary>
    public const string Anonymous = "anonymous";
}
