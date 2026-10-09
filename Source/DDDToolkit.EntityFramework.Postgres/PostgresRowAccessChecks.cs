using System.Data.Common;
using System.Text;
using System.Transactions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Checks an application on Postgres runs at start-up, before its first request, that the database is set up
/// as its row level security relies on: the role it logs in as holds nothing and reaches no role that does,
/// and may switch to every role its callers run as; the functions that run as their owner still answer where
/// row level security is forced; and a context runs its commands as the caller.
/// <para>
/// <c>AddPostgresRowLevelSecurity</c>, and so <c>AddSupabaseRowLevelSecurity</c>, registers each of them as a
/// start-up check over every registered context on Postgres, which a host runs with
/// <c>services.RunStartupChecks()</c> and turns off one by one by the names below. The methods stay for a host
/// that runs them by hand, the switch first:
/// </para>
/// <code>
/// await PostgresRowAccessChecks.EnsureLoginRoleMaySwitchToCallersAsync(context, cancellationToken);
/// await PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(context, cancellationToken);
/// await PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(context, cancellationToken);
/// PostgresRowAccessChecks.EnsureRowLevelSecurityWired(context);
/// </code>
/// </summary>
/// <remarks>
/// The ones that ask the database read its catalogs on the context's own connection, outside any ambient
/// transaction: as the system caller, begun around the check alone, or for the question whether the login role
/// may switch to the system caller's role among the others, as the login role itself. The catalogs answer every
/// role, and what they are asked is about the session's user, the role the application logged in as, whichever
/// role the connection runs as at that moment. Each names every finding with the statement that fixes it, in one
/// exception.
/// </remarks>
public static class PostgresRowAccessChecks
{
    /// <summary>
    /// The start-up check that every registered context on Postgres runs its commands as the caller, but one
    /// configured with <c>UseDDDToolkitCore</c> and left without row level security on purpose
    /// (<see cref="EnsureRowLevelSecurityWired"/>). It opens nothing, so it runs with the checks of the services,
    /// first: a context without the interceptor would fail the questions the other checks put through it.
    /// </summary>
    public const string RowLevelSecurityWiredCheck = "postgres.row-level-security-wired";

    /// <summary>
    /// The start-up check that the role the host logs in as may become every caller of every context that runs as
    /// its caller (<see cref="EnsureLoginRoleMaySwitchToCallersAsync"/>). It runs in the stage of the login, before
    /// every check that asks as the system caller and so switches to the system caller's role.
    /// </summary>
    public const string LoginRoleMaySwitchToCallersCheck = "postgres.login-role-may-switch-to-callers";

    /// <summary>
    /// The start-up check that the role the host logs in as owns and holds nothing in the schemas of every
    /// registered context on Postgres (<see cref="EnsureLoginRoleOwnsNothingAsync"/>). A host that logs in as the
    /// role that owns its tables by design, a sample on the database's own superuser say, turns this one off by
    /// name, with that reason.
    /// </summary>
    public const string LoginRoleOwnsNothingCheck = "postgres.login-role-owns-nothing";

    /// <summary>
    /// The start-up check that every function that runs as its owner, in the schemas of every registered context
    /// on Postgres, is owned by a role the forced policies let through (<see cref="EnsureDefinerOwnersBypassAsync"/>).
    /// </summary>
    public const string DefinerOwnersBypassCheck = "postgres.definer-owners-bypass";

    /// <summary>The name Npgsql's provider for Entity Framework gives itself: what a context on Postgres reports.</summary>
    internal const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// What <c>UseDDDToolkitCore</c> leaves in a context's options, and <c>UseDDDToolkit</c> does not: that the
    /// application asked for the toolkit's base alone. This package references <c>DDDToolkit.EntityFramework</c> no
    /// more than it references Npgsql, so the name of the type is what it goes by.
    /// </summary>
    private const string BaseAlone = "DDDToolkit.EntityFramework.BaseAloneExtension";

    /// <summary>The toolkit's own schema, which every check looks at next to the context's.</summary>
    private const string ToolkitSchema = "ddd";

    /// <summary>What a fix that hands an object to its rightful owner names, which only the host knows.</summary>
    private const string Owner = "<the role that runs the migrations>";

    /// <summary>
    /// What the role the application logged in as owns or holds in the schemas of the checked context's
    /// model and in <c>ddd</c>, one finding and its fix per row, each with the role's name. The role is the
    /// session's user, whatever role the connection runs as. The first, numbered 0, is said only of a role that
    /// owns a schema there: it made the schema, so it is the role the migrations run as, and its fix is another
    /// role to log in as. A membership counts when the role has the member's privileges without switching to
    /// it: since Postgres 16 that is said per membership, before it by the role's own attribute, and reading the
    /// membership as JSON asks for the newer answer without naming a column an older server lacks.
    /// <para>
    /// A role it may switch to counts when that role is past the policies itself: a superuser, a role that
    /// bypasses row level security, or the owner of a table, a function or a schema there. Whether it may switch
    /// is what <c>SET ROLE</c> asks, said per membership since Postgres 16 and by membership alone before it. The
    /// role the question itself runs as is the system caller's, and where the host chose one that bypasses row
    /// level security, Supabase's <c>service_role</c> for one, that is the host's own decision and no finding.
    /// A superuser may switch to any role, and is named as a superuser alone. The role that owns the database
    /// is <c>pg_database_owner</c> without a grant, which owns the schema <c>public</c> since Postgres 15: it is
    /// named like any other, and its fix is another owner for the database, since no grant is there to revoke.
    /// </para>
    /// </summary>
    private const string LoginRoleFindings =
        """
        WITH login AS (
            SELECT r.oid, r.rolname, pg_catalog.quote_ident(r.rolname) AS name, r.rolsuper, r.rolbypassrls, r.rolcreaterole, r.rolreplication, r.rolinherit
            FROM pg_catalog.pg_roles r WHERE r.rolname = SESSION_USER::pg_catalog.text),
        relations AS (
            SELECT c.oid, c.relowner, c.relacl, c.relkind, pg_catalog.quote_ident(n.nspname) || '.' || pg_catalog.quote_ident(c.relname) AS name,
                   CASE c.relkind WHEN 'S' THEN 'sequence' WHEN 'v' THEN 'view' WHEN 'm' THEN 'materialized view' WHEN 'f' THEN 'foreign table' ELSE 'table' END AS kind
            FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = ANY ($1) AND c.relkind IN ('r', 'p', 'v', 'm', 'S', 'f'))
        SELECT 0, 'it owns ' || CASE WHEN pg_catalog.count(*) = 1 THEN 'the schema ' ELSE 'the schemas ' END
                   || pg_catalog.string_agg(pg_catalog.quote_ident(n.nspname), ', ' ORDER BY n.nspname) || ' and what the migrations made there, as the role that runs them does',
               'log in as a role of its own, one that owns and holds nothing and may switch to the roles callers run as, and keep ' || login.name || ' for running the migrations.', login.rolname
        FROM login JOIN pg_catalog.pg_namespace n ON n.nspowner = login.oid WHERE n.nspname = ANY ($1)
        GROUP BY login.name, login.rolname
        UNION ALL
        SELECT 1, 'it is a superuser', 'ALTER ROLE ' || login.name || ' NOSUPERUSER;', login.rolname FROM login WHERE login.rolsuper
        UNION ALL
        SELECT 2, 'it may bypass row level security', 'ALTER ROLE ' || login.name || ' NOBYPASSRLS;', login.rolname FROM login WHERE login.rolbypassrls
        UNION ALL
        SELECT 3, 'it may create roles, and before Postgres 16 grant itself any role that is not a superuser', 'ALTER ROLE ' || login.name || ' NOCREATEROLE;', login.rolname FROM login WHERE login.rolcreaterole
        UNION ALL
        SELECT 4, 'it may replicate, and read every change of every table from a replication slot', 'ALTER ROLE ' || login.name || ' NOREPLICATION;', login.rolname FROM login WHERE login.rolreplication
        UNION ALL
        SELECT 5, 'it has the privileges of ' || pg_catalog.quote_ident(granted.rolname) || ' without switching to it',
               'ALTER ROLE ' || login.name || ' NOINHERIT;' || CASE WHEN (pg_catalog.to_jsonb(m) ->> 'inherit_option') IS NOT NULL
                   THEN ' GRANT ' || pg_catalog.quote_ident(granted.rolname) || ' TO ' || login.name || ' WITH INHERIT FALSE;' ELSE '' END, login.rolname
        FROM login JOIN pg_catalog.pg_auth_members m ON m.member = login.oid JOIN pg_catalog.pg_roles granted ON granted.oid = m.roleid
        WHERE coalesce((pg_catalog.to_jsonb(m) ->> 'inherit_option')::pg_catalog.bool, login.rolinherit)
        UNION ALL
        SELECT 6, 'it owns the schema ' || pg_catalog.quote_ident(n.nspname), 'ALTER SCHEMA ' || pg_catalog.quote_ident(n.nspname) || ' OWNER TO {owner};', login.rolname
        FROM login JOIN pg_catalog.pg_namespace n ON n.nspowner = login.oid WHERE n.nspname = ANY ($1)
        UNION ALL
        SELECT 7, 'it owns the ' || relations.kind || ' ' || relations.name,
               'ALTER ' || CASE relations.relkind WHEN 'S' THEN 'SEQUENCE' WHEN 'v' THEN 'VIEW' WHEN 'm' THEN 'MATERIALIZED VIEW' WHEN 'f' THEN 'FOREIGN TABLE' ELSE 'TABLE' END
                   || ' ' || relations.name || ' OWNER TO {owner};', login.rolname
        FROM login JOIN relations ON relations.relowner = login.oid
        UNION ALL
        SELECT 8, 'it owns the function ' || signature.name, 'ALTER ROUTINE ' || signature.name || ' OWNER TO {owner};', login.rolname
        FROM login JOIN pg_catalog.pg_proc p ON p.proowner = login.oid JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
        CROSS JOIN LATERAL (SELECT pg_catalog.quote_ident(n.nspname) || '.' || pg_catalog.quote_ident(p.proname) || '(' || pg_catalog.pg_get_function_identity_arguments(p.oid) || ')' AS name) signature
        WHERE n.nspname = ANY ($1)
        UNION ALL
        SELECT 9, CASE WHEN held.grantee = 0 THEN 'it has ' ELSE 'it holds ' END || held.privileges || ' on the ' || relations.kind || ' ' || relations.name
                   || CASE WHEN held.grantee = 0 THEN ' through PUBLIC' ELSE '' END,
               'REVOKE ALL ON ' || CASE relations.relkind WHEN 'S' THEN 'SEQUENCE' ELSE 'TABLE' END || ' ' || relations.name
                   || ' FROM ' || CASE WHEN held.grantee = 0 THEN 'PUBLIC' ELSE login.name END || ';', login.rolname
        FROM login CROSS JOIN relations
        CROSS JOIN LATERAL (
            SELECT acl.grantee, pg_catalog.string_agg(DISTINCT acl.privilege_type, ', ' ORDER BY acl.privilege_type) AS privileges
            FROM (SELECT whole.grantee, whole.privilege_type FROM pg_catalog.aclexplode(relations.relacl) whole
                  UNION ALL
                  SELECT part.grantee, part.privilege_type || ' of a column'
                  FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) part
                  WHERE a.attrelid = relations.oid AND NOT a.attisdropped) acl
            WHERE acl.grantee IN (login.oid, 0)
            GROUP BY acl.grantee) held
        WHERE relations.relowner <> login.oid
        UNION ALL
        SELECT 10, 'it may switch to ' || pg_catalog.quote_ident(reached.rolname) || ', which ' || past.what,
               CASE WHEN reached.rolname = 'pg_database_owner'
                   THEN 'ALTER DATABASE ' || pg_catalog.quote_ident(pg_catalog.current_database()) || ' OWNER TO {owner};'
                   WHEN EXISTS (SELECT FROM pg_catalog.pg_auth_members m WHERE m.member = login.oid AND m.roleid = reached.oid)
                   THEN 'REVOKE ' || pg_catalog.quote_ident(reached.rolname) || ' FROM ' || login.name || ';'
                   ELSE 'REVOKE <the role that is a member of ' || pg_catalog.quote_ident(reached.rolname) || '> FROM ' || login.name || ';' END, login.rolname
        FROM login JOIN pg_catalog.pg_roles reached ON reached.oid <> login.oid
        CROSS JOIN LATERAL (
            SELECT pg_catalog.concat_ws(' and ',
                CASE WHEN reached.rolsuper THEN 'is a superuser' END,
                CASE WHEN reached.rolbypassrls AND reached.rolname <> CURRENT_USER::pg_catalog.text THEN 'may bypass row level security' END,
                CASE WHEN EXISTS (SELECT FROM relations WHERE relations.relowner = reached.oid)
                       OR EXISTS (SELECT FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace WHERE p.proowner = reached.oid AND n.nspname = ANY ($1))
                       OR EXISTS (SELECT FROM pg_catalog.pg_namespace n WHERE n.nspowner = reached.oid AND n.nspname = ANY ($1))
                     THEN 'owns tables, functions or schemas the application uses' END) AS what) past
        WHERE NOT login.rolsuper AND past.what <> ''
          AND pg_catalog.pg_has_role(login.oid, reached.oid,
                  CASE WHEN pg_catalog.current_setting('server_version_num')::pg_catalog.int4 >= 160000 THEN 'SET' ELSE 'MEMBER' END)
        UNION ALL
        SELECT 11, CASE WHEN held.grantee = 0 THEN 'it has ' ELSE 'it holds ' END || 'CREATE on the schema ' || pg_catalog.quote_ident(n.nspname)
                   || CASE WHEN held.grantee = 0 THEN ' through PUBLIC' ELSE '' END,
               'REVOKE CREATE ON SCHEMA ' || pg_catalog.quote_ident(n.nspname) || ' FROM ' || CASE WHEN held.grantee = 0 THEN 'PUBLIC' ELSE login.name END || ';', login.rolname
        FROM login JOIN pg_catalog.pg_namespace n ON n.nspname = ANY ($1) AND n.nspowner <> login.oid
        CROSS JOIN LATERAL (
            SELECT DISTINCT acl.grantee FROM pg_catalog.aclexplode(n.nspacl) acl
            WHERE acl.privilege_type = 'CREATE' AND acl.grantee IN (login.oid, 0)) held
        ORDER BY 1, 2
        """;

    /// <summary>
    /// The roles callers run as, <c>$1</c>, that the role the application logged in as may not switch to: each
    /// with its name as a statement writes it, whether it exists at all, and the login role's name, plain and as a
    /// statement writes it. Whether it may switch is what <c>SET ROLE</c> asks, said per membership since Postgres
    /// 16 and by membership alone before it; a superuser may switch to any role there is. Where the settings last
    /// one transaction, <c>$2</c>, the procedure that sets them is asked about too, in the row whose role is null:
    /// the login role calls it before every statement, and needs the right to use the <c>ddd</c> schema for that.
    /// The procedure is found in the catalogs rather than by its name, which Postgres would only look up for a role
    /// that may use the schema already.
    /// </summary>
    private const string SwitchFindings =
        $"""
        WITH login AS (
            SELECT r.oid, r.rolname, pg_catalog.quote_ident(r.rolname) AS name, r.rolsuper
            FROM pg_catalog.pg_roles r WHERE r.rolname = SESSION_USER::pg_catalog.text),
        procedure AS (
            SELECT (SELECT p.oid FROM pg_catalog.pg_proc p
                    WHERE p.pronamespace = n.oid AND p.proname = 'use_caller' AND p.prokind = 'p'
                      AND pg_catalog.oidvectortypes(p.proargtypes) = 'text, text, text[], text[]') AS oid,
                   n.oid AS schema
            FROM (SELECT (SELECT oid FROM pg_catalog.pg_namespace WHERE nspname = 'ddd') AS oid) n)
        SELECT wanted.role, pg_catalog.quote_ident(wanted.role), reached.oid IS NULL, login.rolname, login.name
        FROM login CROSS JOIN pg_catalog.unnest($1::pg_catalog.text[]) AS wanted(role)
        LEFT JOIN pg_catalog.pg_roles reached ON reached.rolname = wanted.role
        WHERE reached.oid IS NULL
           OR NOT (login.rolsuper OR pg_catalog.pg_has_role(login.oid, reached.oid,
                   CASE WHEN pg_catalog.current_setting('server_version_num')::pg_catalog.int4 >= 160000 THEN 'SET' ELSE 'MEMBER' END))
        UNION ALL
        SELECT NULL, '{PostgresRowAccess.UseCallerSignature}', procedure.oid IS NULL, login.rolname, login.name
        FROM login CROSS JOIN procedure
        WHERE $2 AND CASE WHEN procedure.oid IS NULL THEN true
                          ELSE NOT (pg_catalog.has_function_privilege(login.oid, procedure.oid, 'EXECUTE')
                                    AND pg_catalog.has_schema_privilege(login.oid, procedure.schema, 'USAGE')) END
        """;

    /// <summary>
    /// The functions in the schemas of a context's model and in <c>ddd</c> that run as their owner, and whose
    /// owner neither bypasses row level security nor is a superuser: each with its owner. None while no table
    /// in those schemas forces row level security: a table's owner is then exempt from its policies, and such a
    /// function answers as it should.
    /// </summary>
    private const string DefinerFindings =
        """
        SELECT pg_catalog.quote_ident(n.nspname) || '.' || pg_catalog.quote_ident(p.proname) || '(' || pg_catalog.pg_get_function_identity_arguments(p.oid) || ')',
               pg_catalog.quote_ident(owner.rolname)
        FROM pg_catalog.pg_proc p
        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
        JOIN pg_catalog.pg_roles owner ON owner.oid = p.proowner
        WHERE p.prosecdef AND n.nspname = ANY ($1) AND NOT (owner.rolbypassrls OR owner.rolsuper)
          AND EXISTS (SELECT FROM pg_catalog.pg_class forced JOIN pg_catalog.pg_namespace within ON within.oid = forced.relnamespace
                      WHERE within.nspname = ANY ($1) AND forced.relkind IN ('r', 'p') AND forced.relforcerowsecurity)
        ORDER BY 1
        """;

    /// <summary>
    /// Throws unless the role the application logs in as holds nothing: it owns no table, view, sequence,
    /// function or schema among the schemas of <paramref name="context"/>'s model and <c>ddd</c>, holds no
    /// privilege on their tables and may create nothing in those schemas, of its own or through
    /// <c>PUBLIC</c>, has the privileges of no role it is a member of without switching to it, and is neither a
    /// superuser nor a role that bypasses row level security. Nor may it switch to a role that is one of those
    /// or owns something there, since switching is all it takes to be that role.
    /// <para>
    /// Nor may it create roles or replicate, each a way past the policies that a statement on the connection
    /// takes as the login role itself: a role that may create roles grants itself, before Postgres 16, any role
    /// that is not a superuser, Supabase's <c>service_role</c> for one, and a role that may replicate makes a
    /// replication slot and reads from it every change to every table, where the server keeps what logical
    /// replication reads, as Supabase's does.
    /// </para>
    /// <para>
    /// Any statement that reaches the application's connection can go back to the login role: Postgres checks a
    /// change of role against the role that logged in, not the one that is current. So what the login role
    /// owns or holds, and what a role it may switch to owns, is outside every policy, for whoever manages to
    /// send a statement. A login role that holds nothing leaves such a statement the roles it may switch to,
    /// each held to its policies and its privileges, and nothing besides.
    /// </para>
    /// <para>
    /// One role it may switch to is not asked whether it bypasses row level security: the one the system caller
    /// runs as, <see cref="PostgresRowLevelSecurityOptions.SystemRole"/>, which is the role this check runs as.
    /// A host that names a bypassing role there, Supabase's <c>service_role</c> for one, chose that its
    /// background work reads past the policies. That role is still named when it is a superuser or owns
    /// something.
    /// </para>
    /// </summary>
    /// <param name="context">A context on the application's connection; its model names the schemas to look at.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The login role owns or holds something, or may switch to a role that is past the policies; the message names
    /// the role, and each finding with the statement that fixes it. A login role that owns a schema there is the
    /// role the migrations run as, and the message says that one thing, and to log in as a role of its own.
    /// </exception>
    public static async Task EnsureLoginRoleOwnsNothingAsync(DbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var findings = await AskAsync(context, LoginRoleFindings.Replace("{owner}", Owner, StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
        if (findings.Count == 0)
        {
            return;
        }

        // A login role that owns a schema of the application's made it, with what is in it: it is the role the
        // migrations run as. Every other finding then has one fix, another role to log in as, so that is the one
        // said, rather than a line for each thing the migrations made.
        var shown = findings.Where(static finding => finding[0] == "0").ToList() is { Count: > 0 } owner ? owner : findings;
        var message = new StringBuilder()
            .Append("The role '").Append(findings[0][3]).Append("' that '").Append(context.GetType().Name).Append("' logs in as is meant to hold nothing, and it does:");
        foreach (var finding in shown)
        {
            message.Append("\n- ").Append(finding[1]).Append(". Fix: ").Append(finding[2]);
        }

        message.Append("\nA statement that reaches the connection can always go back to the role that logged in, and from there to every role it may switch to, so what those roles own or hold is outside every policy.");
        throw new InvalidOperationException(message.ToString());
    }

    /// <summary>
    /// Throws unless the role the application logs in as may switch to every role <paramref name="context"/>'s
    /// callers run as: the user's, the anonymous caller's, the scoped system role, the role the system caller runs
    /// as, and the role of every mapped token role, as its row level security interceptor was configured. Where the
    /// settings last one transaction (<see cref="RowLevelSecurityScope.Transaction"/>), it also throws unless the
    /// login role may call <c>ddd.use_caller</c>, which sets them.
    /// <para>
    /// A role is switched to when a caller of that kind connects, not when the application starts. Without this,
    /// an application whose login role was never granted one of them starts, passes its other checks, and fails the
    /// first request of such a caller, a token role nobody holds in testing, or the background work, which runs as
    /// the system caller's role. The login role holding nothing (<see cref="EnsureLoginRoleOwnsNothingAsync"/>)
    /// is half of the bargain; this is the other half.
    /// </para>
    /// <para>
    /// The scoped system role is asked about where it exists. Supabase's exported access files make it only where a
    /// policy is for it or the grants are written, as they are unless the project sets <c>SupabaseRowAccessGrants</c>
    /// to <c>None</c>; so only an application that writes its privileges itself and whose rules name it nowhere has
    /// no such role, and it could do no scoped system work with one: no policy can be for a role that does not
    /// exist. Such a role is no finding. Where it exists, a login role of the application's own that may not switch
    /// to it is one: the file <c>SupabaseLoginRole</c> writes grants it, or else a migration of the host's own.
    /// The scoped system role that is also another caller's role is asked about as that one.
    /// </para>
    /// <para>
    /// Unlike the other checks, this one asks as the login role itself, on the context's connection opened past the
    /// interceptor: the system caller's role is one of those it asks about, and switching to a role the login role
    /// may not switch to would fail the question before it was asked. The catalogs answer every role, and nothing
    /// is set on the connection. So call it before the checks that run as the system caller, which would otherwise
    /// fail first, on the switch, without saying why.
    /// </para>
    /// </summary>
    /// <param name="context">A context on the application's connection, with its row level security interceptor.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The context has no row level security interceptor, or the login role may not switch to one of the roles or
    /// call the procedure; the message names the login role, and each role or the procedure with the statement that
    /// fixes it.
    /// </exception>
    public static async Task EnsureLoginRoleMaySwitchToCallersAsync(DbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = InterceptorOf(context)?.Options ?? throw NotWired(context);
        var callers = CallerRolesOf(options);

        var findings = await AskAsLoginRoleAsync(
            context,
            SwitchFindings,
            [callers.Select(caller => caller.Role).ToArray(), options.Scope == RowLevelSecurityScope.Transaction],
            cancellationToken).ConfigureAwait(false);

        // A scoped system role that does not exist is one no policy is for: the application does no scoped system
        // work, and its migrations made no role for it.
        var scopedOnly = ScopedSystemRoleAlone(options);
        findings = [.. findings.Where(finding => !(finding[0] == scopedOnly && finding[2] == bool.TrueString))];
        if (findings.Count == 0)
        {
            return;
        }

        // In the order the roles were configured, and the procedure last: the database sorts names by its own collation.
        var order = callers.Select(caller => caller.Role).ToList();
        findings = [.. findings.OrderBy(finding => finding[0].Length == 0 ? order.Count : order.IndexOf(finding[0]))];

        var login = findings[0][4];
        var message = new StringBuilder()
            .Append("The role '").Append(findings[0][3]).Append("' that '").Append(context.GetType().Name)
            .Append("' logs in as is meant to switch to every role its callers run as, and it cannot:");
        foreach (var finding in findings)
        {
            var (name, missing) = (finding[1], finding[2] == bool.TrueString);
            var (_, what, pair) = finding[0].Length == 0 ? default : callers.First(caller => caller.Role == finding[0]);
            message.Append("\n- ").Append(finding[0].Length == 0
                ? missing
                    ? $"{name}, which sets the caller of each transaction, does not exist. Fix: apply the access files, or PostgresRowAccess.SetupScript(), which make it."
                    : $"it may not call {name}, which sets the caller of each transaction. Fix: GRANT USAGE ON SCHEMA ddd TO {login}; GRANT EXECUTE ON PROCEDURE {name} TO {login};"
                : missing
                    ? $"{name}, {what}, does not exist. Fix: run the migrations that make it, or CREATE ROLE {name} NOLOGIN NOINHERIT; GRANT {name} TO {login}; {MappedOnSupabase(pair)}"
                    : $"it may not switch to {name}, {what}. Fix: GRANT {name} TO {login}; {MappedOnSupabase(pair)}");
        }

        message.Append("\nA caller's role is switched to when that caller connects, not when the application starts: without it, the application starts and that caller's first request fails.");
        throw new InvalidOperationException(message.ToString());
    }

    /// <summary>
    /// Throws unless every function that runs as its owner, in the schemas of <paramref name="context"/>'s model
    /// and in <c>ddd</c>, is owned by a role that bypasses row level security or is a superuser. It passes
    /// where no table in those schemas forces row level security, whoever owns the functions, so a host may
    /// call it whether it forces or not.
    /// <para>
    /// The access functions run as their owner so that they answer without the policies of the tables they
    /// read. Without <c>FORCE ROW LEVEL SECURITY</c> a table's owner is exempt from its policies, which is all
    /// they need. Where row level security is forced (<see cref="RowAccessExport.ForceRowLevelSecurity"/>),
    /// only a role that may bypass it is: a function owned by any other role would read the tables under that
    /// role's policies, find no row, and answer that nobody may do anything, without a word.
    /// </para>
    /// </summary>
    /// <param name="context">A context on the application's connection; its model names the schemas to look at.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A function's owner is held to row level security; the message names each function, its owner and the fix.</exception>
    public static async Task EnsureDefinerOwnersBypassAsync(DbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var findings = await AskAsync(context, DefinerFindings, cancellationToken).ConfigureAwait(false);
        if (findings.Count == 0)
        {
            return;
        }

        var message = new StringBuilder()
            .Append("Functions in the schemas of '").Append(context.GetType().Name)
            .Append("' run as their owner, and their owner is held to row level security, so on a table that forces it they would read no row:");
        foreach (var finding in findings)
        {
            message.Append("\n- ").Append(finding[0]).Append(" is owned by ").Append(finding[1])
                .Append(". Fix: ALTER FUNCTION ").Append(finding[0]).Append(" OWNER TO <a role with BYPASSRLS>; or ALTER ROLE ").Append(finding[1]).Append(" BYPASSRLS;");
        }

        throw new InvalidOperationException(message.ToString());
    }

    /// <summary>
    /// Throws unless <paramref name="context"/> runs its commands through
    /// <see cref="PostgresRowLevelSecurityInterceptor"/>, or was configured with <c>UseDDDToolkitCore</c> and left
    /// without it on purpose. Without it every command runs as the role the application logged in as: past every
    /// policy where that role owns the tables, and refused outright where it holds nothing.
    /// <para>
    /// A context wired with <c>UseDDDToolkit</c> has it, since that call adds it to every context on Postgres once row
    /// level security is registered. A context given the base alone, <c>UseDDDToolkitCore</c>, keeps that in its
    /// options: that call is how an application says a context runs as the login role, and it is taken as meant.
    /// Every other context without the interceptor is refused, one that added the toolkit's interceptors by hand
    /// included: nothing there says the login role was meant.
    /// </para>
    /// </summary>
    /// <param name="context">The context to check; it is not opened.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The context's options have no row level security interceptor, and were not given the base alone.</exception>
    public static void EnsureRowLevelSecurityWired(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (InterceptorOf(context) is null && !GivenTheBaseAlone(context))
        {
            throw NotWired(context);
        }
    }

    /// <summary>
    /// Registers the four checks above as start-up checks: once, however many times row level security is
    /// registered. Each takes every context the services register, on Postgres, in a scope of its own; the switch
    /// takes only those that run as their caller, since another switches to nobody.
    /// </summary>
    internal static void AddStartupChecks(IServiceCollection services)
    {
        services.AddStartupCheck(new StartupCheck(RowLevelSecurityWiredCheck, StartupCheckStage.Services, (provider, cancellationToken) =>
            EachContextAsync(provider, runsAsCaller: false, (context, _) =>
            {
                EnsureRowLevelSecurityWired(context);
                return Task.CompletedTask;
            }, cancellationToken)));

        services.AddStartupCheck(new StartupCheck(LoginRoleMaySwitchToCallersCheck, StartupCheckStage.Login, (provider, cancellationToken) =>
            EachContextAsync(provider, runsAsCaller: true, EnsureLoginRoleMaySwitchToCallersAsync, cancellationToken)));

        services.AddStartupCheck(new StartupCheck(LoginRoleOwnsNothingCheck, StartupCheckStage.Database, (provider, cancellationToken) =>
            EachContextAsync(provider, runsAsCaller: false, EnsureLoginRoleOwnsNothingAsync, cancellationToken)));

        services.AddStartupCheck(new StartupCheck(DefinerOwnersBypassCheck, StartupCheckStage.Database, (provider, cancellationToken) =>
            EachContextAsync(provider, runsAsCaller: false, EnsureDefinerOwnersBypassAsync, cancellationToken)));
    }

    /// <summary>
    /// Runs <paramref name="check"/> on every context <paramref name="services"/> register that is on Postgres, and
    /// with <paramref name="runsAsCaller"/> only on those with the row level security interceptor: by the name of
    /// its type, in a scope of its own. Every context the container knows, not a list of them, so a module added
    /// later is checked without anyone remembering to add it. Entity Framework registers the options of each
    /// context once more as the non-generic <see cref="DbContextOptions"/>, and those name the context they are
    /// for.
    /// </summary>
    private static async Task EachContextAsync(IServiceProvider services, bool runsAsCaller, Func<DbContext, CancellationToken, Task> check, CancellationToken cancellationToken)
    {
        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var contextTypes = scope.ServiceProvider.GetServices<DbContextOptions>()
                .Select(options => options.ContextType)
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList();

            foreach (var contextType in contextTypes)
            {
                var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
                if (context.Database.ProviderName == NpgsqlProvider && (!runsAsCaller || InterceptorOf(context) is not null))
                {
                    await check(context, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// The other fix for a role the login role may not switch to, where the Supabase build writes the login role's
    /// migration: that migration grants the roles <c>SupabaseRowAccessRoles</c> maps, so such a role is one a pair
    /// there leaves out, or one the database has not had the file for yet. A grant by hand would pass this check,
    /// and leave the role without what the access files write for a role they map, its privileges among them.
    /// </summary>
    /// <param name="pair">The pair that maps the role, as in <c>system=ddd_system</c>.</param>
    private static string MappedOnSupabase(string pair)
        => $"Or, where the Supabase build writes the login role's migration (SupabaseLoginRole), map it in SupabaseRowAccessRoles as {pair} and apply the file the next build writes.";

    /// <summary>The row level security interceptor among <paramref name="context"/>'s, or null where it has none.</summary>
    private static PostgresRowLevelSecurityInterceptor? InterceptorOf(DbContext context)
        => InterceptorsOf(context).OfType<PostgresRowLevelSecurityInterceptor>().FirstOrDefault();

    /// <summary>The interceptors of <paramref name="context"/>'s options, in the order they were added.</summary>
    private static IEnumerable<IInterceptor> InterceptorsOf(DbContext context)
        => context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];

    /// <summary>Whether <paramref name="context"/>'s options were given the toolkit's base alone, with <c>UseDDDToolkitCore</c>.</summary>
    private static bool GivenTheBaseAlone(DbContext context)
        => context.GetService<IDbContextOptions>().Extensions.Any(extension => string.Equals(extension.GetType().FullName, BaseAlone, StringComparison.Ordinal));

    /// <summary>What a context without the row level security interceptor is told, with the ways to configure it.</summary>
    private static InvalidOperationException NotWired(DbContext context)
        => new(
            $"'{context.GetType().Name}' does not run its commands as the caller: its options have no {nameof(PostgresRowLevelSecurityInterceptor)}, so every command would run as the role the application logged in as. " +
            "Configure it with options.UseDDDToolkit(serviceProvider), which adds it to every context on Postgres once row level security is registered; " +
            $"a context wired otherwise takes it with options.{nameof(DependencyInjection.UsePostgresRowLevelSecurity)}(serviceProvider), or UseSupabaseRowLevelSecurity on Supabase. " +
            "A context that should run as the role the application logged in as is configured with options.UseDDDToolkitCore(serviceProvider), which says so.");

    /// <summary>
    /// Every role <paramref name="options"/> switches a caller to, each once, with what it is to a message and the
    /// pair of <c>SupabaseRowAccessRoles</c> that maps it: the user's, the anonymous caller's, the scoped system role
    /// and the system caller's where there are those, and the roles of the mapped token roles. A role that is two of
    /// these is said as the first.
    /// </summary>
    private static List<(string Role, string What, string Pair)> CallerRolesOf(PostgresRowLevelSecurityOptions options)
    {
        List<(string Role, string What, string Pair)> roles =
        [
            (options.UserRole, "the role of a signed-in user", $"user={options.UserRole}"),
            (options.AnonymousRole, "the role of a caller without a token", $"anonymous={options.AnonymousRole}"),
        ];

        if (options.SystemInRole is { } systemIn)
        {
            roles.Add((systemIn, "the scoped system role", $"system-in={systemIn}"));
        }

        if (options.SystemRole is { } system)
        {
            roles.Add((system, "the role the system caller runs as", $"system={system}"));
        }

        foreach (var mapped in options.TokenRoles.GroupBy(pair => pair.Value, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var tokenRoles = mapped.Select(pair => pair.Key).Order(StringComparer.Ordinal).ToList();
            roles.Add((
                mapped.Key,
                $"the role of the token role {string.Join(" and ", tokenRoles.Select(tokenRole => $"'{tokenRole}'"))}",
                $"token:{tokenRoles[0]}={mapped.Key}"));
        }

        return [.. roles.DistinctBy(role => role.Role, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The scoped system role of <paramref name="options"/> where no other caller runs as it, or
    /// <see langword="null"/>: only that role may be missing without a caller failing on it.
    /// </summary>
    private static string? ScopedSystemRoleAlone(PostgresRowLevelSecurityOptions options)
        => options.SystemInRole is { } role
           && !string.Equals(role, options.UserRole, StringComparison.Ordinal)
           && !string.Equals(role, options.AnonymousRole, StringComparison.Ordinal)
           && !string.Equals(role, options.SystemRole, StringComparison.Ordinal)
           && !options.TokenRoles.Values.Contains(role, StringComparer.Ordinal)
            ? role
            : null;

    /// <summary>The schemas a check looks at: those of the tables and views of the context's model, its default schema, and the toolkit's own.</summary>
    private static string[] SchemasOf(DbContext context)
    {
        var model = context.Model;
        var schemas = new SortedSet<string>(StringComparer.Ordinal) { ToolkitSchema, model.GetDefaultSchema() ?? PostgresRowAccess.DefaultSchema };
        foreach (var entity in model.GetEntityTypes())
        {
            if (entity.GetTableName() is not null)
            {
                schemas.Add(entity.GetSchema() ?? PostgresRowAccess.DefaultSchema);
            }

            if (entity.GetViewName() is not null)
            {
                schemas.Add(entity.GetViewSchema() ?? PostgresRowAccess.DefaultSchema);
            }
        }

        return [.. schemas];
    }

    /// <summary>
    /// The rows <paramref name="sql"/> answers about the schemas of <paramref name="context"/>, its one parameter,
    /// each as text: asked on the context's own connection, as the system caller begun around the question
    /// alone, outside any ambient transaction.
    /// </summary>
    private static async Task<List<string[]>> AskAsync(DbContext context, string sql, CancellationToken cancellationToken)
    {
        using var suppressed = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        using var system = Callers.Begin(Caller.System);

        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAsync(context.Database.GetDbConnection(), context.Database.CurrentTransaction?.GetDbTransaction(), sql, [SchemasOf(context)], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The rows <paramref name="sql"/> answers with <paramref name="parameters"/>, each as text, asked as the role
    /// the application logged in as: on the context's connection, opened past Entity Framework and so past the
    /// interceptor where it is closed, outside any ambient transaction, and closed again. Nothing is set on it, so
    /// nothing is left on it either. A connection the context holds open already runs as whatever caller it was
    /// set for, which the session's user, the one a question about the login role asks for, is the same under.
    /// </summary>
    private static async Task<List<string[]>> AskAsLoginRoleAsync(DbContext context, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        using var suppressed = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);

        var connection = context.Database.GetDbConnection();
        var opened = connection.State == System.Data.ConnectionState.Closed;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await ReadAsync(connection, opened ? null : context.Database.CurrentTransaction?.GetDbTransaction(), sql, parameters, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>The rows <paramref name="sql"/> answers on <paramref name="connection"/>, which is open, with <paramref name="parameters"/>, each as text.</summary>
    private static async Task<List<string[]>> ReadAsync(DbConnection connection, DbTransaction? transaction, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            command.Transaction = transaction;

            // Positional, as the interceptor's own statement binds: the provider takes an array of text as it is.
            foreach (var value in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            var rows = new List<string[]>();
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var row = new string[reader.FieldCount];
                    for (var column = 0; column < row.Length; column++)
                    {
                        row[column] = Convert.ToString(reader.GetValue(column), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                    }

                    rows.Add(row);
                }
            }

            return rows;
        }
    }
}
