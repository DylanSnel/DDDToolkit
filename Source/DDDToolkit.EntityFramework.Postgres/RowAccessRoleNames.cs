using System.Collections.ObjectModel;
using System.Text;
using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// The database roles a script writes for the symbolic roles a rule names in <c>To</c>:
/// <see cref="RowAccessRoles.User"/>, <see cref="RowAccessRoles.Anonymous"/>,
/// <see cref="RowAccessRoles.SystemIn"/>, <see cref="RowAccessRoles.Token"/> for each token role the host
/// mapped, <see cref="TokenRoles"/>, and <see cref="RowAccessRoles.System"/> where the host gave its own
/// bookkeeping a role, <see cref="System"/>. A rule compiles with the symbols, so the same module writes the
/// right roles for every host, whatever it calls them.
/// </summary>
/// <param name="User">The role a signed-in user's queries run as.</param>
/// <param name="Anonymous">The role the queries of a request without a user run as.</param>
/// <param name="SystemIn">The role a scoped system caller's queries run as: the application's own work that stays inside the policies.</param>
/// <exception cref="ArgumentException">
/// A role is empty, <c>PUBLIC</c>, <c>none</c>, symbolic itself, one of Postgres's own <c>pg_</c> roles, has a <c>$</c>
/// or is longer than Postgres keeps a name, or <paramref name="SystemIn"/> is the user's or the anonymous
/// caller's role.
/// </exception>
public sealed record RowAccessRoleNames(string User, string Anonymous, string SystemIn)
{
    /// <summary>How long a name Postgres keeps: it cuts a longer one, so it would name another role than the one asked about.</summary>
    private const int MaxNameBytes = 63;

    private readonly string _user = Configured(User, nameof(User));

    private readonly string _anonymous = Configured(Anonymous, nameof(Anonymous));

    private readonly string _systemIn = OwnRole(Configured(SystemIn, nameof(SystemIn)), User, Anonymous);

    /// <summary>The token roles of a host that mapped none. Declared before <see cref="Default"/>, which is made with it.</summary>
    private static readonly IReadOnlyDictionary<string, string> NoTokenRoles
        = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(0, StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<string, string> _tokenRoles = NoTokenRoles;

    private readonly string? _system;

    /// <summary>PostgREST's and Supabase's roles, <c>authenticated</c> and <c>anon</c>, and <c>ddd_system_in</c>.</summary>
    public static RowAccessRoleNames Default { get; } = new(
        PostgresRowLevelSecurityOptions.AuthenticatedRole,
        PostgresRowLevelSecurityOptions.AnonRole,
        PostgresRowLevelSecurityOptions.DefaultSystemInRole);

    // The accessors check each role on its own. Whether the scoped system role is one of its own is checked
    // by the constructor, and again where the roles are used, because a with-expression sets its roles one
    // at a time: `with { User = "ddd_system_in", SystemIn = "worker" }` passes through a moment where two
    // are the same, although it ends where none are. Whether a token role is mapped to the scoped system role
    // or the anonymous caller's is checked where the roles are used, for the same reason.

    /// <summary>The role a signed-in user's queries run as, <see cref="RowAccessRoles.User"/> in a rule.</summary>
    public string User
    {
        get => _user;
        init => _user = Configured(value, nameof(User));
    }

    /// <summary>The role the queries of a request without a user run as, <see cref="RowAccessRoles.Anonymous"/> in a rule.</summary>
    public string Anonymous
    {
        get => _anonymous;
        init => _anonymous = Configured(value, nameof(Anonymous));
    }

    /// <summary>
    /// The role a scoped system caller's queries run as, the application's own work that stays inside the
    /// policies: <see cref="RowAccessRoles.SystemIn"/> in a rule. A script whose policies name it makes it where the
    /// database does not have it, and refuses one that could log in or bypass row level security.
    /// </summary>
    public string SystemIn
    {
        get => _systemIn;
        init => _systemIn = Configured(value, nameof(SystemIn));
    }

    /// <summary>
    /// The token roles a signed-in user's token may carry, each with the database role its queries run as:
    /// <see cref="RowAccessRoles.Token"/> in a rule. A script whose policies name a mapped role makes it where
    /// the database does not have it, and refuses one that could log in or bypass row level security. Empty by
    /// default; a rule for a token role that is not here is refused, naming the rule.
    /// </summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    /// <exception cref="ArgumentException">A token role has no name, or the role it is mapped to cannot be a policy's.</exception>
    public IReadOnlyDictionary<string, string> TokenRoles
    {
        get => _tokenRoles;
        init => _tokenRoles = Mapped(value);
    }

    /// <summary>
    /// The role the application's own bookkeeping runs as, <see cref="RowAccessRoles.System"/> in a rule or a
    /// contribution, where the role the application logs in as holds nothing: the role the system caller
    /// switches to, <see cref="PostgresRowLevelSecurityOptions.SystemRole"/>. <see langword="null"/> by default,
    /// and then a script says nothing about it: the system caller is the login role itself, or a role of the
    /// host's own making.
    /// <para>
    /// A script that names it, because <see cref="RowAccessExport.WriteGrants"/> is on or a rule or a
    /// contribution is for <see cref="RowAccessRoles.System"/>, makes the role where the database does not have
    /// it, without a login and without <c>BYPASSRLS</c>, and fails where a role of that name could log in or
    /// bypass row level security, is a superuser, has the privileges of a role that owns tables, or is granted
    /// to a role callers run as. With <see cref="RowAccessExport.WriteGrants"/> it gets the privileges the
    /// toolkit's bookkeeping needs and nothing else: on the outbox and inbox tables, the migration history, and
    /// the rows of an event log that may go. A script that does not name it is the same with or without it.
    /// </para>
    /// <para>
    /// It is a role of its own: never the user's, the anonymous caller's or the scoped system role, and never
    /// a role a token role is mapped to, which is checked where a script names it. A host whose system caller
    /// runs as a role that bypasses row level security, Supabase's <c>service_role</c> for one, leaves this
    /// <see langword="null"/>: <c>RowAccessRoleNames.From(options) with { System = null }</c>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Set to a role that cannot be configured: empty, <c>PUBLIC</c>, <c>none</c>, symbolic, one of Postgres's
    /// own <c>pg_</c> roles, with a <c>$</c>, or longer than Postgres keeps a name.
    /// </exception>
    public string? System
    {
        get => _system;
        init => _system = value is null ? null : Configured(value, nameof(System));
    }

    /// <summary>
    /// The roles the interceptor switches to with <paramref name="options"/>, so the policies are written
    /// for the roles the queries run as:
    /// <see cref="PostgresRowLevelSecurityOptions.UserRole"/>,
    /// <see cref="PostgresRowLevelSecurityOptions.AnonymousRole"/>,
    /// <see cref="PostgresRowLevelSecurityOptions.SystemInRole"/>,
    /// <see cref="PostgresRowLevelSecurityOptions.TokenRoles"/> and, as <see cref="System"/>,
    /// <see cref="PostgresRowLevelSecurityOptions.SystemRole"/>. Where the scoped system role is
    /// <see langword="null"/>, a rule for <see cref="RowAccessRoles.SystemIn"/> is still written, for
    /// <c>ddd_system_in</c>, and its script makes that role.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">A role of <paramref name="options"/> cannot be a policy's.</exception>
    public static RowAccessRoleNames From(PostgresRowLevelSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        return new(options.UserRole, options.AnonymousRole, options.SystemInRole ?? PostgresRowLevelSecurityOptions.DefaultSystemInRole)
        {
            TokenRoles = new Dictionary<string, string>(options.TokenRoles, StringComparer.Ordinal),
            System = options.SystemRole,
        };
    }

    /// <summary>
    /// The database role <paramref name="role"/> stands for: the configured role for a symbolic one, and a
    /// role's own name as it is.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="role"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="role"/> is empty, <c>PUBLIC</c> in any case, starts with <c>@</c> and is none of
    /// <see cref="RowAccessRoles"/>, names a token role that is not mapped, has a <c>$</c>, or is longer than
    /// Postgres keeps a name.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="SystemIn"/> is the user's or the anonymous caller's role, or a token role is mapped to the scoped
    /// system role or the anonymous caller's.
    /// </exception>
    public string Resolve(string role)
    {
        ArgumentNullException.ThrowIfNull(role);
        EnsureOwnRoles();

        return TryResolve(role, out var resolved, out var problem) ? resolved : throw new ArgumentException(problem, nameof(role));
    }

    /// <summary>
    /// <see cref="Resolve"/> without the exception: the role, or why a policy cannot be for
    /// <paramref name="role"/>, for a message that names the rule as well.
    /// </summary>
    internal bool TryResolve(string role, out string resolved, out string problem)
    {
        if (role.StartsWith(RowAccessRoles.TokenPrefix, StringComparison.Ordinal))
        {
            var tokenRole = role[RowAccessRoles.TokenPrefix.Length..];
            if (_tokenRoles.TryGetValue(tokenRole, out var mapped))
            {
                resolved = mapped;
                problem = "";
                return true;
            }

            resolved = role;
            problem = $"'{role}' is the token role '{tokenRole}', which is mapped to no database role, so no query ever runs as it. " +
                      $"Map it in PostgresRowLevelSecurityOptions.TokenRoles and write the script with RowAccessRoleNames.From(options), or, for the Supabase export, add 'token:{tokenRole}=<role>' to SupabaseRowAccessRoles.";
            return false;
        }

        if (string.Equals(role, RowAccessRoles.System, StringComparison.Ordinal))
        {
            resolved = _system ?? role;
            problem = _system is null
                ? $"'{role}' is the role the application's own bookkeeping runs as, and none is configured. " +
                  "Set PostgresRowLevelSecurityOptions.SystemRole and write the script with RowAccessRoleNames.From(options), or set RowAccessRoleNames.System; for the Supabase export, add 'system=<role>' to SupabaseRowAccessRoles."
                : NotABookkeepingRole() ?? "";
            return problem.Length == 0;
        }

        resolved = role switch
        {
            RowAccessRoles.User => User,
            RowAccessRoles.Anonymous => Anonymous,
            RowAccessRoles.SystemIn => SystemIn,
            _ => role,
        };
        problem = role is RowAccessRoles.User or RowAccessRoles.Anonymous or RowAccessRoles.SystemIn ? "" : NotARole(role) ?? "";

        return problem.Length == 0;
    }

    /// <summary>
    /// Why <see cref="System"/> cannot be the bookkeeping role of a script; <see langword="null"/> when it can, and
    /// when none is set. It holds the outbox, the inbox and the migration history, so it is no role a caller runs
    /// as. Asked where a script names the role, so a host whose system caller runs as a role of another kind is
    /// not refused a script that never mentions it.
    /// </summary>
    internal string? NotABookkeepingRole()
    {
        if (_system is not { } system)
        {
            return null;
        }

        var shared = string.Equals(system, _user, StringComparison.Ordinal) ? "the role of a signed-in user"
            : string.Equals(system, _anonymous, StringComparison.Ordinal) ? "the role of an anonymous caller"
            : string.Equals(system, _systemIn, StringComparison.Ordinal) ? "the scoped system role"
            : _tokenRoles.FirstOrDefault(pair => string.Equals(pair.Value, system, StringComparison.Ordinal)) is { Key: { } tokenRole } ? $"the role of the token role '{tokenRole}'"
            : null;

        return shared is null
            ? null
            : $"'{system}' is the role the application's own bookkeeping runs as, and {shared} as well. It holds the outbox, the inbox and the migration history, which no caller may reach, so it is a role of its own.";
    }

    /// <summary>
    /// The database roles token roles are mapped to, each once and in order, without the user's own role: the
    /// roles a script makes and checks next to the scoped system role. A token role mapped to the user's role is
    /// that role, which is the host's to make.
    /// </summary>
    internal IReadOnlyList<string> TokenDatabaseRoles
        => [.. _tokenRoles.Values.Where(role => !string.Equals(role, _user, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Throws when <see cref="SystemIn"/> is the user's or the anonymous caller's role, which a
    /// with-expression can leave behind, or when a token role is mapped to the scoped system role or the
    /// anonymous caller's; see the note on the accessors.
    /// </summary>
    /// <exception cref="InvalidOperationException">It is.</exception>
    internal void EnsureOwnRoles()
    {
        if (SharedSystemIn(_systemIn, _user, _anonymous) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        foreach (var (tokenRole, role) in _tokenRoles)
        {
            if (NotAMappedRole(tokenRole, role, _anonymous, _systemIn) is { } unmappable)
            {
                throw new InvalidOperationException(unmappable);
            }
        }
    }

    /// <summary>
    /// Why the token role <paramref name="tokenRole"/> cannot be mapped to <paramref name="role"/>;
    /// <see langword="null"/> when it can. A token's holder runs as the mapped role, so that is never a role only
    /// the application's own work runs as, nor the role of callers who did not sign in.
    /// </summary>
    internal static string? NotAMappedRole(string? tokenRole, string? role, string anonymous, string systemIn)
    {
        if (NotMappable(tokenRole, role) is { } problem)
        {
            return problem;
        }

        var mapping = $"The token role '{tokenRole}' is mapped to '{role}'.";
        if (string.Equals(role, systemIn, StringComparison.Ordinal))
        {
            return $"{mapping} That is the scoped system role, which only the application's own work runs as: its policies would let the holder of a token do that work.";
        }

        return string.Equals(role, anonymous, StringComparison.Ordinal)
            ? $"{mapping} That is the role of callers who did not sign in: a rule for them would hold for the holders of this token role as well, with their identity. Map it to a role of its own."
            : null;
    }

    /// <summary>
    /// Why <paramref name="tokenRole"/> and <paramref name="role"/> cannot be a mapping, each looked at on its
    /// own: a token role has a name, and the role it is mapped to is one a host may configure.
    /// </summary>
    private static string? NotMappable(string? tokenRole, string? role)
    {
        if (string.IsNullOrWhiteSpace(tokenRole))
        {
            return $"A token role mapped to '{role}' has no name. A token role is the value of a token's role claim, such as 'analyst'.";
        }

        return NotAConfiguredRole(role ?? string.Empty) is { } problem ? $"The token role '{tokenRole}' is mapped to '{role}'. {problem}" : null;
    }

    /// <summary>
    /// Why <paramref name="role"/> cannot be a role a host configures, the user's, the anonymous caller's
    /// or the scoped system role, worded for whichever setting names it; <see langword="null"/> when it can.
    /// </summary>
    internal static string? NotAConfiguredRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return "A role has a name; an empty one is none.";
        }

        if (role.StartsWith('@'))
        {
            return $"'{role}' starts with '@', as only the symbolic roles of a rule's To do; a configured role is the name the database has for it.";
        }

        if (string.Equals(role, "public", StringComparison.OrdinalIgnoreCase))
        {
            return "PUBLIC is every role there is, the application's own and the system's included, and no policy is ever for it.";
        }

        // Setting the role to none is RESET ROLE: the caller would run as the role the application logged in as.
        if (string.Equals(role, "none", StringComparison.OrdinalIgnoreCase))
        {
            return "'none' is no role: switching to it goes back to the role the application logged in as, usually the tables' owner, past every policy. Name the role the database has.";
        }

        // Postgres refuses to make a role of that name itself, and the scoped system role is granted to
        // whoever runs a script: one of Postgres's predefined roles would hand that role its powers.
        return role.StartsWith("pg_", StringComparison.Ordinal)
            ? $"'{role}' starts with pg_, which Postgres keeps for its own roles."
            : Unwritable(role);
    }

    /// <summary>Why a policy cannot be for <paramref name="role"/>, a name that is not one of <see cref="RowAccessRoles"/>; <see langword="null"/> when it can.</summary>
    private static string? NotARole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return "A role has a name; an empty one is none.";
        }

        if (string.Equals(role, "public", StringComparison.OrdinalIgnoreCase))
        {
            return "A policy is never for PUBLIC: that is every role there is, the application's own and the system's included. Name the roles it is for, such as RowAccessRoles.User and RowAccessRoles.Anonymous.";
        }

        return role.StartsWith('@')
            ? $"'{role}' is not a role a rule can name. The symbolic roles are {RowAccessRoles.User}, {RowAccessRoles.Anonymous} and {RowAccessRoles.SystemIn}, {RowAccessRoles.System} for the bookkeeping role the host configured, and {RowAccessRoles.TokenPrefix}<role> for a token role the host mapped; any other role is named as the database spells it."
            : Unwritable(role);
    }

    /// <summary>Why a script cannot write <paramref name="role"/> so that it means that role; <see langword="null"/> when it can.</summary>
    private static string? Unwritable(string role)
    {
        if (role.Contains('$', StringComparison.Ordinal))
        {
            // Quoting does not help: inside a dollar-quoted block Postgres looks only for the closing tag.
            return $"'{role}' has a '$', which a script cannot write into the dollar-quoted blocks it puts role names in.";
        }

        return Encoding.UTF8.GetByteCount(role) > MaxNameBytes
            ? $"'{role}' is longer than the {MaxNameBytes} bytes of a name Postgres keeps, so the role it made would not be the one a script asks about."
            : null;
    }

    /// <summary>Why the scoped system role cannot be <paramref name="systemIn"/>; <see langword="null"/> when it can.</summary>
    internal static string? SharedSystemIn(string systemIn, string? user, string? anonymous)
        => string.Equals(systemIn, user, StringComparison.Ordinal) || string.Equals(systemIn, anonymous, StringComparison.Ordinal)
            ? $"'{systemIn}' is the role of a user or an anonymous caller as well, and the scoped system role must be one of its own: its policies would let those callers do what only the application's own work may."
            : null;

    /// <summary>
    /// A copy of <paramref name="tokenRoles"/> nobody can change, in the order of the token roles, each checked
    /// on its own: a token role has a name, and the role it is mapped to is one a host may configure.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Mapped(IReadOnlyDictionary<string, string> tokenRoles)
    {
        ArgumentNullException.ThrowIfNull(tokenRoles, nameof(TokenRoles));

        var mapped = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (tokenRole, role) in tokenRoles)
        {
            if (NotMappable(tokenRole, role) is { } problem)
            {
                throw new ArgumentException(problem, nameof(TokenRoles));
            }

            mapped[tokenRole] = role;
        }

        return mapped.Count == 0 ? NoTokenRoles : new ReadOnlyDictionary<string, string>(mapped);
    }

    /// <summary>
    /// Whether <paramref name="other"/> names the same roles, the mapped token roles included: two sets of names
    /// are compared by what they say, however the map each holds was built.
    /// </summary>
    public bool Equals(RowAccessRoleNames? other)
        => other is not null
           && string.Equals(_user, other._user, StringComparison.Ordinal)
           && string.Equals(_anonymous, other._anonymous, StringComparison.Ordinal)
           && string.Equals(_systemIn, other._systemIn, StringComparison.Ordinal)
           && string.Equals(_system, other._system, StringComparison.Ordinal)
           && _tokenRoles.Count == other._tokenRoles.Count
           && _tokenRoles.All(pair => other._tokenRoles.TryGetValue(pair.Key, out var role) && string.Equals(role, pair.Value, StringComparison.Ordinal));

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_user, StringComparer.Ordinal);
        hash.Add(_anonymous, StringComparer.Ordinal);
        hash.Add(_systemIn, StringComparer.Ordinal);
        hash.Add(_system ?? string.Empty, StringComparer.Ordinal);
        foreach (var (tokenRole, role) in _tokenRoles)
        {
            hash.Add(tokenRole, StringComparer.Ordinal);
            hash.Add(role, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>The roles as a message shows them, the mapped token roles as <c>analyst=desk_analyst</c>.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("User = ").Append(_user).Append(", Anonymous = ").Append(_anonymous).Append(", SystemIn = ").Append(_systemIn)
            .Append(", TokenRoles = [").AppendJoin(", ", _tokenRoles.Select(pair => pair.Key + "=" + pair.Value)).Append(']');
        if (_system is not null)
        {
            builder.Append(", System = ").Append(_system);
        }

        return true;
    }

    /// <summary>A configured role: a database role's own name, which a symbolic one is not.</summary>
    private static string Configured(string role, string parameter)
    {
        ArgumentNullException.ThrowIfNull(role, parameter);

        return NotAConfiguredRole(role) is { } problem ? throw new ArgumentException(problem, parameter) : role;
    }

    /// <summary>The scoped system role, which must be one of its own.</summary>
    private static string OwnRole(string systemIn, string? user, string? anonymous)
        => SharedSystemIn(systemIn, user, anonymous) is { } problem ? throw new ArgumentException(problem, nameof(SystemIn)) : systemIn;
}
