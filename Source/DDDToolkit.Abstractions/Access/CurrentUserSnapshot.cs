#if NET
using System.Text.Json;
#endif

namespace DDDToolkit.Abstractions.Access;

/// <summary>
/// Who a flow of work runs as at one moment: a signed-in user, somebody who has not signed in, system work, or
/// system work inside a scope a module chose. <see cref="ICurrentUser"/> answers from the snapshot that is current.
/// A host hands one over for each request, and <see cref="ICurrentUser.BeginAs(CurrentUserSnapshot)"/> makes one
/// current for a flow of work.
/// </summary>
/// <remarks>
/// Application code asks <see cref="ICurrentUser"/>. A snapshot is for code that keeps the answer or passes it on: a
/// host that says who a request runs as, row level security that sets the user on a connection, and a test that runs
/// as a user with claims of its own.
/// <para>
/// A snapshot has the members <see cref="ICurrentUser"/> has, with the same answers. Postgres is given the same
/// answer on each connection a context opens, so a row access rule answers the same in a handler as in a policy.
/// </para>
/// <para>
/// <c>FromClaims</c> makes a user of a validated access token's claims. <c>DDDToolkit.Auth.Supabase</c> does
/// that for every request that carries a Supabase access token.
/// </para>
/// </remarks>
public sealed class CurrentUserSnapshot
{
    private readonly Func<string, string?> _claim;

    private CurrentUserSnapshot(CurrentUserKind kind, Guid? id, string? role, string? claims, Func<string, string?>? claim, string? scope = null)
    {
        Kind = kind;
        Id = id;
        Role = role;
        Claims = claims;
        SystemScope = scope;
        _claim = claim ?? (static _ => null);
    }

    /// <summary>
    /// System work with no scope, such as the outbox. Row access rules apply to users, so this work is not subject to
    /// them. On Postgres it runs as the host's system role.
    /// </summary>
    public static CurrentUserSnapshot System { get; } = new(CurrentUserKind.System, id: null, role: null, claims: null, claim: null);

    /// <summary>Somebody who has not signed in. There is no user id, the role is <c>anon</c>, and there are no claims.</summary>
    public static CurrentUserSnapshot Anonymous { get; } = new(CurrentUserKind.Anonymous, id: null, role: "anon", claims: null, claim: null);

    /// <summary>
    /// System work inside a scope a module chose. Row level security confines it. On Postgres it runs as a role that
    /// cannot bypass the policies, <c>ddd_system_in</c> unless the host configures another. The scope reaches the
    /// database in the claims, <c>{"role":"ddd_system_in","scope":"…"}</c>, where a module's policies may read it. A
    /// module puts whatever else confines the work on the connection next to the claims.
    /// <para>
    /// Code that treats system work as above the rules asks for <see cref="CurrentUserKind.System"/>, and this kind is
    /// <see cref="CurrentUserKind.SystemIn"/>. <see cref="IsSystem"/> is true for both.
    /// </para>
    /// </summary>
    /// <param name="scope">
    /// The scope, in lower case letters, digits, <c>_</c> and <c>-</c>. Use a module's name, such as <c>projects</c>,
    /// so the module's policies can tell its work from another module's.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is empty, white space, or has another character.</exception>
    public static CurrentUserSnapshot SystemIn(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("Scoped system work needs a scope, such as the name of the module whose work it is.", nameof(scope));
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

        return new(CurrentUserKind.SystemIn, id: null, role: null, claims: null, claim: null, scope);
    }

    /// <summary>A signed-in user.</summary>
    /// <param name="id">The user's id, the token's <c>sub</c>. <see langword="null"/> when the <c>sub</c> is not a <see cref="Guid"/>.</param>
    /// <param name="role">
    /// The role the user's token carries. It is <c>authenticated</c> unless the identity provider gives the user a role
    /// of its own. The host's list says which database role such a role runs as. A role on no list runs as none.
    /// </param>
    /// <param name="claim">Reads a claim by its path, as <see cref="FindClaim"/> describes. Left out, the user has no claims to read.</param>
    /// <param name="claims">
    /// The payload of the user's validated access token, as JSON. The database reads its claims from it. Left out, the
    /// database sees only <paramref name="id"/> and <paramref name="role"/>.
    /// </param>
    public static CurrentUserSnapshot User(Guid? id, string? role = "authenticated", Func<string, string?>? claim = null, string? claims = null)
        => new(CurrentUserKind.User, id, role, claims, claim);

#if NET
    /// <summary>
    /// The user an access token names, from its claims. The <c>sub</c> claim is the user's id and the <c>role</c> claim
    /// the role. <see cref="FindClaim"/> reads the other claims. The claims go to the database unchanged, so
    /// <c>auth.jwt()</c> there answers what the token says.
    /// <para>
    /// A token without a <c>role</c> claim, or with <c>null</c> for one, gives a user without a role. A <c>role</c> that
    /// is not text becomes the role as the token writes it. No host lists such a role.
    /// </para>
    /// </summary>
    /// <param name="claims">
    /// The payload of an access token that was validated already: the JSON object between the two dots, decoded. This
    /// method checks no signature. Pass only claims that came out of a validated token.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="claims"/> is empty or not a JSON object.</exception>
    public static CurrentUserSnapshot FromClaims(string claims)
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

        return User(user, role, path => ClaimOf(root, path), claims);
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
#endif

    /// <summary>The kind of user this is.</summary>
    public CurrentUserKind Kind { get; }

    /// <summary>
    /// The signed-in user's id, which <c>auth.uid()</c> answers. <see langword="null"/> for system work, for somebody
    /// who has not signed in, and for a token whose <c>sub</c> is no <see cref="Guid"/>.
    /// </summary>
    public Guid? Id { get; }

    /// <summary>Whether a user signed in, which is <c>auth.uid() IS NOT NULL</c>. It is true when <see cref="Id"/> has a value.</summary>
    public bool IsAuthenticated => Id.HasValue;

    /// <summary>
    /// Whether this is system work, with a scope or without one. <see cref="Kind"/> tells the two apart. Row level
    /// security lets only <see cref="CurrentUserKind.System"/> past the policies.
    /// </summary>
    public bool IsSystem => Kind is CurrentUserKind.System or CurrentUserKind.SystemIn;

    /// <summary>The scope of scoped system work, <see cref="SystemIn"/>. <see langword="null"/> for every other kind.</summary>
    public string? SystemScope { get; }

    /// <summary>
    /// The role the user's token carries, which <c>auth.role()</c> answers. It is <c>authenticated</c>, <c>anon</c>, or a
    /// role that an identity provider gave the user. The database role the queries run as is a separate thing. The host
    /// maps one to the other, and refuses a role it did not list. <see langword="null"/> for a token without a role
    /// claim, which counts as a signed-in user's, and for system work, whose roles the host configures.
    /// </summary>
    public string? Role { get; }

    /// <summary>
    /// The payload of a user's validated access token, as JSON. The database reads <c>auth.jwt()</c> from it.
    /// <see langword="null"/> for system work, for somebody who has not signed in, and for a user made without one.
    /// The database is given the claims of scoped system work from its role and scope. Code reads a claim with
    /// <see cref="FindClaim"/>.
    /// </summary>
    public string? Claims { get; }

    /// <summary>
    /// A claim of the user's token, as text. <c>FindClaim("email")</c> is <c>auth.jwt() -&gt;&gt; 'email'</c>. A path
    /// into nested claims, <c>FindClaim("app_metadata.role")</c>, is <c>auth.jwt() #&gt;&gt; '{app_metadata,role}'</c>.
    /// <see langword="null"/> when the token has no such claim. Rules about roles and teams read <c>app_metadata</c>,
    /// which only the server can change. A user can edit their own <c>user_metadata</c>.
    /// </summary>
    /// <param name="path">The claim's name, or a path of names separated by dots.</param>
    public string? FindClaim(string path) => _claim(path);

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        CurrentUserKind.System => "system",
        CurrentUserKind.SystemIn => $"system in {SystemScope}",
        CurrentUserKind.User when Id is { } user => $"{Role} {user}",
        _ => Role ?? Kind.ToString(),
    };
}

/// <summary>The kinds of <see cref="CurrentUserSnapshot"/>, which <see cref="ICurrentUser.Kind"/> answers.</summary>
public enum CurrentUserKind
{
    /// <summary>System work with no scope and no request behind it.</summary>
    System,

    /// <summary>A request without a user.</summary>
    Anonymous,

    /// <summary>A request from a signed-in user.</summary>
    User,

    /// <summary>System work inside a scope, which row level security confines: <see cref="CurrentUserSnapshot.SystemIn"/>.</summary>
    SystemIn,
}
