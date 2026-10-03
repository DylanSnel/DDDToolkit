using System.Data.Common;
using System.Globalization;
using System.Transactions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DDDToolkit.Supporting.Tenancy.Postgres;

/// <summary>
/// Checks a host runs at start-up, before it serves anything, that the database and the host are set up the way
/// Tenancy's policies rely on. Each throws <see cref="InvalidOperationException"/> naming what is wrong and how to
/// put it right, so a host that would run with a hole in its second lock does not start.
/// <code>
/// var app = builder.Build();
/// TenancyPostgresChecks.EnsureExplicitCallers(app.Services);
/// TenancyPostgresChecks.EnsureSeatedTokenRolesAreSignedInUsers(app.Services);
/// await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(app.Services, cancellationToken);
/// await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(app.Services, cancellationToken);
/// await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(app.Services, cancellationToken);
/// </code>
/// The ones that ask the database do so on a context of Tenancy's own, made in a scope of their own, as the
/// application itself (<see cref="Caller.System"/>, begun around each query and outside any transaction the caller
/// holds), and read the catalog of Postgres alone: no table of Tenancy's, and no tenant's rows. The one that
/// proves the reads across tenants also asks one of their functions, as Tenancy's own scoped system work in no
/// tenant, the way those reads run.
/// </summary>
public static class TenancyPostgresChecks
{
    /// <summary>The role PostgREST, and so Supabase's Data API, logs in as before it switches to a caller's role.</summary>
    public const string DataApiLoginRole = "authenticator";

    /// <summary>
    /// A scope that is no module's, which <see cref="EnsureSystemReadsAcrossTenantsAsync"/> asks in to prove that
    /// what answers Tenancy's own work alone answers no other scope.
    /// </summary>
    private const string AnotherScope = "another-scope";

    /// <summary>The property of a unit that holds its tenant: the column the unique index on a tenant's root is on.</summary>
    private const string TenantProperty = "TenantId";

    /// <summary>The property of a unit that holds its parent: a unit without one is a root.</summary>
    private const string ParentProperty = "ParentId";

    /// <summary>
    /// Proves the host still requires explicit callers, as <see cref="TenancyPostgresServiceCollectionExtensions.AddTenancyPostgres"/>
    /// registered: the <see cref="CallerOptions"/> the services resolve, and the row level security interceptor the
    /// contexts use, which read them when it was built. A later registration of the options, or an interceptor
    /// made without them, would let work that said nothing about who it runs as run as the application, past
    /// every policy.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Either one does not require explicit callers, or row level security is not registered.</exception>
    public static void EnsureExplicitCallers(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetService<CallerOptions>() is not { RequireExplicitCallers: true })
        {
            throw new InvalidOperationException(
                "The CallerOptions the services resolve do not require explicit callers, so work that says nothing about who it runs as would run as the application, past every policy. " +
                "Something registered CallerOptions after AddTenancyPostgres(); call services.RequireExplicitCallers() after it, or leave CallerOptions to AddTenancyPostgres().");
        }

        var interceptor = services.GetService<PostgresRowLevelSecurityInterceptor>()
            ?? throw new InvalidOperationException(
                "Row level security is not registered, so Tenancy's policies see no caller. Call services.AddSupabaseRowLevelSecurity() or services.AddPostgresRowLevelSecurity() next to AddTenancyPostgres(), and UsePostgresRowLevelSecurity on the contexts.");

        if (!interceptor.RequireExplicitCallers)
        {
            throw new InvalidOperationException(
                "The row level security interceptor was built without explicit callers, although the options require them: a caller that changes inside a transaction would be logged rather than refused. " +
                "Resolve PostgresRowLevelSecurityInterceptor from the services, after AddTenancyPostgres(), rather than making one by hand.");
        }
    }

    /// <summary>
    /// Proves every token role Tenancy seats (<see cref="TenantSelectionOptions.SeatedTokenRoles"/>) reaches the
    /// database as a signed-in user: the role <c>authenticated</c>, the user's role by its own name, or a token
    /// role mapped to the user's role (<see cref="PostgresRowLevelSecurityOptions.TokenRoles"/>). Tenancy's
    /// policies are written for that one database role. A seated token role that runs as another, a mapped role
    /// of its own or the anonymous caller's, would be a seat in C# and nobody to the database: every read empty
    /// and every write refused, with nothing that says why.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// A seated token role does not run as the user's role, named with what puts it right; or Tenancy or row level
    /// security is not registered.
    /// </exception>
    public static void EnsureSeatedTokenRolesAreSignedInUsers(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var seated = services.GetService<TenantSelectionOptions>()
            ?? throw new InvalidOperationException("Tenancy is not registered, so there are no seated token roles to check. Call services.AddTenancy<TContext>(...) first.");
        var options = services.GetService<PostgresRowLevelSecurityOptions>()
            ?? throw new InvalidOperationException(
                "Row level security is not registered, so no token role reaches the database as any role. Call services.AddSupabaseRowLevelSecurity() or services.AddPostgresRowLevelSecurity() next to AddTenancyPostgres().");

        foreach (var tokenRole in seated.SeatedTokenRoles)
        {
            // As the interceptor picks a role for a token: the map first, then the names of a signed-in user.
            var runsAs = options.TokenRoles.TryGetValue(tokenRole, out var mapped)
                ? mapped
                : string.Equals(tokenRole, PostgresRowLevelSecurityOptions.AuthenticatedRole, StringComparison.Ordinal) || string.Equals(tokenRole, options.UserRole, StringComparison.Ordinal)
                    ? options.UserRole
                    : null;

            if (!string.Equals(runsAs, options.UserRole, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "TenantSelectionOptions.SeatedTokenRoles lists the token role '" + tokenRole + "', which " +
                    (runsAs is null ? "the database is given no role for" : "runs as the database role '" + runsAs + "'") +
                    ", and Tenancy's policies are written for the role of a signed-in user, '" + options.UserRole + "': such a user would hold a seat in the application and be nobody to the database. " +
                    "Map it to that role, PostgresRowLevelSecurityOptions.TokenRoles[\"" + tokenRole + "\"] = \"" + options.UserRole + "\", or take it off SeatedTokenRoles.");
            }
        }
    }

    /// <summary>
    /// Proves Tenancy's reads across tenants answer, with nothing running past the policies: the few reads made
    /// before any tenant is known, the keys stored on every tenant's roles among them
    /// (<see cref="TenancySystemReads"/>). They run as the scoped system role in no tenant, where the policies show
    /// it no row, and ask three functions of the database, which run as their owner:
    /// <see cref="TenancyFunctionNames.RoleKeysInUse"/>, <see cref="TenancyFunctionNames.TenantsToSweep"/> and
    /// <see cref="TenancyFunctionNames.SeatsOfIdentity"/>. This proves that
    /// <list type="bullet">
    /// <item>each is there, runs as its owner, and with an empty search path;</item>
    /// <item>its owner reads the table it answers from past the policies: a superuser, a role with
    /// <c>BYPASSRLS</c>, or the table's owner on a table that does not force row level security on its owner;</item>
    /// <item>the scoped system role may execute it, and neither the user's role, the anonymous caller's nor every
    /// role at once, <c>PUBLIC</c>, may;</item>
    /// <item>asked the way Tenancy asks, the keys in use are answered, on a connection that says who is calling;
    /// and asked in another scope, none is.</item>
    /// </list>
    /// <para>
    /// The last is one of those reads itself: start-up only, on a context of its own.
    /// </para>
    /// </summary>
    /// <param name="services">The application's services, with the context that maps Tenancy's tables.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// One of those does not hold, named with what puts it right; there is no scoped system role, or row level
    /// security is not registered; the store does not leave the rights to the database, so the reads would not go
    /// through the functions at all; or no context maps Tenancy's tables.
    /// </exception>
    public static async Task EnsureSystemReadsAcrossTenantsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.GetService<PostgresRowLevelSecurityOptions>()
            ?? throw new InvalidOperationException(
                "Row level security is not registered, so there is no scoped system role for Tenancy's reads across tenants to run as. Call services.AddSupabaseRowLevelSecurity() or services.AddPostgresRowLevelSecurity() next to AddTenancyPostgres().");
        if (options.SystemInRole is not { } role)
        {
            throw new InvalidOperationException(
                "PostgresRowLevelSecurityOptions.SystemInRole is null, so there is no scoped system role, and Tenancy's reads across tenants run as it: the keys stored on every tenant's roles, " +
                "and the tenants a round of system work visits. Leave SystemInRole as it is, '" + PostgresRowLevelSecurityOptions.DefaultSystemInRole + "', or set it to a role of your own.");
        }

        if (services.GetService<IOptions<TenancyStoreOptions>>() is not { Value.DatabaseKeepsRights: true })
        {
            throw new InvalidOperationException(
                "TenancyStoreOptions.DatabaseKeepsRights is off, so Tenancy's reads across tenants would run as the application itself, over the tables, and not through the functions this proves. " +
                "Something turned it off after AddTenancyPostgres(), or AddTenancyPostgres() was not called; call it, and leave the option to it.");
        }

        await AsSystemAsync(services, async (connection, tenancy) =>
        {
            // Each function with the table it answers from, whose policies its owner has to read past.
            (string Function, IEntityType Table)[] reads =
            [
                (TenancyFunctionNames.RoleKeysInUse, tenancy.Roles),
                (TenancyFunctionNames.TenantsToSweep, tenancy.Tenants),
                (TenancyFunctionNames.SeatsOfIdentity, tenancy.Seats),

                // Where the context maps invitations: the one way the digest of a token is read.
                .. tenancy.InvitationDigests is { } digests ? new[] { (TenancyFunctionNames.InvitationOfDigest, digests) } : [],
            ];
            IEntityType[] tables = [.. reads.Select(read => read.Table)];

            var problems = await ListAsync(
                connection,
                """
                WITH listed AS (
                    SELECT each.name, each.table_schema, each.table_name
                    FROM ROWS FROM (pg_catalog.unnest($2::pg_catalog.text[]), pg_catalog.unnest($3::pg_catalog.text[]), pg_catalog.unnest($4::pg_catalog.text[])) AS each(name, table_schema, table_name)),
                found AS (
                    SELECT listed.name, listed.table_schema, listed.table_name, p.oid, p.prosecdef, p.proconfig, p.proowner,
                           p.oid::pg_catalog.regprocedure::pg_catalog.text AS signature
                    FROM listed
                    JOIN pg_catalog.pg_namespace n ON n.nspname = $1
                    JOIN pg_catalog.pg_proc p ON p.pronamespace = n.oid AND p.proname = listed.name)
                SELECT problem FROM (
                    SELECT 1 AS sort, listed.name || ' is missing: export the access files with Tenancy''s contribution, and apply them.' AS problem
                    FROM listed WHERE NOT EXISTS (SELECT 1 FROM found WHERE found.name = listed.name)
                    UNION ALL
                    SELECT 2, found.signature || ' does not run as its owner, so it would answer what the scoped system role reads itself, which in no tenant is nothing: export the access files with Tenancy''s contribution, and apply them.'
                    FROM found WHERE NOT found.prosecdef
                    UNION ALL
                    SELECT 3, found.signature || ' runs as its owner without an empty search path, so the schemas of the session that asks would decide what it runs: ALTER FUNCTION ' || found.signature || ' SET search_path = '''';'
                    FROM found WHERE found.prosecdef AND NOT coalesce('search_path=""' = ANY (found.proconfig), false)
                    UNION ALL
                    SELECT 4, found.signature || ' is owned by ' || pg_catalog.quote_ident(owner.rolname) || ', which row level security holds back on '
                              || pg_catalog.quote_ident(found.table_schema) || '.' || pg_catalog.quote_ident(found.table_name)
                              || ', so it would answer for no tenant: give the function to the table''s owner, on a table without FORCE ROW LEVEL SECURITY, or to a role with BYPASSRLS, with ALTER FUNCTION '
                              || found.signature || ' OWNER TO ...;'
                    FROM found
                    JOIN pg_catalog.pg_roles owner ON owner.oid = found.proowner
                    JOIN pg_catalog.pg_namespace tn ON tn.nspname = found.table_schema
                    JOIN pg_catalog.pg_class c ON c.relnamespace = tn.oid AND c.relname = found.table_name
                    WHERE c.relrowsecurity AND NOT (owner.rolsuper OR owner.rolbypassrls)
                      AND NOT (pg_catalog.pg_has_role(owner.oid, c.relowner, 'USAGE') AND NOT c.relforcerowsecurity)
                    UNION ALL
                    SELECT 5, 'the scoped system role ' || pg_catalog.quote_ident($5) || ' does not exist: run the access files, or PostgresRowAccess.SetupScript(), first.'
                    WHERE pg_catalog.to_regrole($5) IS NULL
                    UNION ALL
                    SELECT 6, pg_catalog.quote_ident($5) || ' may not execute ' || found.signature || ', which Tenancy''s reads across tenants ask as it: GRANT EXECUTE ON FUNCTION '
                              || found.signature || ' TO ' || pg_catalog.quote_ident($5) || ';'
                    FROM found
                    WHERE pg_catalog.to_regrole($5) IS NOT NULL AND NOT pg_catalog.has_function_privilege(pg_catalog.to_regrole($5), found.oid, 'EXECUTE')
                    UNION ALL
                    SELECT 7, pg_catalog.quote_ident(caller.rolname) || ' may execute ' || found.signature || ', which answers across tenants: REVOKE EXECUTE ON FUNCTION '
                              || found.signature || ' FROM PUBLIC, ' || pg_catalog.quote_ident(caller.rolname) || ';'
                    FROM found
                    JOIN pg_catalog.pg_roles caller ON caller.rolname IN ($6, $7)
                    WHERE pg_catalog.has_function_privilege(caller.oid, found.oid, 'EXECUTE')
                      AND NOT pg_catalog.has_function_privilege('public', found.oid, 'EXECUTE')
                    UNION ALL
                    SELECT 8, 'every role may execute ' || found.signature || ', which answers across tenants: REVOKE EXECUTE ON FUNCTION ' || found.signature || ' FROM PUBLIC;'
                    FROM found WHERE pg_catalog.has_function_privilege('public', found.oid, 'EXECUTE')
                ) wrong
                ORDER BY sort, problem
                """,
                [tenancy.Schema, reads.Select(read => read.Function).ToArray(), SchemasOf(tables), NamesOf(tables), role, options.UserRole, options.AnonymousRole],
                cancellationToken).ConfigureAwait(false);

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Tenancy's reads across tenants would not answer as they should: " + string.Join(" ", problems.Select(problem => "- " + problem)) +
                    " They run as the scoped system role " + role + " in no tenant, where the policies show it no row, and ask the functions " +
                    string.Join(", ", reads.Select(read => read.Function)) + " of " + tenancy.Schema + ", which run as their owner.");
            }
        }, cancellationToken).ConfigureAwait(false);

        // Asked the way Tenancy asks: as its own scoped system work, in no tenant. The connection has to say so
        // itself; one that runs as the role the application logs in as would be answered nothing, in silence.
        var own = await AskedInAsync(services, TenancyWork.SystemScope, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(own.Role, role, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The context that maps Tenancy's tables asked the database as " + own.Role + ", and not as the scoped system role " + role + ", which Tenancy's reads across tenants begin: " +
                "it does not say who is calling, so those reads would be answered nothing. Add UsePostgresRowLevelSecurity(serviceProvider) to the options of that context.");
        }

        var other = await AskedInAsync(services, AnotherScope, cancellationToken).ConfigureAwait(false);
        if (other.Keys != 0)
        {
            throw new InvalidOperationException(
                "The function " + TenancyFunctionNames.RoleKeysInUse + " answered " + other.Keys.ToString(CultureInfo.InvariantCulture) + " keys to system work in another scope than Tenancy's own, " +
                "so it is not as Tenancy's contribution writes it: the keys stored on every tenant's roles are for Tenancy's own work to read. " +
                "Export the access files with Tenancy's contribution, and apply them.");
        }
    }

    /// <summary>
    /// Proves the scoped system role, <see cref="PostgresRowLevelSecurityOptions.SystemInRole"/>, cannot escape the tenant
    /// its policies keep it in: it is not a superuser, has no <c>BYPASSRLS</c>, cannot log in and has the privileges of
    /// no role that owns a table; neither the user's nor the anonymous caller's role has its privileges, nor does the
    /// role of any mapped token role (<see cref="PostgresRowLevelSecurityOptions.TokenRoles"/>), and the Data
    /// API's login role (<see cref="DataApiLoginRole"/>), where there is one, is not a member of it; no mapped token
    /// role has the privileges of the user's role, which would give the holder of such a token, who has no seat,
    /// every policy and every function written for signed-in users; no mapped token role may execute a function in
    /// Tenancy's schema either, by a grant of its own or with the privileges of another role it was given; and
    /// neither the anonymous caller's role nor every role at once, <c>PUBLIC</c>, may execute any of the functions
    /// in Tenancy's schema that run as their owner, the ones that answer about other seats' rights among them.
    /// <para>
    /// The access files give Tenancy's functions to signed-in users and to the scoped system role, and take every
    /// other grant on the ones they write back. A grant made by hand since, <c>GRANT EXECUTE ON ALL FUNCTIONS IN
    /// SCHEMA</c> next to the tables' privileges say, stays until the next file is applied, and on the trigger
    /// functions for good. With it the holder of a token that seats nobody could ask what the functions answer a
    /// seat. Only a grant to a role counts here. What every role may execute, through <c>PUBLIC</c>, is refused
    /// for the functions that run as their owner, whoever the role is, and a function that runs as its caller
    /// reads nothing its caller could not. Every function in the schema counts, whoever wrote it: a function of
    /// the host's own that a mapped role is to run belongs in another schema.
    /// </para>
    /// </summary>
    /// <param name="services">The application's services, with the context that maps Tenancy's tables.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// One of those does not hold, named with the statement that puts it right; the role does not exist; row level
    /// security is not registered; or no context maps Tenancy's tables.
    /// </exception>
    public static Task EnsureSystemInRoleIsConfinedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.GetService<PostgresRowLevelSecurityOptions>()
            ?? throw new InvalidOperationException(
                "Row level security is not registered, so there is no scoped system role to check. Call services.AddSupabaseRowLevelSecurity() or services.AddPostgresRowLevelSecurity() next to AddTenancyPostgres().");
        if (options.SystemInRole is not { } role)
        {
            // Without one, a scoped system caller fails before it connects, and nothing runs as such a role.
            return Task.CompletedTask;
        }

        return AsSystemAsync(services, async (connection, tenancy) =>
        {
            var problems = await ListAsync(
                connection,
                """
                SELECT problem FROM (
                    SELECT 1 AS sort, 'it does not exist: run the access files, or PostgresRowAccess.SetupScript(), first' AS problem
                    WHERE pg_catalog.to_regrole($1) IS NULL
                    UNION ALL
                    SELECT 2, 'it is a superuser, can bypass row level security or can log in: ALTER ROLE ' || pg_catalog.quote_ident($1) || ' NOSUPERUSER NOBYPASSRLS NOLOGIN;'
                    FROM pg_catalog.pg_roles r WHERE r.rolname = $1 AND (r.rolsuper OR r.rolbypassrls OR r.rolcanlogin)
                    UNION ALL
                    SELECT 3, CASE WHEN scoped.oid = owner.oid
                                   THEN 'it owns tables, and Postgres does not hold an owner to row level security: give them another owner with ALTER TABLE ... OWNER TO.'
                                   ELSE 'it has the privileges of ' || pg_catalog.quote_ident(owner.rolname) || ', which owns tables, and Postgres does not hold an owner to row level security: REVOKE '
                                        || pg_catalog.quote_ident(owner.rolname) || ' FROM ' || pg_catalog.quote_ident($1) || ';'
                              END
                    FROM pg_catalog.pg_roles scoped
                    JOIN pg_catalog.pg_roles owner ON owner.oid IN (SELECT c.relowner FROM pg_catalog.pg_class c WHERE c.relkind IN ('r', 'p'))
                    WHERE scoped.rolname = $1 AND pg_catalog.pg_has_role(scoped.oid, owner.oid, 'USAGE')
                    UNION ALL
                    SELECT 4, pg_catalog.quote_ident(caller.rolname) || ' has its privileges, so its callers would get every policy written for it: REVOKE '
                              || pg_catalog.quote_ident($1) || ' FROM ' || pg_catalog.quote_ident(caller.rolname) || ';'
                    FROM pg_catalog.pg_roles caller
                    WHERE (caller.rolname IN ($2, $3) OR caller.rolname::pg_catalog.text = ANY ($6::pg_catalog.text[]))
                      AND pg_catalog.to_regrole($1) IS NOT NULL AND pg_catalog.pg_has_role(caller.oid, pg_catalog.to_regrole($1), 'USAGE')
                    UNION ALL
                    SELECT 5, pg_catalog.quote_ident(mapped.rolname) || ', the role of a mapped token role, has the privileges of ' || pg_catalog.quote_ident($2)
                              || ', so the holder of such a token, who has no seat, would get every policy and every function written for signed-in users: REVOKE '
                              || pg_catalog.quote_ident($2) || ' FROM ' || pg_catalog.quote_ident(mapped.rolname) || ';'
                    FROM pg_catalog.pg_roles mapped
                    WHERE mapped.rolname::pg_catalog.text = ANY ($6::pg_catalog.text[]) AND mapped.rolname::pg_catalog.text <> $2
                      AND pg_catalog.to_regrole($2) IS NOT NULL AND pg_catalog.pg_has_role(mapped.oid, pg_catalog.to_regrole($2), 'USAGE')
                    UNION ALL
                    SELECT 6, pg_catalog.quote_ident(held.mapped) || ', the role of a mapped token role, may execute '
                              || CASE WHEN held.own OR held.total = 1 THEN held.signatures
                                      ELSE held.total::pg_catalog.text || ' functions of ' || pg_catalog.quote_ident($5) || ', ' || held.example || ' among them'
                                 END
                              || CASE WHEN held.own THEN ' by a grant of its own'
                                      ELSE ' with the privileges of ' || pg_catalog.quote_ident(held.through) || ', which it has'
                                 END
                              || ', and Tenancy''s functions are for signed-in users and the scoped system role alone, so the holder of such a token, who has no seat, could ask what they answer a seat: '
                              || CASE WHEN held.own THEN 'REVOKE EXECUTE ON FUNCTION ' || held.signatures || ' FROM ' || pg_catalog.quote_ident(held.mapped) || ';'
                                      ELSE 'REVOKE ' || pg_catalog.quote_ident(held.through) || ' FROM ' || pg_catalog.quote_ident(held.mapped) || ';'
                                 END
                    FROM (SELECT mapped.rolname::pg_catalog.text AS mapped, through.rolname::pg_catalog.text AS through, through.oid = mapped.oid AS own,
                                 pg_catalog.count(*) AS total, pg_catalog.min(executable.signature) AS example,
                                 pg_catalog.string_agg(executable.signature, ', ' ORDER BY executable.signature) AS signatures
                          FROM (SELECT DISTINCT p.oid::pg_catalog.regprocedure::pg_catalog.text AS signature, granted.grantee
                                FROM pg_catalog.pg_proc p
                                JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                                CROSS JOIN LATERAL pg_catalog.aclexplode(coalesce(p.proacl, pg_catalog.acldefault('f', p.proowner))) granted
                                WHERE n.nspname = $5 AND granted.privilege_type = 'EXECUTE') executable
                          JOIN pg_catalog.pg_roles through ON through.oid = executable.grantee
                          JOIN pg_catalog.pg_roles mapped ON mapped.rolname::pg_catalog.text = ANY ($6::pg_catalog.text[])
                          WHERE mapped.rolname::pg_catalog.text <> $2 AND through.rolname::pg_catalog.text NOT IN ($1, $2)
                            AND pg_catalog.pg_has_role(mapped.oid, through.oid, 'USAGE')
                          GROUP BY mapped.oid, mapped.rolname, through.oid, through.rolname) held
                    UNION ALL
                    SELECT 7, pg_catalog.quote_ident(api.rolname) || ', the Data API''s login role, may switch to it, so a request through the Data API could run as it: REVOKE '
                              || pg_catalog.quote_ident($1) || ' FROM ' || pg_catalog.quote_ident(api.rolname) || ';'
                    FROM pg_catalog.pg_roles api
                    WHERE api.rolname = $4 AND pg_catalog.to_regrole($1) IS NOT NULL AND pg_catalog.pg_has_role(api.oid, pg_catalog.to_regrole($1), 'MEMBER')
                    UNION ALL
                    SELECT 8, pg_catalog.quote_ident($3) || ' may execute ' || p.oid::pg_catalog.regprocedure::pg_catalog.text || ', which runs as its owner: REVOKE EXECUTE ON FUNCTION '
                              || p.oid::pg_catalog.regprocedure::pg_catalog.text || ' FROM PUBLIC, ' || pg_catalog.quote_ident($3) || ';'
                    FROM pg_catalog.pg_proc p
                    JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = $5 AND p.prosecdef AND pg_catalog.to_regrole($3) IS NOT NULL
                      AND pg_catalog.has_function_privilege(pg_catalog.to_regrole($3), p.oid, 'EXECUTE')
                      AND NOT pg_catalog.has_function_privilege('public', p.oid, 'EXECUTE')
                    UNION ALL
                    SELECT 9, 'every role may execute ' || p.oid::pg_catalog.regprocedure::pg_catalog.text || ', which runs as its owner: REVOKE EXECUTE ON FUNCTION '
                              || p.oid::pg_catalog.regprocedure::pg_catalog.text || ' FROM PUBLIC;'
                    FROM pg_catalog.pg_proc p
                    JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = $5 AND p.prosecdef AND pg_catalog.has_function_privilege('public', p.oid, 'EXECUTE')
                ) found
                ORDER BY sort, problem
                """,
                [role, options.UserRole, options.AnonymousRole, DataApiLoginRole, tenancy.Schema, options.TokenRoles.Values.Distinct(StringComparer.Ordinal).ToArray()],
                cancellationToken).ConfigureAwait(false);

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "The scoped system role " + role + ", which Tenancy's system work in a tenant runs as, could escape that tenant: " + string.Join(" ", problems.Select(problem => "- " + problem)) +
                    " Tenancy's policies keep system work to its tenant only for a role that is held to them.");
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Proves Tenancy's policies are in the database the application talks to, and that the application leaves to
    /// the database what they take from it: every one of Tenancy's tables has row level security on, and so has
    /// its access history where the context maps one; the units' table has the unique index that keeps a tenant's
    /// organization to a single root; the functions
    /// written from the catalogue, which say which keys manage access, which keys a copy of each pack holds and
    /// which keys are live, answer as the catalogue the application runs with does; the store leaves the rights to
    /// the database (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>), and the trigger that writes them is
    /// on the grants, the seats and the roles; the functions the store asks about other seats' rights, the ones
    /// system work reads across tenants through and the ones that take the tenant as an argument are there,
    /// running as their owner; and the functions a module reads Tenancy through are there, running as
    /// their caller and written for Postgres to fold into the module's queries, each answering exactly the columns
    /// of its row of the read model, in order. A contribution the export was never told to use, or an access file
    /// written from another catalogue or another version of this package and not yet applied, leaves the second
    /// lock open or out of date, and nothing else would say so.
    /// <para>
    /// The index is the one <c>AddTenancy</c> maps when it is given the context's <c>Database</c>, and on Postgres
    /// it is required. The policies keep a seat from leaving a unit without a parent, and hold neither Tenancy's
    /// own system work nor the tables' owner, while a key held at any unit without a parent is held for the whole
    /// tenant: a second root, however it got there, would make whoever holds a key at it a holder for the whole
    /// tenant. It is found by what it does and not by its name: unique, on the units' tenant alone, over the units
    /// without a parent, and valid, which an index that <c>CREATE INDEX CONCURRENTLY</c> could not finish is not.
    /// </para>
    /// <para>
    /// Where the application has operators (<c>TenancyOptions.OperatorTokenRoles</c>), it also proves they read:
    /// each of their token roles is mapped to a database role of its own
    /// (<see cref="PostgresRowLevelSecurityOptions.TokenRoles"/>), and that role has the policy that lets it read
    /// the tenants. The options name the operators for the application and the contribution names them for the
    /// database; when only the first does, an operator is answered an empty directory, in silence.
    /// </para>
    /// </summary>
    /// <param name="services">The application's services, with the context that maps Tenancy's tables and the catalogue.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// A table of Tenancy's has no row level security, the units' table has no unique index on a tenant's root,
    /// named with how to get it, one of the functions is missing, was written from another
    /// catalogue or does not run as it should, a function a module reads Tenancy through answers other columns than
    /// the read model has, the trigger that writes the rights is missing from a table, the store does not leave the
    /// rights to the database, an operator's token role is mapped to no database role of its own or its role has
    /// no policy to read the tenants with, Tenancy is not registered, or no context maps Tenancy's tables.
    /// </exception>
    public static Task EnsurePoliciesAreInPlaceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        var catalogue = services.GetService<TenancyCatalogue>()
            ?? throw new InvalidOperationException("Tenancy is not registered, so there is no catalogue to check the policies against. Call services.AddTenancy<TContext>(...) first.");

        if (services.GetService<IOptions<TenancyStoreOptions>>() is not { Value.DatabaseKeepsRights: true })
        {
            throw new InvalidOperationException(
                "TenancyStoreOptions.DatabaseKeepsRights is off, so a save would write the rights itself, which Tenancy's policies let no caller do: the database writes them. " +
                "Something turned it off after AddTenancyPostgres(), or AddTenancyPostgres() was not called; call it, and leave the option to it.");
        }

        var operatorRoles = OperatorDatabaseRoles(services);

        return AsSystemAsync(services, async (connection, tenancy) =>
        {
            // The operators' database roles, each with the policy that lets it read the tenants: the first table
            // an operator reads, and written with every other of Tenancy's by the same contribution.
            if (operatorRoles.Length > 0)
            {
                var blind = await ListAsync(
                    connection,
                    """
                    SELECT pg_catalog.quote_ident(listed.role) || CASE WHEN r.oid IS NULL THEN ' (the role does not exist)' ELSE '' END
                    FROM pg_catalog.unnest($3::pg_catalog.text[]) AS listed(role)
                    LEFT JOIN pg_catalog.pg_roles r ON r.rolname = listed.role
                    WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                                      JOIN pg_catalog.pg_class c ON c.oid = p.polrelid
                                      JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                                      WHERE n.nspname = $1 AND c.relname = $2 AND p.polpermissive AND p.polcmd IN ('r', '*')
                                        AND r.oid = ANY (p.polroles))
                    ORDER BY 1
                    """,
                    [tenancy.Tenants.GetSchema() ?? PostgresRowAccess.DefaultSchema, tenancy.Tenants.GetTableName()!, operatorRoles],
                    cancellationToken).ConfigureAwait(false);

                if (blind.Count > 0)
                {
                    throw new InvalidOperationException(
                        "No policy lets the operators' database roles " + string.Join(", ", blind) + " read the tenants, so an operator would be answered an empty directory. " +
                        "Give the class derived from TenancyRowAccessContribution the same operator token roles as TenancyOptions.OperatorTokenRoles, as its second constructor argument, " +
                        "export the access files, and apply them.");
                }
            }

            // Tenancy's own tables, its invitations where the context maps them, and its access history where it maps one.
            IEntityType[] secured =
            [
                .. tenancy.All,
                .. tenancy is { Invitations: { } invitations, InvitationDigests: { } digests } ? new[] { invitations, digests } : [],
                .. tenancy.History is { } history ? new[] { history.Log } : [],
            ];
            var open = await ListAsync(
                connection,
                """
                SELECT pg_catalog.quote_ident(listed.schema) || '.' || pg_catalog.quote_ident(listed.name)
                FROM ROWS FROM (pg_catalog.unnest($1::pg_catalog.text[]), pg_catalog.unnest($2::pg_catalog.text[])) AS listed(schema, name)
                WHERE NOT coalesce((SELECT c.relrowsecurity FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                                    WHERE n.nspname = listed.schema AND c.relname = listed.name), false)
                ORDER BY 1
                """,
                [SchemasOf(secured), NamesOf(secured)],
                cancellationToken).ConfigureAwait(false);

            if (open.Count > 0)
            {
                throw new InvalidOperationException(
                    "Row level security is off on Tenancy's tables " + string.Join(", ", open) + ", so nothing holds a seat to its tenant there. " +
                    "List a class derived from TenancyRowAccessContribution with [assembly: UseRowAccessContribution] in the project that runs the export, and apply the access files it writes.");
            }

            // The unique index on a tenant's root. The policies keep a seat from making a second root; system work
            // and the tables' owner they do not hold, and the question that asks whether a key is held for the whole
            // tenant counts every unit without a parent. It is looked for by what it does, since an application
            // names it as it likes: unique, keyed on the tenant alone, over the units without a parent, and in use.
            // Answered nothing where there is one; otherwise what is missing, and after it every index of that
            // shape that Postgres does not use.
            var units = tenancy.Units;
            var rootless = await ListAsync(
                connection,
                """
                WITH shaped AS (
                    SELECT pg_catalog.quote_ident(n.nspname) || '.' || pg_catalog.quote_ident(made.relname) AS name,
                           i.indisvalid AND i.indisready AND i.indislive AS used
                    FROM pg_catalog.pg_index i
                    JOIN pg_catalog.pg_class made ON made.oid = i.indexrelid
                    JOIN pg_catalog.pg_class c ON c.oid = i.indrelid
                    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                    JOIN pg_catalog.pg_attribute tenant ON tenant.attrelid = c.oid AND tenant.attname = $3 AND NOT tenant.attisdropped
                    WHERE n.nspname = $1 AND c.relname = $2
                      AND i.indisunique AND i.indnkeyatts = 1 AND i.indkey[0] = tenant.attnum
                      AND pg_catalog.pg_get_expr(i.indpred, i.indrelid) = '(' || pg_catalog.quote_ident($4) || ' IS NULL)')
                SELECT said FROM (
                    SELECT 0 AS sort, pg_catalog.quote_ident($1) || '.' || pg_catalog.quote_ident($2) || ' over ' || pg_catalog.quote_ident($3)
                                      || ' alone, for the units where ' || pg_catalog.quote_ident($4) || ' IS NULL' AS said
                    UNION ALL
                    SELECT 1, shaped.name FROM shaped
                ) found
                WHERE NOT EXISTS (SELECT 1 FROM shaped WHERE shaped.used)
                ORDER BY sort, said
                """,
                [units.GetSchema() ?? PostgresRowAccess.DefaultSchema, units.GetTableName()!, TenancyTables.ColumnName(units, TenantProperty), TenancyTables.ColumnName(units, ParentProperty)],
                cancellationToken).ConfigureAwait(false);

            if (rootless.Count > 0)
            {
                // Whether the model has it says which half is missing: the call that maps it, or the migration.
                var mapped = units.GetIndexes().FirstOrDefault(index =>
                    index.IsUnique && index.GetFilter() is not null && index.Properties is [{ Name: TenantProperty }]);
                var unused = rootless.Skip(1).ToList();

                throw new InvalidOperationException(
                    "The database has no unique index that keeps a tenant's organization to a single root: none in use on " + rootless[0] + ". On Postgres that index is required: " +
                    "the policies keep a seat from making a second root, and hold neither Tenancy's own system work nor the tables' owner, " +
                    "while a key held at any unit without a parent is held for the whole tenant. " +
                    (unused.Count > 0
                        ? "The index " + string.Join(", ", unused) + " is there, and Postgres does not use it: it is not valid, as CREATE INDEX CONCURRENTLY leaves an index it could not finish. " +
                          "See that no tenant has two roots, and build it again: " + string.Join(" ", unused.Select(index => "REINDEX INDEX " + index + ";"))
                        : mapped is null
                            ? "The model maps no such index either: pass the context's Database to AddTenancy, modelBuilder.AddTenancy(database: Database), which maps it, then add a migration and apply it."
                            : "The model maps it, as " + mapped.GetDatabaseName() + ", so the database is behind the model: add a migration, where none makes the index yet, and apply it."));
            }

            (string Name, string Body, string Says)[] fromTheCatalogue =
            [
                (TenancySql.ManagesAccess, TenancySql.ManagesAccessBody(catalogue), "the keys that manage access are " + string.Join(", ", catalogue.AccessManagingKeys)),
                (TenancySql.PackKeys, TenancySql.PackKeysBody(catalogue), "the packs are " + string.Join(", ", catalogue.Packs.Select(pack => pack.Key)) + ", with the keys the catalogue gives them"),
                (TenancySql.KeyIsLive, TenancySql.KeyIsLiveBody(catalogue), "the live keys are " + string.Join(", ", catalogue.LiveKeys)),
            ];

            foreach (var (name, body, says) in fromTheCatalogue)
            {
                // Found by name in the catalog, which needs no privilege on the schema.
                var written = await ListAsync(
                    connection,
                    """
                    SELECT p.prosrc FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = $1 AND p.proname = $2 AND p.pronargs = 1 AND p.proargtypes[0] = 'pg_catalog.text'::pg_catalog.regtype
                    """,
                    [tenancy.Schema, name],
                    cancellationToken).ConfigureAwait(false);

                if (written.Count != 1 || written[0].Trim() != body)
                {
                    throw new InvalidOperationException(
                        "The function " + tenancy.Schema + "." + name + " in the database " + (written.Count == 1 ? "was written from another catalogue" : "is missing") +
                        ": " + says + ". Export the access files with the catalogue the application runs with, and apply them.");
                }
            }

            // The trigger that writes the rights, on each table it follows: without it on one of them, a change
            // there would leave the rights as they were, and no save writes them.
            IEntityType[] followed = [tenancy.Grants, tenancy.Seats, tenancy.Roles];
            var unfollowed = await ListAsync(
                connection,
                """
                SELECT pg_catalog.quote_ident(listed.schema) || '.' || pg_catalog.quote_ident(listed.name)
                FROM ROWS FROM (pg_catalog.unnest($1::pg_catalog.text[]), pg_catalog.unnest($2::pg_catalog.text[])) AS listed(schema, name)
                WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                                  JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
                                  JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                                  WHERE n.nspname = listed.schema AND c.relname = listed.name AND t.tgname = $3
                                    AND NOT t.tgisinternal AND t.tgenabled <> 'D')
                ORDER BY 1
                """,
                [SchemasOf(followed), NamesOf(followed), TenancySql.RightsFollowGrantsTrigger],
                cancellationToken).ConfigureAwait(false);

            if (unfollowed.Count > 0)
            {
                throw new InvalidOperationException(
                    "The trigger " + TenancySql.RightsFollowGrantsTrigger + ", which writes the rights, is missing or disabled on " + string.Join(", ", unfollowed) +
                    ", so a change there would leave the rights as they were. Export the access files with Tenancy's contribution, and apply them.");
            }

            // The functions that run as their owner, by what they are there for: the ones the store and the
            // questions ask, with the one that writes a tenant's rights again, which Tenancy's system work runs
            // from SQL; the reads across tenants; and the questions that take the tenant as an argument. Without
            // one, or with one that runs as its caller, who reads no such row, what it answers would be answered
            // nothing.
            (string[] Names, string What, string Why)[] asOwner =
            [
                (
                    [TenancyFunctionNames.TenantAdministrators, TenancyFunctionNames.RightsAMoveChanges, TenancyFunctionNames.SeatsHoldingAt, TenancySql.RewriteTenantRights],
                    "answer about other seats' rights or write a tenant's rights again",
                    "The store and the questions ask the first three, since a seat reads only its own rights, and Tenancy's own system work runs the last."),
                (
                    [
                        TenancyFunctionNames.RoleKeysInUse, TenancyFunctionNames.TenantsToSweep, TenancyFunctionNames.SeatsOfIdentity,
                        .. tenancy.InvitationDigests is null ? Array.Empty<string>() : [TenancyFunctionNames.InvitationOfDigest],
                    ],
                    "read across tenants",
                    "System work asks them in no tenant, where the policies show it no row."),
                (
                    [TenancySql.SeatInTenant, TenancySql.SeatedInTenant, TenancySql.HoldsKeyInTenant, TenancySql.UnitsWhereIHoldInTenant, TenancySql.RolesWithKeyInTenant],
                    "take the tenant as an argument",
                    "Policies ask them where the connection names no tenant, as on the path of a stored file."),
            ];

            foreach (var (names, what, why) in asOwner)
            {
                var absent = await ListAsync(
                    connection,
                    """
                    SELECT listed.name || CASE WHEN EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                                                            WHERE n.nspname = $1 AND p.proname = listed.name)
                                               THEN ' (it does not run as its owner)' ELSE ' (missing)' END
                    FROM pg_catalog.unnest($2::pg_catalog.text[]) AS listed(name)
                    WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                                      WHERE n.nspname = $1 AND p.proname = listed.name AND p.prosecdef)
                    ORDER BY 1
                    """,
                    [tenancy.Schema, names],
                    cancellationToken).ConfigureAwait(false);

                if (absent.Count > 0)
                {
                    throw new InvalidOperationException(
                        "The functions of " + tenancy.Schema + " that " + what + " are not as Tenancy's contribution writes them: " + string.Join(", ", absent) +
                        ". " + why + " Export the access files with Tenancy's contribution, and apply them.");
                }
            }

            // The functions a module reads Tenancy through: each answers rows, as its caller, so the tables'
            // policies decide them, and with nothing that keeps Postgres from folding it into the module's query.
            string[] read =
            [
                TenancyFunctionNames.CallerRights, TenancyFunctionNames.TenantUnitPaths, TenancyFunctionNames.TenantUnits,
                TenancyFunctionNames.TenantRoles, TenancyFunctionNames.TenantPlacements, TenancyFunctionNames.TenantSeats,
            ];
            var unreadable = await ListAsync(
                connection,
                """
                SELECT listed.name || CASE WHEN EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                                                        WHERE n.nspname = $1 AND p.proname = listed.name)
                                           THEN ' (it runs as its owner, has settings of its own, or is strict or volatile)' ELSE ' (missing)' END
                FROM pg_catalog.unnest($2::pg_catalog.text[]) AS listed(name)
                WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                                  WHERE n.nspname = $1 AND p.proname = listed.name AND p.pronargs = 0 AND p.proretset
                                    AND NOT p.prosecdef AND p.proconfig IS NULL AND NOT p.proisstrict AND p.provolatile <> 'v')
                ORDER BY 1
                """,
                [tenancy.Schema, read],
                cancellationToken).ConfigureAwait(false);

            if (unreadable.Count > 0)
            {
                throw new InvalidOperationException(
                    "The functions of " + tenancy.Schema + " that modules read Tenancy through are not as Tenancy's contribution writes them: " + string.Join(", ", unreadable) +
                    ". A module's context maps its read model to them with AddTenancyReadFunctions; written otherwise, they would answer past the tables' policies, or as a step of " +
                    "their own that no index serves. Export the access files with Tenancy's contribution, and apply them.");
            }

            // And each answers exactly the columns of its row of the read model, in order. For a function without
            // parameters, the names of its arguments in the catalog are the columns it answers. A function left
            // from another version of the read model would answer a column no row has, a name among them, or lack
            // one a row reads; replacing a function cannot change its columns, so nothing else would say so.
            var columns = TenancySql.ReadFunctionColumns(tenancy);
            var answering = await ListAsync(
                connection,
                """
                SELECT listed.name || ' answers ' || coalesce(found.columns, 'no columns') || ' where the read model has ' || listed.columns
                FROM ROWS FROM (pg_catalog.unnest($2::pg_catalog.text[]), pg_catalog.unnest($3::pg_catalog.text[])) AS listed(name, columns)
                LEFT JOIN LATERAL (SELECT pg_catalog.array_to_string(p.proargnames, ', ') AS columns
                                   FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                                   WHERE n.nspname = $1 AND p.proname = listed.name AND p.pronargs = 0
                                   LIMIT 1) found ON true
                WHERE found.columns IS DISTINCT FROM listed.columns
                ORDER BY 1
                """,
                [tenancy.Schema, read, read.Select(name => string.Join(", ", columns[name])).ToArray()],
                cancellationToken).ConfigureAwait(false);

            if (answering.Count > 0)
            {
                throw new InvalidOperationException(
                    "The functions of " + tenancy.Schema + " that modules read Tenancy through do not answer the columns of the read model: " + string.Join("; ", answering) +
                    ". A module's context reads each row from the function of its name, column by column, and the read model carries access facts only, never a name. " +
                    "Postgres replaces no function by one that answers other columns, so drop the ones named here first. " +
                    "Export the access files with Tenancy's contribution, and apply them.");
            }
        }, cancellationToken);
    }

    /// <summary>
    /// The database roles the application's operators run as: the role each of
    /// <see cref="TenancyOperatorTokenRoles.TokenRoles"/> is mapped to, each once. None for an application without
    /// operators.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Row level security is not registered, or an operator's token role is mapped to no database role, or to the
    /// role of a signed-in user.
    /// </exception>
    private static string[] OperatorDatabaseRoles(IServiceProvider services)
    {
        if (services.GetService<TenancyOperatorTokenRoles>() is not { TokenRoles: { Count: > 0 } operators })
        {
            return [];
        }

        var options = services.GetService<PostgresRowLevelSecurityOptions>()
            ?? throw new InvalidOperationException(
                "Row level security is not registered, so the operators' token roles run as no database role. Call services.AddSupabaseRowLevelSecurity() or services.AddPostgresRowLevelSecurity() next to AddTenancyPostgres().");

        var unmapped = operators.Where(tokenRole => !options.TokenRoles.TryGetValue(tokenRole, out var role) || string.Equals(role, options.UserRole, StringComparison.Ordinal)).ToList();
        if (unmapped.Count > 0)
        {
            throw new InvalidOperationException(
                "The operator token roles " + string.Join(", ", unmapped) + " are mapped to no database role of their own, so an operator's queries would run as no role that reads the tenants. " +
                "Map each in PostgresRowLevelSecurityOptions.TokenRoles to a role that is not the role of a signed-in user, and give the same token roles to the class derived from TenancyRowAccessContribution.");
        }

        return [.. operators.Select(tokenRole => options.TokenRoles[tokenRole]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The schemas of <paramref name="tables"/>, in their order: tables are looked up by name in the catalog,
    /// which needs no privilege on the schema, where resolving a qualified name would.
    /// </summary>
    private static string[] SchemasOf(IReadOnlyList<IEntityType> tables) => [.. tables.Select(table => table.GetSchema() ?? PostgresRowAccess.DefaultSchema)];

    /// <summary>The names of <paramref name="tables"/>, in their order.</summary>
    private static string[] NamesOf(IReadOnlyList<IEntityType> tables) => [.. tables.Select(table => table.GetTableName()!)];

    /// <summary>
    /// Runs <paramref name="check"/> on the connection of a context that maps Tenancy's tables, made in a scope of its
    /// own, as <see cref="Caller.System"/>, begun around it alone and outside any ambient transaction. The caller
    /// is begun before the context is taken, so a host that makes a context for whoever is calling makes this
    /// one for the application itself.
    /// </summary>
    private static async Task AsSystemAsync(IServiceProvider services, Func<DbConnection, TenancyTables, Task> check, CancellationToken cancellationToken)
    {
        using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
        using (Callers.Begin(Caller.System))
        {
            await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            var (context, tenancy) = TenancyContextOf(scope.ServiceProvider);

            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await check(context.Database.GetDbConnection(), tenancy).ConfigureAwait(false);
            }
            finally
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// How many keys <see cref="TenancyFunctionNames.RoleKeysInUse"/> answers scoped system work in
    /// <paramref name="scope"/>, in no tenant, and which role the connection ran as when it asked: on the
    /// connection of a context that maps Tenancy's tables, made in a scope of its own, outside any ambient
    /// transaction.
    /// </summary>
    /// <exception cref="InvalidOperationException">The database refused the role or the function to the connection.</exception>
    private static async Task<(string Role, long Keys)> AskedInAsync(IServiceProvider services, string scope, CancellationToken cancellationToken)
    {
        // The caller first and then the context, as the reads themselves take theirs.
        using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
        using (Callers.Begin(Caller.SystemIn(scope)))
        using (TenancyCallers.BeginNone())
        {
            await using var lifetime = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            var (context, tenancy) = TenancyContextOf(lifetime.ServiceProvider);
            var function = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(TenancyFunctionNames.RoleKeysInUse, tenancy.Schema);

            try
            {
                await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var answered = await ListAsync(
                        context.Database.GetDbConnection(),
                        "SELECT CURRENT_USER::pg_catalog.text || ' ' || (SELECT pg_catalog.count(*) FROM " + function + "() AS held)::pg_catalog.text",
                        [],
                        cancellationToken).ConfigureAwait(false);

                    // A role's name may hold a space; the count never does.
                    var split = answered[0].LastIndexOf(' ');
                    return (answered[0][..split], long.Parse(answered[0][(split + 1)..], CultureInfo.InvariantCulture));
                }
                finally
                {
                    await context.Database.CloseConnectionAsync().ConfigureAwait(false);
                }
            }
            catch (PostgresException refused) when (refused.SqlState == PostgresErrorCodes.InsufficientPrivilege)
            {
                throw new InvalidOperationException(
                    "System work in the scope '" + scope + "' could not ask " + function + "() as the scoped system role: " + refused.MessageText + ". Tenancy's reads across tenants ask it so: " +
                    "the role the application logs in as has to be able to switch to the scoped system role, as PostgresRowAccess.SetupScript(loginRole) lets it, " +
                    "and the scoped system role needs USAGE on the schema, from the application's own migrations, and EXECUTE on the function, from the access files.",
                    refused);
            }
        }
    }

    /// <summary>
    /// The context of the application's services that maps Tenancy's tables: of every context registered with
    /// <c>AddDbContext</c> or with a context pool, the one whose model has them as tables rather than as a read
    /// model's views.
    /// </summary>
    /// <exception cref="InvalidOperationException">None does.</exception>
    private static (DbContext Context, TenancyTables Tables) TenancyContextOf(IServiceProvider services)
    {
        foreach (var options in services.GetServices<DbContextOptions>())
        {
            if (services.GetService(options.ContextType) is DbContext context && TenancyTables.Of(context.Model) is { } tables)
            {
                return (context, tables);
            }
        }

        throw new InvalidOperationException(
            "No context the services make maps Tenancy's tables, so there is nothing to check. Register the context whose model calls modelBuilder.AddTenancy() with AddDbContext, " +
            "or with a context pool and services.AddScopedFromPool<TContext>().");
    }

    /// <summary>The first column of every row <paramref name="sql"/> answers, as text.</summary>
    private static async Task<List<string>> ListAsync(DbConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var value in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
