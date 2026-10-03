namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// The roles a row access rule's <c>To</c> can name by what they are for, rather than by what a database
/// calls them. The rule compiles with the symbol, and the role it stands for is chosen when the policies
/// are written, from what the host configured, so no role name is baked into a compiled module:
/// <code>
/// [RowAccess&lt;Order&gt;(RowOperations.Read, To = [RowAccessRoles.User])]
/// public static partial class SignedInCustomersSeeTheirOrders { ... }
/// </code>
/// A role's own name in <c>To</c> keeps working and is written as it is spelled. <c>PUBLIC</c> is refused:
/// it is every role there is, the application's own and the system's included, which a policy never means.
/// <para>
/// A signed-in user whose token carries a role of its own, <c>analyst</c> say, runs as the database role the host mapped
/// that token role to, and a rule names it by the token role: <see cref="Token"/>, or
/// <see cref="TokenPrefix"/> and the token role where a constant is needed.
/// </para>
/// </summary>
public static class RowAccessRoles
{
    /// <summary>
    /// A signed-in user: the role a user's queries run as, <c>authenticated</c> unless the host configures
    /// another one.
    /// </summary>
    public const string User = "@user";

    /// <summary>
    /// A caller without a user: the role such a request's queries run as, <c>anon</c> unless the host
    /// configures another one.
    /// </summary>
    public const string Anonymous = "@anonymous";

    /// <summary>
    /// The application's own work that should stay inside the policies: <c>ddd_system_in</c> unless the host
    /// configures another role. It can neither log in nor bypass row level security, and the policies
    /// written for it make it where the database does not have it yet.
    /// </summary>
    public const string SystemIn = "@system-in";

    /// <summary>
    /// The application's own bookkeeping, where the role it logs in as holds nothing: the role that reads and
    /// marks outbox rows, writes inbox rows, checks the migration history and deletes rows that are old enough
    /// to go. It is the role the host configured for the system caller, and there is none unless the host
    /// configured one: a script refuses a rule or a contribution that names it while none is configured.
    /// </summary>
    public const string System = "@system";

    /// <summary>
    /// What the symbolic name of a token role starts with: <c>@token:analyst</c> is the database role the host
    /// mapped the token role <c>analyst</c> to. An attribute takes constants only, so a rule's <c>To</c> writes it
    /// as one:
    /// <code>
    /// public const string Analyst = RowAccessRoles.TokenPrefix + "analyst";
    ///
    /// [RowAccess&lt;Order&gt;(RowOperations.Read, To = [Analyst])]
    /// public static partial class AnalystsReadEveryOrder { ... }
    /// </code>
    /// </summary>
    public const string TokenPrefix = "@token:";

    /// <summary>
    /// A signed-in user whose token carries the role <paramref name="tokenRole"/>: the database role the host
    /// mapped that token role to, which is the role such a user's queries run as. A script refuses a rule for a
    /// token role the host did not map, since no query would ever run as it.
    /// </summary>
    /// <param name="tokenRole">The role as the token's <c>role</c> claim spells it, <c>analyst</c>.</param>
    /// <returns><see cref="TokenPrefix"/> and the token role: <c>@token:analyst</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="tokenRole"/> is null, empty or white space.</exception>
    public static string Token(string tokenRole)
        => string.IsNullOrWhiteSpace(tokenRole)
            ? throw new ArgumentException("A token role has a name: the value of the token's role claim.", nameof(tokenRole))
            : TokenPrefix + tokenRole;
}
