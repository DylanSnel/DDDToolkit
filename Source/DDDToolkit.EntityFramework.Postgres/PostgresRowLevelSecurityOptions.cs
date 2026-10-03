using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Which Postgres role each kind of <see cref="Caller"/> gets. The defaults are PostgREST's, which is
/// also what every Supabase project has, so policies written for Supabase's Data API apply unchanged, and
/// <see cref="PostgresRowAccess.SetupScript"/> makes the same two on a Postgres of your own, together with
/// <see cref="SystemInRole"/> and the roles of <see cref="TokenRoles"/>.
/// <para>
/// A signed-in user's token says which role it is for, in its <c>role</c> claim, and that claim picks the
/// database role only through a list the host wrote: <c>authenticated</c>, or no claim at all, is
/// <see cref="UserRole"/>; a role in <see cref="TokenRoles"/> is the role it is mapped to; and a token that
/// names <see cref="UserRole"/> or <see cref="AnonymousRole"/> itself runs as that role. Any other role is
/// refused, <see cref="UnknownTokenRole"/>: a token never chooses a database role by naming it.
/// </para>
/// </summary>
public sealed class PostgresRowLevelSecurityOptions
{
    /// <summary>The role PostgREST, and so Supabase, gives a signed-in user.</summary>
    public const string AuthenticatedRole = "authenticated";

    /// <summary>The role PostgREST, and so Supabase, gives a request without a user.</summary>
    public const string AnonRole = "anon";

    /// <summary>The role a scoped system caller, <c>Caller.SystemIn(scope)</c>, runs as unless another is configured.</summary>
    public const string DefaultSystemInRole = "ddd_system_in";

    /// <summary>The role a signed-in user's queries run as. <c>authenticated</c> by default.</summary>
    public string UserRole { get; set; } = AuthenticatedRole;

    /// <summary>The role the queries of a request without a user run as. <c>anon</c> by default.</summary>
    public string AnonymousRole { get; set; } = AnonRole;

    /// <summary>
    /// The role a scoped system caller, <c>Caller.SystemIn(scope)</c>, runs as: the application's own work
    /// that stays inside the policies. <c>ddd_system_in</c> by default, a role that can neither log in nor
    /// bypass row level security. Its claims are <c>{"role":"ddd_system_in","scope":"…"}</c>, so a policy
    /// may ask the scope. <see cref="PostgresRowAccess.SetupScript"/> makes it, and a script written with
    /// <see cref="RowAccessRoleNames.Of"/> these options writes the policies of a rule for
    /// <c>RowAccessRoles.SystemIn</c> for it.
    /// <para>
    /// <see langword="null"/> leaves it out of <see cref="PostgresRowAccess.SetupScript"/>, and a scoped
    /// system caller then fails when a context opens a connection for it, rather than running as another
    /// role. It does not take the rules for <c>RowAccessRoles.SystemIn</c> out of a script:
    /// <see cref="RowAccessRoleNames.Of"/> writes those for <c>ddd_system_in</c>, and the script makes that
    /// role where it is missing.
    /// </para>
    /// </summary>
    public string? SystemInRole { get; set; } = DefaultSystemInRole;

    /// <summary>
    /// The role background work runs as, or <see langword="null"/>, the default, to leave it the role
    /// the application logged in as.
    /// <para>
    /// Logged in as the tables' owner, as most applications are, <see langword="null"/> keeps background
    /// work as it was: row level security does not apply to the owner. Logged in as a role of its own
    /// that may do nothing but switch roles, set this to a role with <c>BYPASSRLS</c>, Supabase's
    /// <c>service_role</c> for one, and background work gets exactly that.
    /// </para>
    /// <para>
    /// <b>Recommended where the login role holds nothing:</b> a role of the toolkit's own bookkeeping,
    /// <c>ddd_system</c> say, that can neither log in nor bypass row level security and holds only the outbox,
    /// the inbox, the migration history and the rows of an event log that may go. A script written with
    /// <see cref="RowAccessRoleNames.Of"/> these options and <see cref="RowAccessExport.WriteGrants"/> makes
    /// that role and gives it exactly those privileges; grant the role to the login role, and the system caller
    /// can do the toolkit's bookkeeping and nothing else. System work that reads a module's own tables then
    /// fails, which is the point: it runs as a scoped system caller, inside the policies, instead.
    /// </para>
    /// </summary>
    public string? SystemRole { get; set; }

    /// <summary>
    /// The roles a signed-in user's token may carry besides <c>authenticated</c>, each with the database role its
    /// queries run as: <c>TokenRoles["analyst"] = "desk_analyst"</c>. A token role is matched as the token spells it.
    /// <c>authenticated</c>, and a token without a role claim, run as <see cref="UserRole"/> unless mapped here.
    /// <para>
    /// <see cref="PostgresRowAccess.SetupScript"/> makes each mapped role as it makes the others, without a login
    /// and without <c>BYPASSRLS</c>, and a rule names one as <c>RowAccessRoles.Token("analyst")</c>, which a script
    /// written with <see cref="RowAccessRoleNames.Of"/> these options writes as the mapped role. A mapped role is
    /// never <see cref="SystemRole"/>, the scoped system role or <see cref="AnonymousRole"/>: a token would then
    /// hold what only the application's own work, or nobody in particular, may do.
    /// </para>
    /// <para>
    /// The interceptor reads the map once, when it is built. Empty by default, so only the roles of a signed-in
    /// user and of an anonymous caller ever run.
    /// </para>
    /// </summary>
    public IDictionary<string, string> TokenRoles { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// What a signed-in user gets whose token carries a role that is on no list. Refused by default, with
    /// <c>access.role-not-allowed</c>, before a context connects for it.
    /// </summary>
    public UnknownTokenRole UnknownTokenRole { get; set; } = UnknownTokenRole.Refuse;

    /// <summary>
    /// How long what the interceptor sets for a caller lasts: on the connection, for its session, or for one
    /// transaction at a time. <see cref="RowLevelSecurityScope.Connection"/> by default.
    /// <para>
    /// Set <see cref="RowLevelSecurityScope.Transaction"/> where a pooler hands each transaction whichever server
    /// connection is free, PgBouncer in transaction mode or Supabase's transaction pooler: a setting made for a
    /// session there reaches whoever is given that server connection next. Nothing guesses it from the
    /// connection string. It needs the procedure <c>ddd.use_caller</c>, which
    /// <see cref="PostgresRowAccess.SetupScript"/> and every script and access file make, and the role the
    /// application logs in as must be allowed to call it.
    /// </para>
    /// <para>The interceptor reads it once, when it is built.</para>
    /// </summary>
    public RowLevelSecurityScope Scope { get; set; } = RowLevelSecurityScope.Connection;

    /// <summary>
    /// The <c>statement_timeout</c> a caller's statements run under, per kind of caller:
    /// <c>StatementTimeouts[CallerKind.User] = TimeSpan.FromSeconds(8)</c>. A kind left out gets the login
    /// role's own again, its reset value, so a timeout never outlives the caller it was set for.
    /// <para>
    /// Postgres applies the settings stored on a role, <c>ALTER ROLE authenticated SET statement_timeout</c>, when
    /// that role logs in, not when a session switches to it. A caller's statements would otherwise run under
    /// the timeout of the role the application logged in as, whatever the caller's own role says.
    /// </para>
    /// <para>
    /// A user whose token's role is on no list, and who runs as an anonymous caller, gets the anonymous
    /// caller's. Empty by default, and the interceptor's statement is then what it always was. The interceptor
    /// reads the map once, when it is built.
    /// </para>
    /// </summary>
    public IDictionary<CallerKind, TimeSpan> StatementTimeouts { get; } = new Dictionary<CallerKind, TimeSpan>();

    /// <summary>
    /// The role <paramref name="caller"/> runs as; <c>none</c> means the role the application logged in as.
    /// Every kind has an arm of its own and nothing falls through to the system's role, so a kind added
    /// later fails here until it is given one, rather than running with the application's power.
    /// </summary>
    /// <param name="caller">Who is calling.</param>
    /// <param name="asAnonymous">
    /// Whether the caller is a signed-in user who runs as an anonymous caller after all, because its token's role
    /// is on no list and <see cref="UnknownTokenRole"/> says so: the database is then given the anonymous
    /// caller's claims, not the token's, and the modules' settings for an anonymous caller.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The caller is a scoped system caller and <see cref="SystemInRole"/> is <see langword="null"/>, or it is
    /// of a kind no role is configured for.
    /// </exception>
    /// <exception cref="RefusalException">
    /// The caller is a signed-in user whose token's role is on no list, and <see cref="UnknownTokenRole"/> refuses
    /// it: <see cref="ToolkitRefusals.RoleNotAllowed"/>.
    /// </exception>
    internal string RoleOf(Caller caller, out bool asAnonymous)
    {
        asAnonymous = false;
        return caller.Kind switch
        {
            CallerKind.User => RoleOfUser(caller.Role, out asAnonymous),
            CallerKind.Anonymous => AnonymousRole,
            CallerKind.System => SystemRole ?? "none",
            CallerKind.SystemIn => SystemInRole ?? throw new InvalidOperationException(
                $"The caller is {caller}, a scoped system caller, and PostgresRowLevelSecurityOptions.{nameof(SystemInRole)} is null, so there is no role for it to run as. " +
                $"Set {nameof(SystemInRole)}, '{DefaultSystemInRole}' by default, or do not begin Caller.SystemIn in this host."),
            _ => throw new InvalidOperationException(
                $"No role is configured for a caller of kind {caller.Kind}, so its queries cannot run. A kind of caller needs a role of its own before row level security can apply to it."),
        };
    }

    /// <summary>
    /// The database role of a signed-in user whose token carries <paramref name="tokenRole"/>, which only ever
    /// comes off a list: the map first, then the user's and the anonymous caller's own roles. The claim is
    /// compared, never used as the role, so a token that names the system's role, or any role the database
    /// happens to have, does not run as it.
    /// </summary>
    /// <exception cref="RefusalException">The role is on no list, and <see cref="UnknownTokenRole"/> refuses it.</exception>
    private string RoleOfUser(string? tokenRole, out bool asAnonymous)
    {
        asAnonymous = false;

        // PostgREST's name for a signed-in user, whatever the host calls the role: a caller made without a token
        // has it, and so does a token that says nothing about a role.
        var claimed = tokenRole ?? AuthenticatedRole;
        if (TokenRoles.TryGetValue(claimed, out var mapped))
        {
            return mapped;
        }

        if (string.Equals(claimed, AuthenticatedRole, StringComparison.Ordinal) || string.Equals(claimed, UserRole, StringComparison.Ordinal))
        {
            return UserRole;
        }

        // The least any caller gets. The token's own claims go with it, as PostgREST would send them.
        if (string.Equals(claimed, AnonRole, StringComparison.Ordinal) || string.Equals(claimed, AnonymousRole, StringComparison.Ordinal))
        {
            return AnonymousRole;
        }

        if (UnknownTokenRole == UnknownTokenRole.Anonymous)
        {
            asAnonymous = true;
            return AnonymousRole;
        }

        throw ToolkitRefusals.Of(ToolkitRefusals.RoleNotAllowed, ("Role", claimed));
    }

    /// <summary>
    /// Refuses roles that no policy can be for, the same as <see cref="RowAccessRoleNames"/> does, so the
    /// roles <see cref="PostgresRowAccess.SetupScript"/> makes are the roles a script writes policies for.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A role is empty or cannot be a policy's, <see cref="SystemInRole"/> is another kind of caller's role as
    /// well, or <see cref="TokenRoles"/> maps a token role without a name, or one to a role a token must never
    /// run as.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="UnknownTokenRole"/> or <see cref="Scope"/> is not one of its values, or a timeout in
    /// <see cref="StatementTimeouts"/> is for a kind that does not exist, is shorter than a millisecond or is longer
    /// than Postgres can count.
    /// </exception>
    internal void Validate()
    {
        Configured(UserRole, nameof(UserRole));
        Configured(AnonymousRole, nameof(AnonymousRole));

        if (SystemRole is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(SystemRole, nameof(SystemRole));
        }

        if (SystemInRole is not null)
        {
            Configured(SystemInRole, nameof(SystemInRole));

            if (RowAccessRoleNames.SharedSystemIn(SystemInRole, UserRole, AnonymousRole) is { } shared)
            {
                throw new ArgumentException(shared, nameof(SystemInRole));
            }

            // SystemRole may bypass row level security, which the scoped system role never does.
            if (string.Equals(SystemInRole, SystemRole, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"'{SystemInRole}' is the SystemRole as well, which background work runs as past every policy. The scoped system role stays inside them, so it is a role of its own.",
                    nameof(SystemInRole));
            }
        }

        if (!Enum.IsDefined(UnknownTokenRole))
        {
            throw new ArgumentOutOfRangeException(nameof(UnknownTokenRole), UnknownTokenRole, "A token role on no list is refused, or runs as an anonymous caller.");
        }

        if (!Enum.IsDefined(Scope))
        {
            throw new ArgumentOutOfRangeException(nameof(Scope), Scope, "A caller's settings last as long as the connection, or as long as a transaction.");
        }

        foreach (var (kind, timeout) in StatementTimeouts)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(StatementTimeouts), kind, "A statement timeout is for a kind of caller that exists.");
            }

            // Postgres counts the timeout in whole milliseconds, in an integer, and reads zero as no timeout at all.
            if (timeout < TimeSpan.FromMilliseconds(1) || timeout.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(StatementTimeouts),
                    timeout,
                    $"The statement timeout of a {kind} caller is at least a millisecond and at most {int.MaxValue} milliseconds. Leave the kind out to give it the login role's own.");
            }
        }

        foreach (var (tokenRole, role) in TokenRoles)
        {
            // A rule for the scoped system role is written for 'ddd_system_in' where the host configures none, so
            // that name is the application's own either way.
            var problem = RowAccessRoleNames.NotAMappedRole(tokenRole, role, AnonymousRole, SystemInRole ?? DefaultSystemInRole);
            if (problem is null && SystemRole is not null && string.Equals(role, SystemRole, StringComparison.Ordinal))
            {
                problem = $"TokenRoles maps the token role '{tokenRole}' to '{role}', which is the SystemRole: background work runs as it past every policy, and a token would carry that power.";
            }

            if (problem is not null)
            {
                throw new ArgumentException(problem, nameof(TokenRoles));
            }
        }
    }

    /// <summary>
    /// These options as they are now, checked, in an object of their own: what an interceptor keeps, so a change
    /// made to the host's options after it was built reaches no connection half way, the role without its claims.
    /// </summary>
    /// <exception cref="ArgumentException">As <see cref="Validate"/>.</exception>
    internal PostgresRowLevelSecurityOptions Snapshot()
    {
        var snapshot = new PostgresRowLevelSecurityOptions
        {
            UserRole = UserRole,
            AnonymousRole = AnonymousRole,
            SystemInRole = SystemInRole,
            SystemRole = SystemRole,
            UnknownTokenRole = UnknownTokenRole,
            Scope = Scope,
        };

        foreach (var (tokenRole, role) in TokenRoles)
        {
            snapshot.TokenRoles[tokenRole] = role;
        }

        foreach (var (kind, timeout) in StatementTimeouts)
        {
            snapshot.StatementTimeouts[kind] = timeout;
        }

        snapshot.Validate();
        return snapshot;
    }

    private static void Configured(string role, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role, name);

        if (RowAccessRoleNames.NotAConfiguredRole(role) is { } problem)
        {
            throw new ArgumentException(problem, name);
        }
    }
}
