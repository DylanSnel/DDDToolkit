using System.Data.Common;
using System.Text;
using System.Transactions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Checks an application on Postgres runs at start-up, before its first request, that the database is set up
/// as its row level security relies on: the role it logs in as holds nothing and reaches no role that does,
/// the functions that run as their owner still answer where row level security is forced, and a context runs
/// its commands as the caller.
/// <code>
/// await PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(context, cancellationToken);
/// await PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(context, cancellationToken);
/// PostgresRowAccessChecks.EnsureRowLevelSecurityWired(context);
/// </code>
/// </summary>
/// <remarks>
/// The two that ask the database read its catalogs on the context's own connection, as the system caller, begun
/// around the check alone and outside any ambient transaction. The catalogs answer every role, and what they
/// are asked is about the session's user, the role the application logged in as, whichever role the connection
/// runs as at that moment. Each names every finding with the statement that fixes it, in one exception.
/// </remarks>
public static class PostgresRowAccessChecks
{
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
            SELECT r.oid, r.rolname, pg_catalog.quote_ident(r.rolname) AS name, r.rolsuper, r.rolbypassrls, r.rolinherit
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
        SELECT 3, 'it has the privileges of ' || pg_catalog.quote_ident(granted.rolname) || ' without switching to it',
               'ALTER ROLE ' || login.name || ' NOINHERIT;' || CASE WHEN (pg_catalog.to_jsonb(m) ->> 'inherit_option') IS NOT NULL
                   THEN ' GRANT ' || pg_catalog.quote_ident(granted.rolname) || ' TO ' || login.name || ' WITH INHERIT FALSE;' ELSE '' END, login.rolname
        FROM login JOIN pg_catalog.pg_auth_members m ON m.member = login.oid JOIN pg_catalog.pg_roles granted ON granted.oid = m.roleid
        WHERE coalesce((pg_catalog.to_jsonb(m) ->> 'inherit_option')::pg_catalog.bool, login.rolinherit)
        UNION ALL
        SELECT 4, 'it owns the schema ' || pg_catalog.quote_ident(n.nspname), 'ALTER SCHEMA ' || pg_catalog.quote_ident(n.nspname) || ' OWNER TO {owner};', login.rolname
        FROM login JOIN pg_catalog.pg_namespace n ON n.nspowner = login.oid WHERE n.nspname = ANY ($1)
        UNION ALL
        SELECT 5, 'it owns the ' || relations.kind || ' ' || relations.name,
               'ALTER ' || CASE relations.relkind WHEN 'S' THEN 'SEQUENCE' WHEN 'v' THEN 'VIEW' WHEN 'm' THEN 'MATERIALIZED VIEW' WHEN 'f' THEN 'FOREIGN TABLE' ELSE 'TABLE' END
                   || ' ' || relations.name || ' OWNER TO {owner};', login.rolname
        FROM login JOIN relations ON relations.relowner = login.oid
        UNION ALL
        SELECT 6, 'it owns the function ' || signature.name, 'ALTER ROUTINE ' || signature.name || ' OWNER TO {owner};', login.rolname
        FROM login JOIN pg_catalog.pg_proc p ON p.proowner = login.oid JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
        CROSS JOIN LATERAL (SELECT pg_catalog.quote_ident(n.nspname) || '.' || pg_catalog.quote_ident(p.proname) || '(' || pg_catalog.pg_get_function_identity_arguments(p.oid) || ')' AS name) signature
        WHERE n.nspname = ANY ($1)
        UNION ALL
        SELECT 7, CASE WHEN held.grantee = 0 THEN 'it has ' ELSE 'it holds ' END || held.privileges || ' on the ' || relations.kind || ' ' || relations.name
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
        SELECT 8, 'it may switch to ' || pg_catalog.quote_ident(reached.rolname) || ', which ' || past.what,
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
        SELECT 9, CASE WHEN held.grantee = 0 THEN 'it has ' ELSE 'it holds ' END || 'CREATE on the schema ' || pg_catalog.quote_ident(n.nspname)
                   || CASE WHEN held.grantee = 0 THEN ' through PUBLIC' ELSE '' END,
               'REVOKE CREATE ON SCHEMA ' || pg_catalog.quote_ident(n.nspname) || ' FROM ' || CASE WHEN held.grantee = 0 THEN 'PUBLIC' ELSE login.name END || ';', login.rolname
        FROM login JOIN pg_catalog.pg_namespace n ON n.nspname = ANY ($1) AND n.nspowner <> login.oid
        CROSS JOIN LATERAL (
            SELECT DISTINCT acl.grantee FROM pg_catalog.aclexplode(n.nspacl) acl
            WHERE acl.privilege_type = 'CREATE' AND acl.grantee IN (login.oid, 0)) held
        ORDER BY 1, 2
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
    /// <see cref="PostgresRowLevelSecurityInterceptor"/>. Without it every command runs as the role the
    /// application logged in as: past every policy where that role owns the tables, and refused outright where
    /// it holds nothing.
    /// </summary>
    /// <param name="context">The context to check; it is not opened.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The context's options have no row level security interceptor.</exception>
    public static void EnsureRowLevelSecurityWired(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var interceptors = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        if (!interceptors.Any(interceptor => interceptor is PostgresRowLevelSecurityInterceptor))
        {
            throw new InvalidOperationException(
                $"'{context.GetType().Name}' does not run its commands as the caller: its options have no {nameof(PostgresRowLevelSecurityInterceptor)}, so every command would run as the role the application logged in as. " +
                $"Configure it with options.{nameof(DependencyInjection.UsePostgresRowLevelSecurity)}(serviceProvider), or UseSupabaseRowLevelSecurity on Supabase, after services.{nameof(DependencyInjection.AddPostgresRowLevelSecurity)}().");
        }
    }

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
            var command = context.Database.GetDbConnection().CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = sql;
                command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();

                // Positional, as the interceptor's own statement binds: the provider takes an array of text as it is.
                var schemas = command.CreateParameter();
                schemas.Value = SchemasOf(context);
                command.Parameters.Add(schemas);

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
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
