namespace DDDToolkit.Abstractions.Access;

/// <summary>
/// Who the application is acting for: a signed-in user, somebody who has not signed in, the application
/// itself, or the application at work inside a scope a module chose. A row access rule asks it about the
/// user, and Postgres is given the same answer on each connection a context opens, so a rule answers the
/// same in a handler as in a policy.
/// </summary>
/// <remarks>
/// In a rule, use the members, not the instance: <c>caller.UserId</c>, <c>caller.IsSignedIn</c>,
/// <c>caller.Role</c> and <c>caller.Claim("app_metadata.role")</c> are what the generator translates.
/// <para>
/// Hosts make callers; a rule only reads one. <c>Callers.FromClaims</c> in the <c>DDDToolkit</c> package
/// makes a user of a validated access token's claims, and <c>DDDToolkit.Auth.Supabase</c> does that for
/// every request that carries a Supabase access token.
/// </para>
/// </remarks>
public sealed class Caller
{
    private readonly Func<string, string?> _claim;

    private Caller(CallerKind kind, Guid? userId, string? role, string? claims, Func<string, string?>? claim, string? scope = null)
    {
        Kind = kind;
        UserId = userId;
        Role = role;
        Claims = claims;
        Scope = scope;
        _claim = claim ?? (static _ => null);
    }

    /// <summary>
    /// The application itself: background work nobody asked for in a request, such as the outbox. Row
    /// access rules are for callers, so the system is not subject to them.
    /// </summary>
    public static Caller System { get; } = new(CallerKind.System, userId: null, role: null, claims: null, claim: null);

    /// <summary>Somebody who has not signed in: no user, the role <c>anon</c>, no claims.</summary>
    public static Caller Anonymous { get; } = new(CallerKind.Anonymous, userId: null, role: "anon", claims: null, claim: null);

    /// <summary>
    /// System work confined by row level security: the application acting inside a scope a module chose,
    /// on a role that cannot bypass the policies, <c>ddd_system_in</c> unless the host configures another.
    /// The scope travels to the database in the caller's claims, <c>{"role":"ddd_system_in","scope":"…"}</c>,
    /// where a module's policies may ask it; whatever else confines the work is what the module puts on the
    /// connection next to the claims.
    /// <para>
    /// It is not <see cref="System"/>: <see cref="IsSystem"/> is false for it, and nothing that treats the
    /// system as above the rules treats this caller so.
    /// </para>
    /// </summary>
    /// <param name="scope">
    /// The scope, in lower case letters, digits, <c>_</c> and <c>-</c>: a module's name, such as
    /// <c>projects</c>, so its own policies can tell its work from another module's.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is empty, white space, or has another character.</exception>
    public static Caller SystemIn(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("A scoped system caller needs a scope, such as the name of the module whose work it is.", nameof(scope));
        }

        foreach (var character in scope)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-'))
            {
                throw new ArgumentException(
                    $"The scope '{scope}' has a character a scope cannot have. A scope is lower case letters, digits, '_' and '-', such as a module's name.",
                    nameof(scope));
            }
        }

        return new(CallerKind.SystemIn, userId: null, role: null, claims: null, claim: null, scope);
    }

    /// <summary>A signed-in user.</summary>
    /// <param name="userId">The user's id, the token's <c>sub</c>; <see langword="null"/> when that is not a <see cref="Guid"/>.</param>
    /// <param name="role">
    /// The role the user's token carries: <c>authenticated</c>, unless the identity provider gives the user a
    /// role of its own. Which database role such a role runs as is for the host's list to say, and a role on
    /// no list runs as none.
    /// </param>
    /// <param name="claim">Reads a claim by its path, as <see cref="Claim"/> describes; none when left out.</param>
    /// <param name="claims">
    /// The payload of the user's validated access token, as JSON, which the database reads its claims
    /// from. Left out, the database sees only <paramref name="userId"/> and <paramref name="role"/>.
    /// </param>
    public static Caller User(Guid? userId, string? role = "authenticated", Func<string, string?>? claim = null, string? claims = null)
        => new(CallerKind.User, userId, role, claims, claim);

    /// <summary>Which kind of caller this is.</summary>
    public CallerKind Kind { get; }

    /// <summary>
    /// Whether this is the application itself, which row access rules do not apply to. A
    /// <see cref="SystemIn"/> caller is not: its work stays inside the policies.
    /// </summary>
    public bool IsSystem => Kind == CallerKind.System;

    /// <summary>Whether this is the application at work inside a scope, <see cref="SystemIn"/>.</summary>
    public bool IsSystemIn => Kind == CallerKind.SystemIn;

    /// <summary>The scope of a <see cref="SystemIn"/> caller; <see langword="null"/> for every other kind.</summary>
    public string? Scope { get; }

    /// <summary>The signed-in user's id, <c>auth.uid()</c>; <see langword="null"/> when nobody signed in.</summary>
    public Guid? UserId { get; }

    /// <summary>Whether somebody signed in: <c>auth.uid() IS NOT NULL</c>.</summary>
    public bool IsSignedIn => UserId.HasValue;

    /// <summary>
    /// The role the caller's token carries, which is what <c>auth.role()</c> answers: <c>authenticated</c> or
    /// <c>anon</c>, or a role of its own that an identity provider gave a user. It is not the database role
    /// the caller's queries run as: the host maps one to the other, and refuses a role it did not list.
    /// <see langword="null"/> for a token without a role claim, which counts as a signed-in user's, and for
    /// the two system kinds, whose roles are the host's to configure.
    /// </summary>
    public string? Role { get; }

    /// <summary>
    /// The payload of a user's validated access token, as JSON: what the database reads <c>auth.jwt()</c>
    /// from. <see langword="null"/> for <see cref="System"/> and <see cref="Anonymous"/>, for a user
    /// made without one, and for <see cref="SystemIn"/>, whose claims the database is given from its role and
    /// scope. Rules read claims with <see cref="Claim"/>, not this.
    /// </summary>
    public string? Claims { get; }

    /// <summary>
    /// A claim of the caller's token, as text: <c>Claim("email")</c> is <c>auth.jwt() -&gt;&gt; 'email'</c>, and a
    /// path into nested claims, <c>Claim("app_metadata.role")</c>, is <c>auth.jwt() #&gt;&gt; '{app_metadata,role}'</c>.
    /// <see langword="null"/> when the token has no such claim. Rules about roles and teams read
    /// <c>app_metadata</c>, which only the server can change; a user edits their own <c>user_metadata</c>.
    /// </summary>
    public string? Claim(string path) => _claim(path);

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        CallerKind.System => "system",
        CallerKind.SystemIn => $"system in {Scope}",
        CallerKind.User when UserId is { } user => $"{Role} {user}",
        _ => Role ?? Kind.ToString(),
    };
}

/// <summary>The kinds of <see cref="Caller"/>.</summary>
public enum CallerKind
{
    /// <summary>The application itself, with no request behind it.</summary>
    System,

    /// <summary>A request without a user.</summary>
    Anonymous,

    /// <summary>A request from a signed-in user.</summary>
    User,

    /// <summary>The application at work inside a scope, confined by row level security: <see cref="Caller.SystemIn"/>.</summary>
    SystemIn,
}
