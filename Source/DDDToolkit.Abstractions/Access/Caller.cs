namespace DDDToolkit.Abstractions.Access;

/// <summary>
/// Who the application is acting for: a signed-in user, somebody who has not signed in, or the
/// application itself. A row access rule asks it about the user, and Postgres gets the same answer on
/// every connection a context opens, so a rule answers the same in a handler as in a policy.
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

    private Caller(CallerKind kind, Guid? userId, string? role, string? claims, Func<string, string?>? claim)
    {
        Kind = kind;
        UserId = userId;
        Role = role;
        Claims = claims;
        _claim = claim ?? (static _ => null);
    }

    /// <summary>
    /// The application itself: background work nobody asked for in a request, such as the outbox. Row
    /// access rules are for callers, so the system is not subject to them.
    /// </summary>
    public static Caller System { get; } = new(CallerKind.System, userId: null, role: null, claims: null, claim: null);

    /// <summary>Somebody who has not signed in: no user, the role <c>anon</c>, no claims.</summary>
    public static Caller Anonymous { get; } = new(CallerKind.Anonymous, userId: null, role: "anon", claims: null, claim: null);

    /// <summary>A signed-in user.</summary>
    /// <param name="userId">The user's id, the token's <c>sub</c>; <see langword="null"/> when that is not a <see cref="Guid"/>.</param>
    /// <param name="role">The role the database gives the user: <c>authenticated</c>.</param>
    /// <param name="claim">Reads a claim by its path, as <see cref="Claim"/> describes; none when left out.</param>
    /// <param name="claims">
    /// The payload of the user's validated access token, as JSON, which the database reads its claims
    /// from. Left out, the database sees only <paramref name="userId"/> and <paramref name="role"/>.
    /// </param>
    public static Caller User(Guid? userId, string? role = "authenticated", Func<string, string?>? claim = null, string? claims = null)
        => new(CallerKind.User, userId, role, claims, claim);

    /// <summary>Which of the three this is.</summary>
    public CallerKind Kind { get; }

    /// <summary>Whether this is the application itself, which row access rules do not apply to.</summary>
    public bool IsSystem => Kind == CallerKind.System;

    /// <summary>The signed-in user's id, <c>auth.uid()</c>; <see langword="null"/> when nobody signed in.</summary>
    public Guid? UserId { get; }

    /// <summary>Whether somebody signed in: <c>auth.uid() IS NOT NULL</c>.</summary>
    public bool IsSignedIn => UserId.HasValue;

    /// <summary>The role the database gives the caller, <c>auth.role()</c>: <c>authenticated</c> or <c>anon</c>.</summary>
    public string? Role { get; }

    /// <summary>
    /// The payload of a user's validated access token, as JSON: what the database reads <c>auth.jwt()</c>
    /// from. <see langword="null"/> for <see cref="System"/> and <see cref="Anonymous"/>, and for a user
    /// made without one. Rules read claims with <see cref="Claim"/>, not this.
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
        CallerKind.User when UserId is { } user => $"{Role} {user}",
        _ => Role ?? Kind.ToString(),
    };
}

/// <summary>The three kinds of <see cref="Caller"/>.</summary>
public enum CallerKind
{
    /// <summary>The application itself, with no request behind it.</summary>
    System,

    /// <summary>A request without a user.</summary>
    Anonymous,

    /// <summary>A request from a signed-in user.</summary>
    User,
}
