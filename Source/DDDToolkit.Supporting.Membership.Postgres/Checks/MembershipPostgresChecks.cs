using System.Data.Common;
using System.Transactions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Membership.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.Supporting.Membership.Postgres;

/// <summary>
/// A check a host runs at start-up, before it serves anything, that the database has the functions its row
/// access rules ask about the members of its resources, as the rules it runs with say them. It throws
/// <see cref="InvalidOperationException"/> naming what is wrong and how to put it right, so a host whose
/// second lock would answer from other rules than its first does not start.
/// <para>
/// <see cref="MembershipPostgresServiceCollectionExtensions.AddMembershipPostgres"/> registers it as a start-up
/// check, <see cref="FunctionsInPlaceCheck"/>, which a host runs with <c>services.RunStartupChecks()</c>. The
/// method stays for a host that runs it by hand:
/// </para>
/// <code>
/// var app = builder.Build();
/// await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(app.Services, cancellationToken);
/// </code>
/// </summary>
public static class MembershipPostgresChecks
{
    /// <summary>
    /// The start-up check that the database has the functions and the lock of every registered resource's
    /// membership, written from the rules the resource is registered with (<see cref="EnsureFunctionsAreInPlaceAsync"/>).
    /// It asks the database, so it runs with the checks of the database, once the migrations are found applied.
    /// </summary>
    public const string FunctionsInPlaceCheck = "membership.functions-in-place";

    /// <summary>The category the check logs what it remarks on under.</summary>
    private const string LogCategory = "DDDToolkit.Supporting.Membership.Postgres";

    /// <summary>
    /// Proves that, for every kind of resource with members the application registered, the database has the five
    /// functions of its membership in the schema of the context that maps it, under the names its name gives
    /// them, each running as its owner with an empty search path and answering a set, each written from the
    /// rules the resource is registered with by this version of the package, and each executable by the
    /// database roles the export granted it to and by no other.
    /// <para>
    /// The functions come with the access files the export writes, which makes
    /// <see cref="MembershipRowAccessContribution{TMember}"/> for every resource whose rules the application marks.
    /// Rules it does not mark get no functions, and functions written from other rules, a role that gives another
    /// permission say, would have the database answer otherwise than the application's own check, and nothing else would
    /// say so. The rules a
    /// function was written from are read from its body, which says them in its first line. That line is what
    /// is compared: a body changed by hand below it is not found.
    /// </para>
    /// <para>
    /// Who may execute a function follows from the row access rules of the resource, which the host does not see:
    /// the export grants each function to every role a rule of the resource is for, never the anonymous caller's,
    /// and to the role scoped system work runs as where the rules name a scope, and records them in a line of the
    /// function's body. The grants are asked of the database and held to that line: a grant taken back by hand would
    /// fail every policy that asks the function, and one given to <c>PUBLIC</c>, or to one more role, would let a role
    /// no rule names ask a function that runs as its owner.
    /// </para>
    /// <para>
    /// Where a permission manages the members, or names the owner, the lock written from it is held to as well: a
    /// restrictive policy on both member tables for each of those roles, where a customer makes the roles the one
    /// that holds a role given to one the caller sees, and the trigger on the owner column. Where a customer makes
    /// the roles, their table is one a caller must not write as it likes: a role's row says what everybody who holds
    /// it may do. A role table without row security that one of those roles may write is refused. Which rows a
    /// caller reads and changes there stays the application's own rules to say.
    /// </para>
    /// <para>
    /// It asks the database on the context of each resource, made in a scope of its own, as the application
    /// itself (<see cref="CurrentUserSnapshot.System"/>, begun around the question and outside any transaction the caller
    /// holds), and reads the catalog of Postgres alone: no table of the application's, and nobody's rows.
    /// </para>
    /// </summary>
    /// <param name="services">The application's services, with the context of each resource registered.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>
    /// What the check remarks on without refusing, one line for each resource it concerns, empty when there is
    /// nothing: a resource whose rules name no permission that changes its members, or none that changes its owner,
    /// so the database leaves those rows to whoever the application's row access rules let change the
    /// resource. Each line is logged as a warning as well, where the services have a logger.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// No resource is registered; a resource's context is not registered, does not map its members, marks no
    /// place a resource hangs at that its rules reach from above, or maps no role class for a resource whose roles
    /// a customer makes; a function is missing, does not run as its owner, has no empty search path, answers no
    /// set, was written from other rules, may be executed by every role or by one it was not granted to, or may
    /// not be executed by a role it was granted to; the lock a permission asks for is not in place; or the table of
    /// the roles a customer makes has no row security and may be written by a role the functions are granted to.
    /// Each finding is named, with the resource it is of.
    /// </exception>
    public static async Task<IReadOnlyList<string>> EnsureFunctionsAreInPlaceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registrations = services.GetServices<MembershipRegistration>().ToList();
        if (registrations.Count == 0)
        {
            throw new InvalidOperationException(
                "No resource with members is registered, so there are no functions to check. Register each kind of resource first, with the registration generated for it: services.AddDocumentMembership<TContext>(rules).");
        }

        var remarks = new List<string>();
        foreach (var registration in registrations)
        {
            var found = await FindingsAsync(services, registration, cancellationToken).ConfigureAwait(false);
            if (found.Functions.Count > 0)
            {
                throw new InvalidOperationException(
                    "The functions that answer the membership of " + registration.Resource.Name + " in the database are not as its rules, '" + registration.Rules.Name
                    + "', say them: " + string.Join(", ", found.Functions) + ". A policy that asks them would fail, or answer otherwise than the application's own check. "
                    + "Mark the rules the resource is registered with [MembershipRules<" + registration.Member.Name + ">], where the project that runs the export sees them, "
                    + "and apply the access files it writes.");
            }

            if (found.Lock.Count > 0)
            {
                throw new InvalidOperationException(
                    "A permission of " + registration.Resource.Name + " locks its members or its owner, and the database does not hold callers to it: "
                    + string.Join(", ", found.Lock) + ". Whoever the application's row access rules let change a " + registration.Resource.Name + " could write those rows past the application. "
                    + "Apply the access files the export writes with the rules the resource is registered with: the lock comes with the functions.");
            }

            if (found.RoleTable is { } unguarded)
            {
                throw new InvalidOperationException(
                    "The roles of " + registration.Resource.Name + " are made in " + unguarded.Table + ", which has no row security, and " + unguarded.Roles + " may write it. "
                    + "A role's row says what everybody who holds the role may do, so whoever changes its permissions there changes that, past the application. "
                    + "Give " + unguarded.RoleClass + " row access rules of its own, [RowAccess<" + unguarded.RoleClass + ">], so the access file turns row security on for its table, "
                    + "or take the privileges to write the table from those roles.");
            }

            if (Remark(registration) is { } remark)
            {
                remarks.Add(remark);
            }
        }

        if (remarks.Count > 0 && services.GetService<ILoggerFactory>() is { } loggers)
        {
            var logger = loggers.CreateLogger(LogCategory);
            foreach (var remark in remarks)
            {
#pragma warning disable CA1848 // A handful of lines once, at start-up.
                // The line is the one value of the message, never its template: a name with a brace in it stays a name.
                logger.LogWarning("{Remark}", remark);
#pragma warning restore CA1848
            }
        }

        return remarks;
    }

    /// <summary>
    /// What is to be said, without refusing, of a resource whose rules leave its member rows or its owner
    /// column to the application's own rules about changing the resource; <see langword="null"/> for rules
    /// that name both permissions.
    /// </summary>
    private static string? Remark(MembershipRegistration registration)
    {
        var rules = registration.Rules;
        var resource = registration.Resource.Name;
        var open = (rules.ManagesMembersPermission, rules.NamesOwnerPermission) switch
        {
            (null, null) => "has no permission that locks its members and none that locks its owner (ManagesMembers, NamesOwner), so the database leaves its member rows and its owner column",
            (null, _) => "has no permission that locks its members (ManagesMembers), so the database leaves its member rows",
            (_, null) => "has no permission that locks its owner (NamesOwner), so the database leaves its owner column, and with it every permission an owner holds,",
            _ => null,
        };

        return open is null
            ? null
            : resource + " " + open + " to whoever the application's row access rules let change a " + resource
              + ": such a caller writes them by any statement that reaches the database, without a command of the application's having asked anything. "
              + "Mark the permissions the application's commands require for it [On<" + resource + ">(ManagesMembers = true)] and [On<" + resource + ">(NamesOwner = true)], "
              + "or let no caller that may change a " + resource + " reach the database past the application.";
    }

    /// <summary>
    /// What is wrong with the database's part of one resource; nothing when it is in place. Asked on the
    /// resource's own context, as system work, outside any ambient transaction; the user is begun
    /// before the context is taken, so a host that makes a context for whoever is calling makes this one for
    /// the application.
    /// </summary>
    private static async Task<Found> FindingsAsync(IServiceProvider services, MembershipRegistration registration, CancellationToken cancellationToken)
    {
        using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
        using (new AccessManager(new CurrentUser()).BeginSystemBypassingRowAccess())
        {
            var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var context = scope.ServiceProvider.GetService(registration.Context) as DbContext
                    ?? throw new InvalidOperationException(
                        registration.Context.Name + ", which keeps the members of " + registration.Resource.Name + ", is not a context the services make, so there is nothing to check. "
                        + "Register it with AddDbContext, or with a context pool and services.AddScopedFromPool<TContext>().");

                if (MembershipModel.For(context.Model, registration.Member) is not { } mapping)
                {
                    throw new InvalidOperationException(
                        registration.Context.Name + " does not map the members of " + registration.Resource.Name + ", so there is nothing to check. Map them where the context builds its model: "
                        + "modelBuilder.Entity<" + registration.Resource.Name + ">().HasMembers(...).");
                }

                var rules = registration.Rules;
                if (rules.ReachFromAbove is not null && mapping.At is null)
                {
                    throw new InvalidOperationException(
                        "The rules '" + rules.Name + "' let " + registration.Resource.Name + " be reached from above, and it marks no place it hangs at, so its functions cannot be "
                        + "the ones the rules say. Mark the property that says where it hangs [At], and map it with HasMembers.");
                }

                if (rules.Roles.Kind == RolesKind.Custom && mapping.RoleClass is null)
                {
                    throw new InvalidOperationException(
                        "The rules '" + rules.Name + "' say a customer makes the roles of " + registration.Resource.Name + ", and " + registration.Context.Name
                        + " maps no role class for it, so its functions cannot be the ones the rules say. Map the role class where the context builds its model: modelBuilder.Entity<"
                        + registration.Resource.Name + "Role>().IsCustomRole().");
                }

                var names = services.GetService<PostgresRowLevelSecurityOptions>() is { } options ? RowAccessRoleNames.From(options) : RowAccessRoleNames.Default;
                var marker = MembershipSql.Marker(rules, mapping, names.SystemIn);
                var schema = context.Model.GetDefaultSchema() ?? PostgresRowAccess.DefaultSchema;

                await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var connection = context.Database.GetDbConnection();
                    var roles = await ExecutorsAsync(connection, schema, rules, cancellationToken).ConfigureAwait(false);
                    var functions = await FunctionsAsync(connection, schema, rules, marker, roles, cancellationToken).ConfigureAwait(false);

                    // Asked only of a database whose functions are the rules': a lock is written with them.
                    var locks = functions.Count > 0 ? [] : await LockAsync(connection, schema, mapping, rules, marker, roles, cancellationToken).ConfigureAwait(false);
                    var roleTable = functions.Count > 0 || locks.Count > 0 || rules.Roles.Kind != RolesKind.Custom
                        ? null
                        : await UnguardedRoleTableAsync(connection, mapping.RoleClass!, roles, cancellationToken).ConfigureAwait(false);

                    return new Found(functions, locks, roleTable);
                }
                finally
                {
                    await context.Database.CloseConnectionAsync().ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// The roles the export granted a resource's functions to, as the bodies of its functions record them, the first of
    /// the five that does; none where none does, which the functions' check names.
    /// </summary>
    private static async Task<string[]> ExecutorsAsync(DbConnection connection, string schema, MembershipRules rules, CancellationToken cancellationToken)
    {
        var bodies = await ListAsync(
            connection,
            """
            SELECT p.prosrc FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = $1 AND p.proname = ANY ($2::pg_catalog.text[])
            ORDER BY pg_catalog.array_position($2::pg_catalog.text[], p.proname::pg_catalog.text)
            """,
            [schema, new[] { rules.Functions.AsMember, rules.Functions.AsMemberWith, rules.Functions.Seen, rules.Functions.HeldOn, rules.Functions.FellowMembers }],
            cancellationToken).ConfigureAwait(false);

        var line = bodies
            .Select(body => body.Split('\n').Select(each => each.Trim()).FirstOrDefault(each => each.StartsWith(MembershipSql.ExecutedBy, StringComparison.Ordinal)))
            .FirstOrDefault(found => found is not null);
        return line is null
            ? []
            : [.. line[MembershipSql.ExecutedBy.Length..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
    }

    /// <summary>What is wrong with the five functions of a resource, each as its name with what is wrong in brackets.</summary>
    private static Task<List<string>> FunctionsAsync(DbConnection connection, string schema, MembershipRules rules, string marker, string[] roles, CancellationToken cancellationToken)
    {
        string[] names = [rules.Functions.AsMember, rules.Functions.AsMemberWith, rules.Functions.Seen, rules.Functions.HeldOn, rules.Functions.FellowMembers];
        int[] arguments = [0, 1, 0, 1, 0];

        // Found by name in the catalog, which needs no privilege on the schema. Positional parameters, as the
        // row level security interceptor's own statement binds.
        return ListAsync(
            connection,
            """
            SELECT '"' || pg_catalog.replace($1, '"', '""') || '".' || listed.name || CASE
                       WHEN found.oid IS NULL THEN ' (missing)'
                       WHEN NOT found.prosecdef THEN ' (it does not run as its owner)'
                       WHEN NOT found.pinned THEN ' (it has no empty search path)'
                       WHEN NOT found.proretset THEN ' (it answers no set)'
                       WHEN pg_catalog.strpos(found.prosrc, $4) = 0 THEN ' (it was written from other rules)'
                       WHEN found.everybody THEN ' (every role may execute it)'
                       WHEN found.others IS NOT NULL THEN ' (' || found.others || ' may execute it, which no row access rule of the resource is for)'
                       ELSE ' (' || found.unable || ' may not execute it)' END
            FROM ROWS FROM (pg_catalog.unnest($2::pg_catalog.text[]), pg_catalog.unnest($3::pg_catalog.int4[])) AS listed(name, arguments)
            LEFT JOIN LATERAL (SELECT p.oid, p.prosecdef, p.proretset, p.prosrc,
                                      coalesce('search_path=""' = ANY (p.proconfig), false) AS pinned,
                                      pg_catalog.has_function_privilege('public', p.oid, 'EXECUTE') AS everybody,
                                      (SELECT pg_catalog.string_agg(pg_catalog.quote_ident(granted.rolname), ', ' ORDER BY granted.rolname)
                                       FROM pg_catalog.aclexplode(p.proacl) acl
                                       JOIN pg_catalog.pg_roles granted ON granted.oid = acl.grantee
                                       WHERE acl.privilege_type = 'EXECUTE' AND acl.grantee <> p.proowner
                                         AND NOT (granted.rolname = ANY ($5::pg_catalog.text[]))) AS others,
                                      (SELECT pg_catalog.string_agg(pg_catalog.quote_ident(named.role), ', ' ORDER BY named.role)
                                       FROM pg_catalog.unnest($5::pg_catalog.text[]) AS named(role)
                                       WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                                                         WHERE r.rolname = named.role AND pg_catalog.has_function_privilege(r.oid, p.oid, 'EXECUTE'))) AS unable
                               FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                               WHERE n.nspname = $1 AND p.proname = listed.name AND p.pronargs = listed.arguments
                                 AND (p.pronargs = 0 OR p.proargtypes[0] = 'pg_catalog.text'::pg_catalog.regtype)
                               LIMIT 1) found ON true
            WHERE found.oid IS NULL OR NOT found.prosecdef OR NOT found.pinned OR NOT found.proretset
               OR pg_catalog.strpos(found.prosrc, $4) = 0 OR found.everybody OR found.others IS NOT NULL OR found.unable IS NOT NULL
            ORDER BY 1
            """,
            [schema, names, arguments, marker, roles],
            cancellationToken);
    }

    /// <summary>
    /// What is missing of the lock a permission asks for: a member table a role is not held on, for adding,
    /// changing or removing a row; where a customer makes the roles, the table of the roles a member holds where a
    /// role is not held to roles it sees, for adding or changing a row; and a resource's table whose owner
    /// column is not held. Nothing for rules with neither permission.
    /// </summary>
    private static async Task<List<string>> LockAsync(
        DbConnection connection,
        string schema,
        MemberMapping mapping,
        MembershipRules rules,
        string marker,
        string[] roles,
        CancellationToken cancellationToken)
    {
        var findings = new List<string>();
        if (rules.ManagesMembersPermission is not null)
        {
            findings.AddRange(await UnheldAsync(
                connection, [mapping.Members, mapping.Roles], ["a", "w", "d"], roles, MembershipSql.MemberLock, "is not held to the permission that manages the members", cancellationToken).ConfigureAwait(false));

            if (rules.Roles.Kind == RolesKind.Custom)
            {
                findings.AddRange(await UnheldAsync(
                    connection, [mapping.Roles], ["a", "w"], roles, MembershipSql.RoleSeen, "is not held to the roles it sees", cancellationToken).ConfigureAwait(false));
            }
        }

        if (rules.NamesOwnerPermission is not null && roles.Length > 0)
        {
            var table = mapping.Resource.GetTableName()!;
            var tableSchema = mapping.Resource.GetSchema() ?? PostgresRowAccess.DefaultSchema;

            // The trigger of the lock's, enabled, running a function written from these rules.
            findings.AddRange(await ListAsync(
                connection,
                """
                SELECT '"' || pg_catalog.replace($1, '"', '""') || '".' || pg_catalog.quote_ident($2) || ' (its owner column is not held to the permission that names the owner)'
                WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                                  JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
                                  JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                                  JOIN pg_catalog.pg_proc f ON f.oid = t.tgfoid
                                  JOIN pg_catalog.pg_namespace fn ON fn.oid = f.pronamespace
                                  WHERE n.nspname = $1 AND c.relname = $2 AND t.tgname = $3 AND NOT t.tgisinternal AND t.tgenabled <> 'D'
                                    AND fn.nspname = $5 AND pg_catalog.strpos(f.prosrc, $4) > 0)
                """,
                [tableSchema, table, MembershipSql.OwnerLock(rules), marker, schema],
                cancellationToken).ConfigureAwait(false));
        }

        return findings;
    }

    /// <summary>
    /// The tables of <paramref name="entities"/> on which a role of <paramref name="roles"/> has no restrictive
    /// policy of the lock's, named starting with <paramref name="lockName"/>, for one of
    /// <paramref name="commands"/> (<c>a</c> adding, <c>w</c> changing, <c>d</c> removing), or that check no
    /// row at all: each with the roles and <paramref name="what"/> in brackets.
    /// </summary>
    private static Task<List<string>> UnheldAsync(
        DbConnection connection,
        Microsoft.EntityFrameworkCore.Metadata.IEntityType[] entities,
        string[] commands,
        string[] roles,
        string lockName,
        string what,
        CancellationToken cancellationToken)
    {
        string[] schemas = [.. entities.Select(entity => entity.GetSchema() ?? PostgresRowAccess.DefaultSchema)];
        string[] tables = [.. entities.Select(entity => entity.GetTableName()!)];

        return ListAsync(
            connection,
            """
            SELECT '"' || pg_catalog.replace(listed.schema, '"', '""') || '".' || pg_catalog.quote_ident(listed.name)
                   || ' (' || pg_catalog.string_agg(DISTINCT pg_catalog.quote_ident(named.role), ', ' ORDER BY pg_catalog.quote_ident(named.role))
                   || ' ' || $6 || ')'
            FROM ROWS FROM (pg_catalog.unnest($1::pg_catalog.text[]), pg_catalog.unnest($2::pg_catalog.text[])) AS listed(schema, name)
            CROSS JOIN pg_catalog.unnest($3::pg_catalog.text[]) AS named(role)
            CROSS JOIN pg_catalog.unnest($5::pg_catalog.text[]) AS written(command)
            WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                              JOIN pg_catalog.pg_class c ON c.oid = p.polrelid
                              JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                              JOIN pg_catalog.pg_roles r ON r.oid = ANY (p.polroles)
                              WHERE n.nspname = listed.schema AND c.relname = listed.name AND c.relrowsecurity
                                AND NOT p.polpermissive AND p.polcmd::pg_catalog.text = written.command
                                AND r.rolname = named.role AND pg_catalog.strpos(p.polname, $4) = 1)
            GROUP BY listed.schema, listed.name
            ORDER BY 1
            """,
            [schemas, tables, roles, lockName + " (", commands, what],
            cancellationToken);
    }

    /// <summary>
    /// The table of the roles a customer makes for a resource, when it has no row security and a role the functions
    /// are granted to may write it, with those roles; <see langword="null"/> when it is guarded, or nobody of them may write it.
    /// </summary>
    private static async Task<UnguardedRoleTable?> UnguardedRoleTableAsync(
        DbConnection connection,
        Microsoft.EntityFrameworkCore.Metadata.IEntityType roleClass,
        string[] roles,
        CancellationToken cancellationToken)
    {
        var table = roleClass.GetTableName()!;
        var tableSchema = roleClass.GetSchema() ?? PostgresRowAccess.DefaultSchema;

        // A privilege on the table, or on any column of it: either writes a role's permissions.
        var able = await ListAsync(
            connection,
            """
            SELECT pg_catalog.string_agg(pg_catalog.quote_ident(named.role), ', ' ORDER BY named.role)
            FROM pg_catalog.unnest($3::pg_catalog.text[]) AS named(role)
            JOIN pg_catalog.pg_roles r ON r.rolname = named.role
            JOIN pg_catalog.pg_class c ON c.relname = $2
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace AND n.nspname = $1
            WHERE NOT c.relrowsecurity
              AND (pg_catalog.has_table_privilege(r.oid, c.oid, 'INSERT, UPDATE, DELETE')
                   OR pg_catalog.has_any_column_privilege(r.oid, c.oid, 'INSERT, UPDATE'))
            HAVING pg_catalog.count(*) > 0
            """,
            [tableSchema, table, roles],
            cancellationToken).ConfigureAwait(false);

        return able.Count == 0 ? null : new UnguardedRoleTable(RowAccessModel.Table(roleClass), able[0], roleClass.ClrType.Name);
    }

    /// <summary>The first column of every row <paramref name="sql"/> answers, a text, with <paramref name="values"/> bound in order.</summary>
    private static async Task<List<string>> ListAsync(DbConnection connection, string sql, object[] values, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            foreach (var value in values)
            {
                var parameter = command.CreateParameter();
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            var rows = new List<string>();
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(reader.GetString(0));
                }
            }

            return rows;
        }
    }

    /// <summary>What is wrong with the database's part of one resource.</summary>
    /// <param name="Functions">The functions that are not as the rules say them.</param>
    /// <param name="Lock">What is missing of the lock a permission asks for.</param>
    /// <param name="RoleTable">The table of the roles a customer makes for the resource, when callers may write it unguarded.</param>
    private sealed record Found(List<string> Functions, List<string> Lock, UnguardedRoleTable? RoleTable);

    /// <summary>A role table without row security that roles the functions are granted to may write.</summary>
    /// <param name="Table">The table, with its schema.</param>
    /// <param name="Roles">The roles that may write it.</param>
    /// <param name="RoleClass">The application's role class.</param>
    private sealed record UnguardedRoleTable(string Table, string Roles, string RoleClass);
}
