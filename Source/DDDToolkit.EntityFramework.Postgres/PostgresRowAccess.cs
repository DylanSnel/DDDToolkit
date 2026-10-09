using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Row access rules as the policies Postgres enforces: the rules' SQL with the columns filled in from the
/// Entity Framework model, and the caller asked through <see cref="PostgresCallerFunctions"/>.
/// <code>
/// // A migration of your own, on a Postgres that is not Supabase's
/// migrationBuilder.Sql(PostgresRowAccess.SetupScript());
/// migrationBuilder.Sql(PostgresRowAccess.Script(context, rules));
/// </code>
/// </summary>
/// <remarks>
/// The policies of a module are written the declarative way. A script first drops every policy an earlier
/// one made on the module's tables, found by the comment each carries, and then makes them all again, so
/// a script says what the rules are now, a rule taken out disappears with the next one, and a policy
/// written by hand, without the comment, is left alone. <c>DDDToolkit.EntityFramework.Supabase</c> writes
/// the same script into <c>supabase/migrations</c> as part of the build.
/// <para>
/// Every policy is for one role. A rule names its roles symbolically, <c>RowAccessRoles.User</c> and the
/// like, <c>RowAccessRoles.Token("analyst")</c> for a token role the host mapped, or by their own names, and a
/// script writes the roles its <see cref="RowAccessExport"/> says the symbols stand for. The rules that allow a
/// command to a role are asked in one policy.
/// </para>
/// <para>
/// A package or a module writes row level security of its own through an <see cref="IRowAccessContribution"/>
/// in <see cref="RowAccessExport.Contributions"/>: functions, policies and statements the script writes next
/// to the rules'.
/// </para>
/// </remarks>
public static partial class PostgresRowAccess
{
    /// <summary>The comment on every policy made from a rule, which is how the next script finds it to drop.</summary>
    public const string PolicyComment = "DDDToolkit row access rule";

    /// <summary>The comment on every trigger made from a column rule, which is how the next script finds it to drop.</summary>
    public const string ColumnRuleComment = "DDDToolkit column rule";

    /// <summary>The schema of a table the model gives none.</summary>
    public const string DefaultSchema = "public";

    private const int MaxIdentifierBytes = 63;

    /// <summary>The tag the <c>DO</c> blocks of a script are quoted with.</summary>
    private const string BlockTag = "$ddd$";

    /// <summary>
    /// The alias of the aggregate's root row in the policies of its entities' tables, which ask the root's
    /// write rules about it.
    /// </summary>
    private const string RootAlias = "r";

    /// <summary>
    /// <c>ddd.written_in_this_transaction(xmin)</c>: whether the visible version of a row was written,
    /// inserted or updated, by the transaction that is running, its savepoints included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>xmin</c> is the 32-bit id of the transaction that wrote the row version, and inside a savepoint it
    /// is the savepoint's own id, not the transaction's. A transaction's own ids are its top-level id,
    /// <c>pg_current_xact_id()</c>, and the ids of its savepoints, which are all handed out after it. So the
    /// function counts how far the row's id is after the top-level one, around the 32-bit wrap: half the
    /// range or more means it is before it, and the row was written before the transaction began.
    /// Otherwise the row's full id is the top-level one plus that distance, and <c>pg_xact_status</c> says
    /// whether that transaction is still in progress. A visible row version whose writer is still in
    /// progress can only be this transaction's own: Postgres hides every other transaction's uncommitted
    /// rows.
    /// </para>
    /// <para>
    /// A row version so old that Postgres froze it, written more than 2^31 transactions ago, keeps the id it
    /// was written with, and that id can read as one after the transaction's own. Then either the id has
    /// not been handed out yet, and <c>pg_xact_status</c> fails the statement, which refuses the insert as
    /// surely as the policy would; or it is one of the few handed out since the transaction began and still
    /// in progress, and the row counts as written.
    /// </para>
    /// <para>
    /// Every type name is qualified with <c>pg_catalog</c>. The function runs as its caller, and with an
    /// empty search path Postgres still looks for a type name in the session's temporary schema first, where
    /// a caller with a login of its own could put a <c>text</c> of its own.
    /// </para>
    /// </remarks>
    private const string WrittenInThisTransactionBody =
        "\n" +
        "    SELECT CASE WHEN written.ahead >= 2147483648 THEN false\n" +
        "                ELSE coalesce(pg_catalog.pg_xact_status((written.top + written.ahead)::pg_catalog.text::pg_catalog.xid8) = 'in progress', false)\n" +
        "           END\n" +
        "    FROM (SELECT current.id AS top, (row_xmin::pg_catalog.text::bigint - (current.id & 4294967295)) & 4294967295 AS ahead\n" +
        "          FROM (SELECT pg_catalog.pg_current_xact_id()::pg_catalog.text::bigint AS id) current) written\n";

    /// <summary>What <see cref="WrittenInThisTransactionBody"/> is the body of, up to the body.</summary>
    private const string WrittenInThisTransactionHeader =
        "CREATE OR REPLACE FUNCTION ddd.written_in_this_transaction(row_xmin xid) RETURNS boolean LANGUAGE sql STABLE SET search_path = '' AS ";

    /// <summary>The signature <see cref="WrittenInThisTransactionBody"/> is found and granted by.</summary>
    private const string WrittenInThisTransactionSignature = "ddd.written_in_this_transaction(xid)";

    /// <summary>
    /// <c>ddd.use_caller(role, claims, names, values)</c>: sets a caller's role, claims and settings for the
    /// running transaction alone, which is how they travel where nothing may live on a session
    /// (<see cref="RowLevelSecurityScope.Transaction"/>). The interceptor calls it in front of a command that runs
    /// outside a transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It empties the four settings Supabase's caller functions read before the claims, as the interceptor's own
    /// statement does, so a value a pooled server connection still carries cannot decide who a statement runs as.
    /// </para>
    /// <para>
    /// It runs as its caller, the role the application logged in as, which is the role that may switch roles;
    /// it has no <c>SET</c> clause, and every function it calls is qualified. It is executable by that role
    /// alone, never by <c>PUBLIC</c> or a role a caller runs as: whoever could call it could say who they are.
    /// </para>
    /// </remarks>
    private const string UseCallerBody =
        "\n" +
        "BEGIN\n" +
        "    PERFORM pg_catalog.set_config('role', role_name, true);\n" +
        "    PERFORM pg_catalog.set_config('request.jwt.claims', claims, true);\n" +
        "    PERFORM pg_catalog.set_config('request.jwt.claim.sub', '', true);\n" +
        "    PERFORM pg_catalog.set_config('request.jwt.claim.role', '', true);\n" +
        "    PERFORM pg_catalog.set_config('request.jwt.claim.email', '', true);\n" +
        "    PERFORM pg_catalog.set_config('request.jwt.claim', '', true);\n" +
        "    FOR i IN 1 .. coalesce(pg_catalog.array_length(setting_names, 1), 0) LOOP\n" +
        "        PERFORM pg_catalog.set_config(setting_names[i], setting_values[i], true);\n" +
        "    END LOOP;\n" +
        "END\n";

    /// <summary>What <see cref="UseCallerBody"/> is the body of, up to the body.</summary>
    private const string UseCallerHeader =
        "CREATE OR REPLACE PROCEDURE ddd.use_caller(role_name text, claims text, setting_names text[], setting_values text[]) LANGUAGE plpgsql AS ";

    /// <summary>The signature <see cref="UseCallerBody"/> is found, revoked and granted by.</summary>
    internal const string UseCallerSignature = "ddd.use_caller(text, text, text[], text[])";

    /// <summary>The commands an entity's table gets a policy for, in the order a script writes them.</summary>
    private static readonly string[] EntityCommands = ["SELECT", "INSERT", "UPDATE", "DELETE"];

    /// <summary>The commands an aggregate's own table gets a policy for, in the order a script writes them, with the operation of a rule that grants each.</summary>
    private static readonly (string Command, RowOperations Operation)[] RootCommands =
    [
        ("SELECT", RowOperations.Read),
        ("INSERT", RowOperations.Create),
        ("UPDATE", RowOperations.Change),
        ("DELETE", RowOperations.Remove),
    ];

    /// <summary>
    /// What this context's rules are now: every policy an earlier script made on its tables dropped, and
    /// the policies of <paramref name="rules"/> that guard an aggregate it maps made again.
    /// </summary>
    /// <param name="context">The context whose model says which tables and columns the rules read.</param>
    /// <param name="rules">The rules; those of aggregates other contexts map are left out.</param>
    /// <param name="accessFunctions">The access functions the rules call; those of aggregates other contexts map are left out, and written by those.</param>
    /// <param name="callerFunctions">How a policy asks about the caller; <see cref="PostgresCallerFunctions.Toolkit"/> when left out.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="rules"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A rule names a role no policy can be for, such as <c>PUBLIC</c> or a token role nobody mapped.</exception>
    public static string Script(
        DbContext context,
        IEnumerable<RowAccessRule> rules,
        IEnumerable<RowAccessFunction>? accessFunctions = null,
        PostgresCallerFunctions? callerFunctions = null)
        => Script(context, rules, accessFunctions, new RowAccessExport { CallerFunctions = callerFunctions ?? PostgresCallerFunctions.Toolkit });

    /// <summary>
    /// What this context's rules are now, written with <paramref name="export"/>'s caller functions and
    /// roles: every policy an earlier script made on its tables dropped, and the policies of
    /// <paramref name="rules"/> that guard an aggregate it maps made again.
    /// </summary>
    /// <param name="context">The context whose model says which tables and columns the rules read.</param>
    /// <param name="rules">The rules; those of aggregates other contexts map are left out.</param>
    /// <param name="accessFunctions">The access functions the rules call; those of aggregates other contexts map are left out, and written by those.</param>
    /// <param name="export">
    /// How a policy asks about the caller, the roles the rules' symbolic roles become, the contributions to
    /// ask, and where the functions of other contexts live.
    /// </param>
    /// <remarks>
    /// A script of one context grants its access functions to the roles of its own policies alone. Where a
    /// policy of another context asks one of them for another role, write the contexts together with
    /// <see cref="Scripts"/>, or grant it yourself after the script.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="context"/>, <paramref name="rules"/> or <paramref name="export"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// A rule names a role no policy can be for, such as <c>PUBLIC</c> or a token role the export's roles do not
    /// map, or a function nothing defines; a policy asks a contributed function for a role it is not granted to;
    /// two functions, or two policies of a table, would have one name; the functions ask each other in a circle;
    /// or a contribution writes what a script cannot.
    /// </exception>
    public static string Script(
        DbContext context,
        IEnumerable<RowAccessRule> rules,
        IEnumerable<RowAccessFunction>? accessFunctions,
        RowAccessExport export)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(export);

        return DropStatement(context) + CreateStatements(context, RulesOf(context, rules), FunctionsOf(context, accessFunctions ?? []), export);
    }

    /// <summary>
    /// <see cref="Script(DbContext, IEnumerable{RowAccessRule}, IEnumerable{RowAccessFunction}?, RowAccessExport)"/>
    /// for several contexts, in the order to run them: a context before every context whose rules, access
    /// functions or contributions ask a function it defines, because Postgres refuses a policy or a function
    /// that asks one which does not exist yet, and otherwise in the order given. A rule of one context may ask
    /// a function another defines by its logical name, <c>owner/name</c>: each script is written knowing where
    /// every context's functions live, and grants each access function to the roles of every policy, in any of
    /// the contexts, that asks it.
    /// </summary>
    /// <param name="contexts">The contexts, typically one per module.</param>
    /// <param name="rules">Every rule; each context writes those of the aggregates it maps.</param>
    /// <param name="accessFunctions">Every access function; each is written by the context that maps its aggregate.</param>
    /// <param name="export">How a policy asks about the caller, the roles the rules' symbolic roles become, and the contributions to ask.</param>
    /// <returns>Each context with its script, in the order to run them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="contexts"/>, <paramref name="rules"/> or <paramref name="export"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Two functions would have one name, in one context or two, a rule names a role no policy can be for, a
    /// policy asks a contributed function for a role it is not granted to, or the contexts ask each other's
    /// functions in a circle.
    /// </exception>
    public static IReadOnlyList<(DbContext Context, string Script)> Scripts(
        IEnumerable<DbContext> contexts,
        IEnumerable<RowAccessRule> rules,
        IEnumerable<RowAccessFunction>? accessFunctions,
        RowAccessExport export)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(export);
        export.Roles.EnsureOwnRoles();

        var everyRule = rules.ToList();
        var everyFunction = (accessFunctions ?? []).ToList();
        var prepared = contexts
            .Select(context => Prepare(context, RulesOf(context, everyRule), FunctionsOf(context, everyFunction), export))
            .ToList();

        var names = NamesAcross(prepared, export.FunctionNames);
        var knowledge = Know(prepared, names);

        return [.. InDependencyOrder(prepared, names)
            .Select(each => (each.Context, DropStatement(each.Context) + Emit(each, OwnFirst(each, names), knowledge, export)))];
    }

    /// <summary>
    /// What row level security needs on a Postgres that is not Supabase's, where every project has it
    /// already: the roles a caller's queries run as, the roles of the mapped token roles among them, which the
    /// application's login role may switch to, the <c>ddd.caller_id()</c>, <c>ddd.caller_role()</c> and
    /// <c>ddd.caller_claims()</c> functions policies ask about the caller,
    /// <c>ddd.written_in_this_transaction(xmin)</c>, which the policies of an aggregate's entities ask about its
    /// root, and the procedure <c>ddd.use_caller</c>, which sets a caller for one transaction and which the
    /// login role alone may call. It can run again, so it can go in the first migration and stay there.
    /// </summary>
    /// <param name="options">The roles; PostgREST's, <c>anon</c> and <c>authenticated</c>, and <c>ddd_system_in</c>, when left out.</param>
    /// <param name="loginRole">
    /// The role the application logs in as, which gets to switch to the roles; the role running the script
    /// when left out, which is right when the application migrates its own database.
    /// </param>
    /// <remarks>
    /// <para>
    /// The tables still need the privileges a caller's queries use; the policies then decide which rows. A
    /// <see cref="Script(DbContext, IEnumerable{RowAccessRule}, IEnumerable{RowAccessFunction}?, RowAccessExport)"/>
    /// written with <see cref="RowAccessExport.WriteGrants"/> writes them from the policies, table by table and
    /// command by command. Without it they are yours to grant, <c>GRANT SELECT, INSERT, UPDATE, DELETE</c> to
    /// the roles, as they would be for PostgREST.
    /// </para>
    /// <para>
    /// <see cref="PostgresRowLevelSecurityOptions.SystemInRole"/> is made as a role that can neither log in
    /// nor bypass row level security, and the script fails when a role of that name exists that can do
    /// either, is a superuser, has the privileges of a role that owns tables, or is granted to the user's or
    /// the anonymous caller's role: such a role would read and write past every policy written for it, or
    /// hand those policies to every caller.
    /// </para>
    /// <para>
    /// Every role a token role is mapped to, <see cref="PostgresRowLevelSecurityOptions.TokenRoles"/>, is made
    /// the same way and held to the same: the script fails when a role of that name exists that can log in or
    /// bypass row level security, is a superuser, has the privileges of a role that owns tables, or is granted
    /// to the user's or the anonymous caller's role. A mapped role is kept apart from those two in the other
    /// direction as well: the script fails when it has the privileges of the user's or the anonymous caller's
    /// role, since the holder of its token would then get every policy and every privilege written for them.
    /// It also fails when a mapped role has the privileges of the scoped system role, since a token's holder
    /// would then do the application's own work. A token role mapped to the user's own role is that role, and
    /// is not checked.
    /// </para>
    /// <para>
    /// The role that runs this owns the <c>ddd</c> schema, its functions and its procedure. A
    /// <see cref="Script(DbContext, IEnumerable{RowAccessRule}, IEnumerable{RowAccessFunction}?, RowAccessExport)">script</see>
    /// run later by another role, the application's, leaves them alone while they are as it needs them. When a
    /// new version of the toolkit changes <c>ddd.written_in_this_transaction</c> or <c>ddd.use_caller</c>, such a
    /// script cannot replace what it does not own: run this again first, as the role that ran it before.
    /// </para>
    /// <para>
    /// <c>ddd.use_caller</c> is taken from <c>PUBLIC</c> and given to <paramref name="loginRole"/>, or to the role
    /// running the script, together with the right to use the <c>ddd</c> schema. A login role that is neither, one
    /// made afterwards, needs both before it can run a context whose settings last one transaction
    /// (<see cref="RowLevelSecurityScope.Transaction"/>): <c>GRANT USAGE ON SCHEMA ddd TO</c> that role, and
    /// <c>GRANT EXECUTE ON PROCEDURE ddd.use_caller(text, text, text[], text[]) TO</c> it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="loginRole"/> is empty, or a role in <paramref name="options"/> is empty or cannot be a policy's.</exception>
    public static string SetupScript(PostgresRowLevelSecurityOptions? options = null, string? loginRole = null)
    {
        options ??= new PostgresRowLevelSecurityOptions();
        options.Validate();
        if (loginRole is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(loginRole);
        }

        List<string> roles = [options.AnonymousRole, options.UserRole];
        if (options.SystemInRole is { } systemIn)
        {
            roles.Add(systemIn);
        }

        // The roles of the mapped token roles, without the user's own: a token role mapped to that is that role.
        var tokenRoles = RowAccessRoleNames.From(options).TokenDatabaseRoles;
        roles.AddRange(tokenRoles);
        roles = [.. roles.Distinct(StringComparer.Ordinal)];
        var quoted = string.Join(", ", roles.Select(Quote));
        var login = loginRole is null ? "CURRENT_USER" : Quote(loginRole);

        var script = new StringBuilder()
            .Append("-- What row level security needs on a Postgres that is not Supabase's: the roles a caller's queries").Append('\n')
            .Append("-- run as, and the functions a policy asks about the caller. Written by DDDToolkit; it can run again.").Append('\n')
            .Append("DO $ddd$").Append('\n')
            .Append("BEGIN").Append('\n');

        foreach (var role in roles)
        {
            script.Append(RoleStatement(role, Quote(role)));
        }

        // Each mapped role on its own first: a role that is a superuser has every role's privileges, and should
        // be refused for what it is rather than for reaching the scoped system role.
        foreach (var tokenRole in tokenRoles)
        {
            script.Append(ConfinedRoleCheck(tokenRole, [options.UserRole, options.AnonymousRole], bothWays: true));
        }

        if (options.SystemInRole is { } scoped)
        {
            script.Append(ConfinedRoleCheck(scoped, [options.UserRole, options.AnonymousRole, .. tokenRoles]));
        }

        return Blocks(1, script
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .Append("GRANT ").Append(quoted).Append(" TO ").Append(login).Append(';').Append('\n')
            .Append('\n')
            .Append("CREATE SCHEMA IF NOT EXISTS ddd;").Append('\n')
            .Append("GRANT USAGE ON SCHEMA ddd TO ").Append(quoted).Append(';').Append('\n')
            .Append('\n')
            .Append("-- The claims of the caller's token, as the interceptor put them on the connection.").Append('\n')
            .Append("CREATE OR REPLACE FUNCTION ddd.caller_claims() RETURNS jsonb LANGUAGE sql STABLE AS $function$").Append('\n')
            .Append("    SELECT nullif(current_setting('request.jwt.claims', true), '')::jsonb").Append('\n')
            .Append("$function$;").Append('\n')
            .Append('\n')
            .Append("-- The user's id, or null when there is none or it is not a uuid: a rule about a user then matches nobody.").Append('\n')
            .Append("CREATE OR REPLACE FUNCTION ddd.caller_id() RETURNS uuid LANGUAGE sql STABLE AS $function$").Append('\n')
            .Append("    SELECT CASE WHEN sub ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN sub::uuid END").Append('\n')
            .Append("    FROM (SELECT ddd.caller_claims() ->> 'sub' AS sub) caller").Append('\n')
            .Append("$function$;").Append('\n')
            .Append('\n')
            .Append("CREATE OR REPLACE FUNCTION ddd.caller_role() RETURNS text LANGUAGE sql STABLE AS $function$").Append('\n')
            .Append("    SELECT ddd.caller_claims() ->> 'role'").Append('\n')
            .Append("$function$;").Append('\n')
            .Append('\n')
            .Append("-- Whether a row was written, inserted or updated, by the running transaction, savepoints included.").Append('\n')
            .Append(WrittenInThisTransactionHeader).Append("$function$").Append(WrittenInThisTransactionBody).Append("$function$;").Append('\n')
            .Append("GRANT EXECUTE ON FUNCTION ").Append(WrittenInThisTransactionSignature).Append(" TO ").Append(quoted).Append(';').Append('\n')
            .Append('\n')
            .Append("-- A caller's role, claims and settings for the running transaction alone, which is how they travel through a").Append('\n')
            .Append("-- pooler that hands each transaction another server connection. Only the role the application logs in as may").Append('\n')
            .Append("-- call it: whoever could call it could say who they are.").Append('\n')
            .Append(UseCallerHeader).Append("$procedure$").Append(UseCallerBody).Append("$procedure$;").Append('\n')
            .Append("REVOKE ALL ON PROCEDURE ").Append(UseCallerSignature).Append(" FROM PUBLIC;").Append('\n')
            .Append("GRANT USAGE ON SCHEMA ddd TO ").Append(login).Append(';').Append('\n')
            .Append("GRANT EXECUTE ON PROCEDURE ").Append(UseCallerSignature).Append(" TO ").Append(login).Append(';').Append('\n')
            .ToString());
    }

    /// <summary>
    /// Statements of a <c>DO</c> block that make <paramref name="role"/>, written as
    /// <paramref name="identifier"/>, where the database does not have it, as a role that can neither log in
    /// nor pass its privileges on.
    /// </summary>
    /// <remarks>
    /// Roles belong to the whole server, so a script migrating another database may make the same role at
    /// the same moment. This one then waits for that one to commit, and finds the role made rather than
    /// failing.
    /// </remarks>
    private static string RoleStatement(string role, string identifier)
        => new StringBuilder()
            .Append("    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = ").Append(Literal(role)).Append(") THEN").Append('\n')
            .Append("        BEGIN").Append('\n')
            .Append("            CREATE ROLE ").Append(identifier).Append(" NOLOGIN NOINHERIT;").Append('\n')
            .Append("        EXCEPTION WHEN duplicate_object OR unique_violation THEN").Append('\n')
            .Append("            NULL; -- made by a script that ran at the same time").Append('\n')
            .Append("        END;").Append('\n')
            .Append("    END IF;").Append('\n')
            .ToString();

    /// <summary>
    /// Statements of a <c>DO</c> block that fail it when <paramref name="role"/>, the scoped system role or the
    /// role of a mapped token role, could void or spread the policies written for it: when it can log in or
    /// bypass row level security, is a superuser, has the privileges of a role that owns tables, or has its own
    /// privileges reach one of <paramref name="callers"/>, the roles other callers run as; and, for the role of
    /// a mapped token role, when it has the privileges of one of them itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Postgres counts a role that has the privileges of a table's owner as the owner, and does not hold an
    /// owner to row level security; and a policy for a role applies to every role that has its privileges.
    /// Both follow the grants between roles, whatever each role's own attributes say.
    /// </para>
    /// <para>
    /// The same holds the other way round, which is what <paramref name="bothWays"/> asks for the role of a
    /// mapped token role: a mapped role that has the privileges of the user's role is a signed-in user in
    /// everything but its name, and one that has the anonymous caller's gets, with a user's identity, what was
    /// written for callers who have none. A host maps a token role to a role of its own to keep the two apart,
    /// so the script fails rather than write policies that do not. The roles only the application's own work
    /// runs as are not asked: no token picks them.
    /// </para>
    /// </remarks>
    /// <param name="role">The role to hold to the policies written for it.</param>
    /// <param name="callers">
    /// The roles that must not have its privileges: the user's and the anonymous caller's, and for the scoped
    /// system role every mapped token role as well.
    /// </param>
    /// <param name="given">What a caller's role granted <paramref name="role"/> would get, as the message says it.</param>
    /// <param name="bothWays">
    /// Whether <paramref name="role"/> must not have the privileges of <paramref name="callers"/> either, as the
    /// role of a mapped token role must not.
    /// </param>
    private static string ConfinedRoleCheck(string role, IReadOnlyList<string> callers, string given = "get every policy written for it", bool bothWays = false)
    {
        var check = new StringBuilder()
            .Append("    IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = ").Append(Literal(role)).Append('\n')
            .Append("               AND (rolbypassrls OR rolsuper OR rolcanlogin)) THEN").Append('\n')
            .Append("        RAISE EXCEPTION USING MESSAGE = ").Append(Literal(
                $"The role {role} exists, but it can bypass row level security or log in. It must be NOLOGIN, without BYPASSRLS and not a superuser.")).Append(';').Append('\n')
            .Append("    END IF;").Append('\n')
            .Append("    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_class c").Append('\n')
            .Append("               WHERE scoped.rolname = ").Append(Literal(role)).Append(" AND c.relkind IN ('r', 'p')").Append('\n')
            .Append("                 AND pg_catalog.pg_has_role(scoped.oid, c.relowner, 'USAGE')) THEN").Append('\n')
            .Append("        RAISE EXCEPTION USING MESSAGE = ").Append(Literal(
                $"The role {role} has the privileges of a role that owns tables, and Postgres does not hold an owner to row level security. It must neither own a table nor be granted a role that does.")).Append(';').Append('\n')
            .Append("    END IF;").Append('\n')
            .Append("    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_roles callers").Append('\n')
            .Append("               WHERE scoped.rolname = ").Append(Literal(role)).Append(" AND callers.rolname IN (").Append(string.Join(", ", callers.Select(Literal))).Append(')').Append('\n')
            .Append("                 AND pg_catalog.pg_has_role(callers.oid, scoped.oid, 'USAGE')) THEN").Append('\n')
            .Append("        RAISE EXCEPTION USING MESSAGE = ").Append(Literal(
                $"The role {role} is granted to {Alternatives(callers)}, so their callers would {given}. Grant it only to the role the application logs in as.")).Append(';').Append('\n')
            .Append("    END IF;").Append('\n');

        if (bothWays)
        {
            // The arguments of pg_has_role the other way round: whether the role itself has what a caller's role has.
            check
                .Append("    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_roles callers").Append('\n')
                .Append("               WHERE scoped.rolname = ").Append(Literal(role)).Append(" AND callers.rolname IN (").Append(string.Join(", ", callers.Select(Literal))).Append(')').Append('\n')
                .Append("                 AND pg_catalog.pg_has_role(scoped.oid, callers.oid, 'USAGE')) THEN").Append('\n')
                .Append("        RAISE EXCEPTION USING MESSAGE = ").Append(Literal(
                    $"The role {role} has the privileges of {Alternatives(callers)}, so the holder of a token mapped to it would get every policy and every privilege written for those callers. Take that grant back: a mapped role has what is written for it and no more.")).Append(';').Append('\n')
                .Append("    END IF;").Append('\n');
        }

        return check.ToString();
    }

    /// <summary><c>a</c>, <c>a or b</c>, <c>a, b or c</c>.</summary>
    private static string Alternatives(IReadOnlyList<string> items)
        => items.Count < 2 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " or " + items[^1];

    /// <summary>
    /// Statements of a <c>DO</c> block that grant <paramref name="role"/>, written as <paramref name="identifier"/>,
    /// to the role running the script, unless that role may switch to it already: since Postgres 16 the role that
    /// creates a role may administer it, but not switch to it until it grants itself the role. A script running
    /// at the same moment, for another database on the server, may grant it too; this one then finds it granted.
    /// </summary>
    private static string GrantToTheRunningRole(string role, string identifier)
        => new StringBuilder()
            .Append("    IF NOT pg_catalog.pg_has_role(CURRENT_USER, ").Append(Literal(role)).Append("::pg_catalog.name,").Append('\n')
            .Append("            CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN").Append('\n')
            .Append("        BEGIN").Append('\n')
            .Append("            GRANT ").Append(identifier).Append(" TO CURRENT_USER;").Append('\n')
            .Append("        EXCEPTION WHEN unique_violation THEN").Append('\n')
            .Append("            NULL; -- granted by a script that ran at the same time").Append('\n')
            .Append("        END;").Append('\n')
            .Append("    END IF;").Append('\n')
            .ToString();

    /// <summary>
    /// <paramref name="sql"/>, which holds <paramref name="blocks"/> blocks quoted with <c>$ddd$</c>, refused
    /// when a name written inside one spells that tag. Postgres, and the Supabase CLI as it splits a file into
    /// statements, look for nothing but the tag inside a block, so such a name would end it early and the
    /// rest of the name would run as statements of their own.
    /// </summary>
    /// <exception cref="InvalidOperationException">A name spells the tag.</exception>
    private static string Blocks(int blocks, string sql)
    {
        var tags = (sql.Length - sql.Replace(BlockTag, "", StringComparison.Ordinal).Length) / BlockTag.Length;
        return tags == 2 * blocks
            ? sql
            : throw new InvalidOperationException(
                $"A table, schema, function or role name has '{BlockTag}' in it, which would end the block of the script it is written into. Give it another name.");
    }

    /// <summary>The rules that guard an aggregate this context maps.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="rules"/> is null.</exception>
    public static IReadOnlyList<RowAccessRule> RulesOf(DbContext context, IEnumerable<RowAccessRule> rules)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rules);

        var mapped = context.Model.GetEntityTypes().Select(entity => entity.ClrType.FullName).ToHashSet(StringComparer.Ordinal);
        return [.. rules.Where(rule => mapped.Contains(rule.AggregateTypeName))];
    }

    /// <summary>
    /// The access functions about an aggregate this context maps, which this context writes: however many
    /// modules see the class that declares one, only the one whose tables it reads creates it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="functions"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Two access functions have one name.</exception>
    public static IReadOnlyList<RowAccessFunction> FunctionsOf(DbContext context, IEnumerable<RowAccessFunction> functions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(functions);

        var all = functions.ToList();
        if (all.GroupBy(function => function.LogicalName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1) is { } twice)
        {
            throw new InvalidOperationException(
                $"Two access functions are called {twice.Key}: {string.Join(" and ", twice.Select(function => function.AggregateTypeName))}. A function has one definition; give one of them another name.");
        }

        var mapped = context.Model.GetEntityTypes().Select(entity => entity.ClrType.FullName).ToHashSet(StringComparer.Ordinal);
        return [.. all.Where(function => mapped.Contains(function.AggregateTypeName))];
    }

    /// <summary>
    /// Where the functions of <paramref name="contexts"/> live in the database, by the name rules ask them by:
    /// an access function named with its schema is itself, and one with a logical name, <c>owner/name</c>, is
    /// that name in the schema of the context that maps its aggregate, its default schema or else its table's.
    /// A function an <see cref="IRowAccessContribution"/> of <paramref name="export"/> writes is
    /// <c>owner/name</c>, in the default schema of the context it answered for. Hand the answer to
    /// <see cref="RowAccessExport.FunctionNames"/> to write a script of one context whose rules ask the
    /// functions of another.
    /// </summary>
    /// <param name="contexts">The contexts, typically one per module.</param>
    /// <param name="functions">Every access function; each is defined by the contexts that map its aggregate.</param>
    /// <param name="export">The contributions to ask as well; none when left out.</param>
    /// <returns>Each logical name, and the name its function has in the database, <c>schema.name</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="contexts"/> or <paramref name="functions"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Two functions have one name, or one logical name would be two functions, in two contexts with
    /// different schemas.
    /// </exception>
    public static IReadOnlyDictionary<string, string> FunctionNamesOf(IEnumerable<DbContext> contexts, IEnumerable<RowAccessFunction> functions, RowAccessExport? export = null)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(functions);

        export ??= new RowAccessExport();
        var all = functions.ToList();
        return NamesAcross([.. contexts.Select(context => Prepare(context, [], FunctionsOf(context, all), export))], new Dictionary<string, string>());
    }

    /// <summary>
    /// <paramref name="known"/>, and where the functions of every one of <paramref name="prepared"/> live: each
    /// logical name once, whichever context writes it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// One logical name would be two functions, in two contexts with different schemas, or two would be one
    /// function, in one schema.
    /// </exception>
    private static Dictionary<string, string> NamesAcross(IReadOnlyList<Prepared> prepared, IReadOnlyDictionary<string, string> known)
    {
        var names = new Dictionary<string, string>(known, StringComparer.OrdinalIgnoreCase);
        var definers = new Dictionary<string, (string Context, string About)>(StringComparer.OrdinalIgnoreCase);
        foreach (var each in prepared)
        {
            foreach (var definition in each.Names)
            {
                if (definers.TryGetValue(definition.Logical, out var other)
                    && !string.Equals(names[definition.Logical], definition.Resolved, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(ResourceAccessAnswer.IsName(definition.Logical)
                        ? $"Two contexts answer {ResourceAccessAnswer.Described(definition.Logical)}: {other.Context} with {names[definition.Logical]} and {each.Context.GetType().Name} with {definition.Resolved}. One function answers a set for a resource, so a rule that asks it is written with that one: write the resource's access for the context that keeps the resource."
                        : $"The function {definition.Logical} would be {names[definition.Logical]} for {other.Context} and {definition.Resolved} for {each.Context.GetType().Name}, which both write it ({other.About}). A function has one definition: write it in one of them, or name it with its schema.");
                }

                names[definition.Logical] = definition.Resolved;
                definers[definition.Logical] = (each.Context.GetType().Name, definition.About);
            }
        }

        EnsureOneFunctionPerName(names, definers);
        return names;
    }

    /// <summary><paramref name="names"/>, with the context's own functions where they are, which take precedence.</summary>
    /// <exception cref="InvalidOperationException">A function of the context would be one that <paramref name="names"/> gives another logical name.</exception>
    private static Dictionary<string, string> OwnFirst(Prepared prepared, IReadOnlyDictionary<string, string> names)
    {
        var all = new Dictionary<string, string>(names, StringComparer.OrdinalIgnoreCase);
        var definers = new Dictionary<string, (string Context, string About)>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in prepared.Names)
        {
            all[definition.Logical] = definition.Resolved;
            definers[definition.Logical] = (prepared.Context.GetType().Name, definition.About);
        }

        EnsureOneFunctionPerName(all, definers);
        return all;
    }

    /// <summary>
    /// Refuses two logical names that would be one function in the database, <c>schema.name</c>, whether one
    /// context writes both or two contexts one each: the file written later would replace the other's
    /// function, and every policy that asks the first would silently ask the second's question.
    /// </summary>
    /// <param name="names">Each logical name and the name its function has in the database.</param>
    /// <param name="definers">The context that writes each, and what it is, for the logical names a context of the script defines.</param>
    /// <exception cref="InvalidOperationException">Two would be one.</exception>
    private static void EnsureOneFunctionPerName(IReadOnlyDictionary<string, string> names, IReadOnlyDictionary<string, (string Context, string About)> definers)
    {
        var byResolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // A resource access set is another name for the function a contribution says answers it, never a function of its own.
        foreach (var (logical, resolved) in names.Where(pair => !ResourceAccessAnswer.IsName(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (byResolved.TryGetValue(resolved, out var other))
            {
                throw new InvalidOperationException(
                    $"The functions {Described(other)} and {Described(logical)} would both be {resolved}. A function has one definition: give one of them another name, or give its context a schema of its own.");
            }

            byResolved[resolved] = logical;
        }

        string Described(string logical)
            => definers.TryGetValue(logical, out var definer)
                ? $"{logical} ({definer.About}, written by {definer.Context})"
                : $"{logical} (named in RowAccessExport.FunctionNames)";
    }

    /// <summary>
    /// The name <paramref name="function"/> has in the database when <paramref name="context"/> writes it:
    /// its own for a name with its schema, and otherwise its name in the schema of the context, the model's
    /// default schema or else that of the aggregate's table.
    /// </summary>
    /// <exception cref="InvalidOperationException">That schema is not a name Postgres keeps as a script writes it.</exception>
    private static string ResolvedName(DbContext context, RowAccessFunction function)
    {
        if (function.IsQualified)
        {
            return function.LogicalName;
        }

        var schema = context.Model.GetDefaultSchema() ?? TableOf(RootOf(context, function.AggregateTypeName)).Schema ?? DefaultSchema;
        return FunctionSchema(context, schema, function.LogicalName, qualifiable: true) + "." + RowAccessNames.Unqualified(function.LogicalName);
    }

    /// <summary>
    /// <paramref name="schema"/>, the schema of <paramref name="context"/> that a function a script names by
    /// its logical name is created in, refused unless Postgres keeps it as a script writes it: a script writes
    /// a function's name without quotes, which Postgres folds to lower case, while Entity Framework quotes the
    /// schema it creates. A model's <c>"Desk"</c> would then be one schema and the functions' <c>Desk.f()</c>
    /// another, <c>desk</c>.
    /// </summary>
    /// <param name="context">The context whose schema it is.</param>
    /// <param name="schema">The schema.</param>
    /// <param name="logical">The logical name of the function, for the message.</param>
    /// <param name="qualifiable">Whether the function could be named with a schema of its own instead, as an access function can.</param>
    /// <exception cref="InvalidOperationException">It is not lower case letters, digits and underscores.</exception>
    private static string FunctionSchema(DbContext context, string schema, string logical, bool qualifiable = false)
        => schema.Length > 0
           && schema.Length <= MaxIdentifierBytes
           && (schema[0] is (>= 'a' and <= 'z') or '_')
           && schema.All(static character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
            ? schema
            : throw new InvalidOperationException(
                $"The function {logical} would be created in the schema '{schema}' of {context.GetType().Name}, and a script writes a function's name without quotes, so Postgres would read it as a function of another schema, or of none. Give the schema a name of lower case letters, digits and underscores{(qualifiable ? ", or name the function with its schema" : "")}.");

    /// <summary>
    /// A statement that drops every policy made from a rule on this context's tables, and every trigger made from
    /// a column rule. The Supabase export puts it at the start of every migration of a module with rules too,
    /// because a policy stands in the way of dropping a column it reads or changing its type, and a column rule's
    /// trigger in the way of a column it holds; the script after it makes them again. Empty for a context
    /// without tables.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public static string DropStatement(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tables = TablesOf(context)
            .Select(table => $"({Literal(table.Schema ?? DefaultSchema)}, {Literal(table.Name)})")
            .ToList();

        if (tables.Count == 0)
        {
            return string.Empty;
        }

        return Blocks(1, new StringBuilder()
            .Append("DO $ddd$").Append('\n')
            .Append("DECLARE").Append('\n')
            .Append("    generated record;").Append('\n')
            .Append("BEGIN").Append('\n')
            .Append("    FOR generated IN").Append('\n')
            .Append("        SELECT p.polname, n.nspname, c.relname").Append('\n')
            .Append("        FROM pg_catalog.pg_policy p").Append('\n')
            .Append("        JOIN pg_catalog.pg_class c ON c.oid = p.polrelid").Append('\n')
            .Append("        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace").Append('\n')
            .Append("        JOIN pg_catalog.pg_description d ON d.objoid = p.oid AND d.classoid = 'pg_catalog.pg_policy'::regclass").Append('\n')
            .Append("        WHERE d.description = ").Append(Literal(PolicyComment)).Append('\n')
            .Append("          AND (n.nspname, c.relname) IN (").Append(string.Join(", ", tables)).Append(')').Append('\n')
            .Append("    LOOP").Append('\n')
            .Append("        EXECUTE format('DROP POLICY %I ON %I.%I', generated.polname, generated.nspname, generated.relname);").Append('\n')
            .Append("    END LOOP;").Append('\n')
            .Append("    -- The triggers of the column rules, which stand in the way of a column as a policy does.").Append('\n')
            .Append("    FOR generated IN").Append('\n')
            .Append("        SELECT t.tgname, n.nspname, c.relname").Append('\n')
            .Append("        FROM pg_catalog.pg_trigger t").Append('\n')
            .Append("        JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid").Append('\n')
            .Append("        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace").Append('\n')
            .Append("        JOIN pg_catalog.pg_description d ON d.objoid = t.oid AND d.classoid = 'pg_catalog.pg_trigger'::regclass").Append('\n')
            .Append("        WHERE d.description = ").Append(Literal(ColumnRuleComment)).Append('\n')
            .Append("          AND (n.nspname, c.relname) IN (").Append(string.Join(", ", tables)).Append(')').Append('\n')
            .Append("    LOOP").Append('\n')
            .Append("        EXECUTE format('DROP TRIGGER %I ON %I.%I', generated.tgname, generated.nspname, generated.relname);").Append('\n')
            .Append("    END LOOP;").Append('\n')
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .ToString());
    }

    /// <summary>
    /// The statements that make this context's access functions, every rule's policies, and the policies of
    /// the aggregates' entities, which follow their root: read with the row they belong to, and written
    /// under the root's write rules. <paramref name="rules"/> and <paramref name="accessFunctions"/> are those
    /// of aggregates this context maps; see <see cref="RulesOf"/> and <see cref="FunctionsOf"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="rules"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A rule names a role no policy can be for, such as <c>PUBLIC</c>.</exception>
    public static string CreateStatements(
        DbContext context,
        IReadOnlyList<RowAccessRule> rules,
        IReadOnlyList<RowAccessFunction>? accessFunctions = null,
        PostgresCallerFunctions? callerFunctions = null)
        => CreateStatements(context, rules, accessFunctions, new RowAccessExport { CallerFunctions = callerFunctions ?? PostgresCallerFunctions.Toolkit });

    /// <summary>
    /// The statements that make this context's access functions, every rule's policies, and the policies of
    /// the aggregates' entities, written with <paramref name="export"/>'s caller functions and roles, and what
    /// its contributions write for the context. <paramref name="rules"/> and <paramref name="accessFunctions"/>
    /// are those of aggregates this context maps; see <see cref="RulesOf"/> and <see cref="FunctionsOf"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A prelude that can run again comes first, with what the policies ask and the roles they are for.
    /// Then the functions, the access functions and the contributed ones, each after the functions it asks,
    /// because Postgres checks a function's body when it creates it, and all of them before the policies,
    /// which Postgres refuses while a function they call does not exist. A function this context made before
    /// and no longer writes is dropped once nothing asks it; one it still writes is replaced in place, so the
    /// policies of other modules that call it keep working. Each is revoked from <c>PUBLIC</c>, every other
    /// grant on it but its owner's is taken back, and it is granted to the roles that ask it: an access
    /// function to the roles of this context's policies that ask it, a contributed function to its
    /// <see cref="ContributedFunction.GrantTo"/>.
    /// </para>
    /// <para>
    /// Every policy is for one role, and a table gets at most one permissive policy per command and role: the
    /// rules, and the contributions' permissive policies, that allow a command to a role are asked in one
    /// policy, whose condition is theirs OR-ed together. That is what Postgres does with several permissive
    /// policies anyway, so nothing is allowed that they on their own would not allow, and each policy says,
    /// in one place, who may do what. A restrictive policy is never merged. The contributions' own
    /// statements come last.
    /// </para>
    /// <para>
    /// The types of a hierarchy mapped to one table share its policies, and those of its entities' tables.
    /// A rule about a type derived in it is about that type's rows only, which the table's discriminator
    /// tells apart.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="context"/>, <paramref name="rules"/> or <paramref name="export"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// A rule names a role no policy can be for, such as <c>PUBLIC</c>, or a function nothing defines, the
    /// export's scoped system role is the user's or the anonymous caller's role as well, a policy asks a
    /// contributed function for a role it is not granted to, two functions or two policies of a table would
    /// have one name, the functions ask each other in a circle, or a contribution writes what it may not.
    /// </exception>
    public static string CreateStatements(
        DbContext context,
        IReadOnlyList<RowAccessRule> rules,
        IReadOnlyList<RowAccessFunction>? accessFunctions,
        RowAccessExport export)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(export);
        export.Roles.EnsureOwnRoles();

        var prepared = Prepare(context, rules, accessFunctions ?? [], export);
        var names = OwnFirst(prepared, export.FunctionNames);
        return Emit(prepared, names, Know([prepared], names), export);
    }

    /// <summary>
    /// The statements <see cref="CreateStatements(DbContext, IReadOnlyList{RowAccessRule}, IReadOnlyList{RowAccessFunction}?, RowAccessExport)"/>
    /// writes for <paramref name="prepared"/>, knowing where every function lives and who asks it.
    /// </summary>
    private static string Emit(Prepared prepared, IReadOnlyDictionary<string, string> names, Knowledge knowledge, RowAccessExport export)
    {
        var context = prepared.Context;
        var sql = context.GetService<ISqlGenerationHelper>();
        var writing = new Writing(export.CallerFunctions, names, sql, export.Roles, export.ForceRowLevelSecurity);
        var definitions = InDependencyOrder(context, Definitions(prepared, writing, knowledge));
        var policies = ContributedPolicies(prepared, writing);

        // The column rules' triggers, worked out before anything is written, so their functions are kept.
        var columnTriggers = ColumnTriggers(prepared, policies, writing);

        // What the permissive policies allow, collected as they are written, where the script writes privileges too.
        var privileges = export.WriteGrants ? new Privileges() : null;

        var named = prepared.Roles.Values.SelectMany(roles => roles)
            .Concat(policies.Select(policy => policy.Role))
            .Concat(definitions.SelectMany(definition => definition.GrantTo));
        List<string> kept = [.. definitions.Select(definition => definition.Resolved), .. columnTriggers.Select(trigger => trigger.Function)];

        // Whether the script is about the bookkeeping role: it writes privileges, or something of it is for the
        // role by its symbol. A role a host names as the database spells it is the host's own to make, also
        // where that name is the one its system caller runs as, a role that bypasses the policies for one.
        var bookkeeping = export.WriteGrants
            || prepared.Rules.Any(rule => rule.Roles.Contains(RowAccessRoles.System, StringComparer.Ordinal))
            || prepared.Contributions.Any(contributed =>
                contributed.Result.Policies.Any(policy => string.Equals(policy.Role, RowAccessRoles.System, StringComparison.Ordinal))
                || contributed.Result.Functions.Any(function => (function.GrantTo ?? []).Contains(RowAccessRoles.System, StringComparer.Ordinal)));

        var statements = new StringBuilder()
            .Append('\n')
            .Append(PreludeStatement(named, export.Roles, sql, export.WriteGrants, bookkeeping))
            .Append(DropFunctionsStatement(context, kept));

        foreach (var definition in definitions)
        {
            statements.Append(definition.Statement).Append("REVOKE ALL ON FUNCTION ").Append(definition.Signature).Append(" FROM PUBLIC;\n");
        }

        statements.Append(GrantStatements(context, definitions, kept, sql));

        // A column rule is no policy: its trigger is written after them.
        var aggregates = prepared.Rules
            .Where(rule => !rule.IsColumnRule)
            .GroupBy(rule => rule.AggregateTypeName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new AggregateRules(RootOf(context, group.Key), [.. group.Distinct().OrderBy(rule => rule.Name, StringComparer.Ordinal)]));

        // The contributions' permissive policies, by table, command and role, to merge with the rules'; each
        // taken out as it is written.
        var permissive = policies.Where(policy => !policy.Restrictive)
            .GroupBy(policy => (policy.Table, policy.Command, policy.Role))
            .ToDictionary(group => group.Key, group => group.ToList());
        var secured = new HashSet<StoreObjectIdentifier>();
        var roots = new HashSet<StoreObjectIdentifier>();
        var policyNames = new PolicyNames();

        // The tables a contribution keeps to itself. A rule reaches one only where it takes the place of a default
        // read on its aggregate's own table (Prepare refuses the rest), so it adds nothing to those of its entities.
        var keptTables = prepared.Contributions.SelectMany(contributed => contributed.Result.ExclusiveTables ?? []).Select(TableOf).ToHashSet();

        // Aggregates that share a table, a hierarchy mapped to one, share its policies too, and those of the
        // tables of their entities, so a command and a role still get one policy on each.
        foreach (var onTable in aggregates.GroupBy(aggregate => TableOf(aggregate.Root)))
        {
            var table = onTable.Key;
            var qualified = sql.DelimitIdentifier(table.Name, table.Schema ?? DefaultSchema);
            List<AggregateRules> sharing = [.. onTable];

            statements.Append('\n').Append(EnableStatement(qualified, writing.Force));
            secured.Add(table);
            roots.Add(table);
            statements.Append(RootStatements(table, qualified, sharing, prepared.Roles, writing, permissive, policyNames, privileges));
            statements.Append(EntityStatements(table, sharing, prepared.Roles, writing, permissive, secured, keptTables, policyNames, privileges));
        }

        statements.Append(ContributedPolicyStatements(prepared, policies, permissive, secured, writing, policyNames, privileges));

        // Only for a context with column rules, so a script of a context without is as it was.
        statements.Append(ColumnTriggerStatements(columnTriggers, writing));

        // Before the contributions' own statements, so a contribution may give what the policies cannot say.
        if (privileges is not null)
        {
            statements.Append(PrivilegeStatements(prepared, secured, roots, privileges, export.Roles, sql));
        }

        statements.Append(ContributedStatements(prepared, writing));

        // Last, and only for a context that maps such a table, so a script of a context without is as it always was.
        statements.Append(KeptRowsStatements(context, sql));

        return statements.ToString();
    }

    /// <summary>
    /// A rule's condition about a row of its aggregate's table, the policy's own row where
    /// <paramref name="rootAlias"/> is <see langword="null"/>, and <paramref name="rootAlias"/> otherwise.
    /// </summary>
    private static string Condition(IEntityType root, StoreObjectIdentifier table, RowAccessRule rule, Writing writing, string? rootAlias)
        => OfItsType(root, table, new Filler($"the rule '{rule.Name}'", root, table, writing, rootAlias).Fill(rule.Sql), rootAlias);

    /// <summary>
    /// What every piece of a script is written with: how a policy asks about the caller, where each function a
    /// rule asks by its logical name lives, how Postgres quotes a name, and whether row level security is
    /// forced where it is turned on.
    /// </summary>
    private sealed record Writing(PostgresCallerFunctions Callers, IReadOnlyDictionary<string, string> Names, ISqlGenerationHelper Sql, RowAccessRoleNames Roles, bool Force);

    /// <summary>
    /// <paramref name="condition"/>, about a row of <paramref name="root"/>'s table, narrowed to the rows of
    /// <paramref name="root"/>'s own type where the table holds a whole hierarchy: a rule about priority
    /// orders says nothing about the other orders in their table. The type a hierarchy starts from, and a
    /// type with a table of its own, is about every row of its table as it is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model gives the shared table no discriminator.</exception>
    private static string OfItsType(IEntityType root, StoreObjectIdentifier table, string condition, string? alias)
        => TypeCondition(root, table, alias) is { } ofType ? $"({Parenthesized(condition)} AND {ofType})" : condition;

    /// <summary>
    /// Whether a row of <paramref name="root"/>'s table, <paramref name="alias"/> or the policy's own, is of
    /// <paramref name="root"/>'s type or one derived from it, as the table's discriminator says; null where the
    /// table holds no other type's rows, and every row is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model gives the shared table no discriminator.</exception>
    private static string? TypeCondition(IEntityType root, StoreObjectIdentifier table, string? alias)
    {
        if (root.BaseType is not { } baseType || TableOf(baseType) != table)
        {
            return null;
        }

        var discriminator = root.FindDiscriminatorProperty()
            ?? throw new InvalidOperationException(
                $"{root.ClrType.Name} shares {table.DisplayName()} with {baseType.ClrType.Name}, and the Entity Framework model gives the table no discriminator to tell their rows apart, so a rule about {root.ClrType.Name} would be about every row there.");

        var mapping = discriminator.GetRelationalTypeMapping();
        var values = root.GetDerivedTypesInclusive()
            .Where(type => !type.IsAbstract())
            .Select(type => mapping.GenerateSqlLiteral(type.GetDiscriminatorValue()))
            .ToList();
        var column = (alias is null ? "" : alias + ".") + Quote(discriminator.GetColumnName(table)!);

        return values.Count switch
        {
            0 => "FALSE",
            1 => $"{column} = {values[0]}",
            _ => $"{column} IN ({string.Join(", ", values)})",
        };
    }

    /// <summary>An aggregate root and the rules that guard it, by name.</summary>
    private sealed record AggregateRules(IEntityType Root, IReadOnlyList<RowAccessRule> Rules);

    /// <summary>
    /// The policies of an aggregate's own table: one per command and role that a rule grants, named after
    /// the rule, <c>"Owners have their tickets (select) for anon"</c>, or, where several rules grant the
    /// same command to the same role, one that asks them all, named after the table,
    /// <c>"Tickets (select) for anon"</c>, with a comment above it that names the rules. A contribution's
    /// permissive policy for the same table, command and role is asked in the same policy.
    /// </summary>
    private static string RootStatements(
        StoreObjectIdentifier table,
        string qualified,
        IReadOnlyList<AggregateRules> aggregates,
        IReadOnlyDictionary<RowAccessRule, IReadOnlyList<string>> roles,
        Writing writing,
        Dictionary<(StoreObjectIdentifier Table, string Command, string Role), List<Policy>> contributed,
        PolicyNames policyNames,
        Privileges? privileges)
    {
        var sql = writing.Sql;
        var conditions = aggregates
            .SelectMany(aggregate => aggregate.Rules.Select(rule => (Rule: rule, Condition: Condition(aggregate.Root, table, rule, writing, rootAlias: null))))
            .OrderBy(each => each.Rule.Name, StringComparer.Ordinal)
            .ToList();
        var named = conditions.SelectMany(each => roles[each.Rule])
            .Concat(contributed.Keys.Where(key => key.Table == table).Select(key => key.Role))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var statements = new StringBuilder();
        foreach (var (command, operation) in RootCommands)
        {
            foreach (var role in named)
            {
                var granting = conditions.Where(each => each.Rule.Operations.HasFlag(operation) && roles[each.Rule].Contains(role, StringComparer.Ordinal)).ToList();
                var added = Take(contributed, table, command, role);
                if (granting.Count + added.Count == 0)
                {
                    // Nothing grants this role this command: without a policy, Postgres refuses it.
                    continue;
                }

                var of = $" ({command.ToLowerInvariant()}) for {role}";
                var name = policyNames.Claim(
                    table,
                    PolicyName(granting.Count + added.Count > 1 ? table.Name + of : granting.Count == 1 ? granting[0].Rule.Name + of : added[0].Declared.Name + of),
                    $"the permissive policy for {command} to {role}");
                privileges?.Allow(table, command, role);

                // A contributed default the rules take the place of: they are held to what it holds a rule to, and
                // what it keeps stays beside them.
                List<Policy> replaced = granting.Count > 0 ? [.. added.Where(policy => policy.Default is not null)] : [];
                if (replaced.Count > 0)
                {
                    statements.Append('\n').Append(Replacing(name, granting.Select(each => each.Rule), replaced, [.. added.Except(replaced)], roles, writing.Roles)).Append('\n');
                    var held = AllOf([.. replaced.Select(policy => policy.Default!.Within), AnyOf(granting.Select(each => each.Condition))])!;
                    var reads = AnyOf([.. replaced.Select(policy => policy.Default!.Kept), held, .. added.Except(replaced).Select(policy => policy.Using)])!;
                    statements
                        .Append("CREATE POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(qualified)
                        .Append(" FOR ").Append(command).Append(" TO ").Append(sql.DelimitIdentifier(role)).Append('\n')
                        .Append(Clauses(command, reads, null)).Append(";\n")
                        .Append("COMMENT ON POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(qualified)
                        .Append(" IS ").Append(Literal(PolicyComment)).Append(";\n");
                    continue;
                }

                statements.Append('\n');
                if (added.Count > 0)
                {
                    statements.Append("-- ").Append(Commented(name)).Append(" asks ")
                        .Append(Listed([.. granting.Select(each => "the rule '" + Commented(each.Rule.Name) + "'"), .. added.Select(Described)]))
                        .Append(granting.Count + added.Count > 1 ? ": a row one of them allows is allowed." : ".").Append('\n');
                }
                else if (granting.Count > 1)
                {
                    statements
                        .Append("-- ").Append(Commented(name)).Append(" asks the rules ")
                        .Append(Listed(granting.Select(each => "'" + Commented(each.Rule.Name) + "'")))
                        .Append(": a row one of them allows is allowed.").Append('\n');
                }

                var usings = granting.Select(each => each.Condition).Concat(added.Select(policy => policy.Using));
                var checks = granting.Select(each => each.Condition).Concat(added.Select(policy => policy.Check));
                statements
                    .Append("CREATE POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(qualified)
                    .Append(" FOR ").Append(command).Append(" TO ").Append(sql.DelimitIdentifier(role)).Append('\n')
                    .Append(Clauses(command, AnyOf(usings), AnyOf(checks))).Append(";\n")
                    .Append("COMMENT ON POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(qualified)
                    .Append(" IS ").Append(Literal(PolicyComment)).Append(";\n");
            }
        }

        return statements.ToString();
    }

    /// <summary>The contributions' permissive policies for <paramref name="table"/>, <paramref name="command"/> and <paramref name="role"/>, taken out of <paramref name="contributed"/>.</summary>
    private static List<Policy> Take(Dictionary<(StoreObjectIdentifier Table, string Command, string Role), List<Policy>> contributed, StoreObjectIdentifier table, string command, string role)
        => contributed.Remove((table, command, role), out var policies) ? policies : [];

    /// <summary>
    /// The database roles a rule's policies are for, one policy each: those its <c>To</c> names, through
    /// <paramref name="names"/>, or the user's and the anonymous caller's when it names none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The rule names a role no policy can be for.</exception>
    private static IReadOnlyList<string> RolesOf(RowAccessRule rule, RowAccessRoleNames names)
    {
        IReadOnlyList<string> named = rule.Roles.Count == 0 ? [RowAccessRoles.User, RowAccessRoles.Anonymous] : rule.Roles;
        var resolved = new List<string>(named.Count);

        foreach (var role in named)
        {
            if (!names.TryResolve(role, out var name, out var problem))
            {
                throw new InvalidOperationException($"The rule '{rule.Name}' is for '{role}', which no policy can be for. {problem}");
            }

            resolved.Add(name);
        }

        return [.. resolved.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary><c>'a'</c>, <c>'a' and 'b'</c>, <c>'a', 'b' and 'c'</c>.</summary>
    private static string Listed(IEnumerable<string> items)
    {
        var all = items.ToList();
        return all.Count < 2 ? string.Concat(all) : string.Join(", ", all.Take(all.Count - 1)) + " and " + all[^1];
    }

    /// <summary>
    /// What makes the policies of
    /// <see cref="CreateStatements(DbContext, IReadOnlyList{RowAccessRule}, IReadOnlyList{RowAccessFunction}?, RowAccessExport)"/>
    /// work, whatever the database already has: the
    /// toolkit's schema and <c>ddd.written_in_this_transaction</c>, which the policies of an aggregate's
    /// entities ask, the scoped system role when a policy is for it, and the right to ask them for every
    /// role a policy names. It comes right after the drop in every script and every access file, so none
    /// depends on another having run. It makes <c>ddd.use_caller</c> as well, which the policies do not ask but
    /// the interceptor calls where the caller's settings last one transaction, so a database that has the
    /// policies can be reached through a transaction pooler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each object is made, replaced or granted only when it is missing or differs. Postgres checks the
    /// right to create a schema before it looks whether the schema exists, and only a function's owner may
    /// replace it, so the plain <c>IF NOT EXISTS</c> and <c>OR REPLACE</c> forms would refuse a script run by
    /// the application's own role after somebody else ran <see cref="SetupScript"/>.
    /// </para>
    /// <para>
    /// <c>ddd.use_caller</c> is taken from <c>PUBLIC</c>, which Postgres gives every new routine to, and stays
    /// with its owner, the role that ran the script that made it. A login role of its own gets it from two
    /// statements of the host's: <c>GRANT USAGE ON SCHEMA ddd TO</c> that role, and
    /// <c>GRANT EXECUTE ON PROCEDURE ddd.use_caller(text, text, text[], text[]) TO</c> it.
    /// </para>
    /// <para>
    /// The scoped system role is made without a login and without <c>BYPASSRLS</c>, and the script fails
    /// when a role of that name exists that has either, is a superuser or has the privileges of a role that
    /// owns tables, since its policies would then hold nobody back, or when the user's or the anonymous
    /// caller's role, or the role of a mapped token role, has its privileges, since its policies would then be
    /// theirs. It is granted to the role running the script, unless that role may switch to it already: since
    /// Postgres 16 the role that creates a role may administer it, but not switch to it until it grants itself
    /// the role.
    /// </para>
    /// <para>
    /// The role of a mapped token role that a policy names is made, checked and granted the same way, so a
    /// script whose rules name <c>RowAccessRoles.Token("analyst")</c> runs on a database that never saw
    /// <see cref="SetupScript"/>. It is checked in the other direction as well: the script fails when the mapped
    /// role has the privileges of the user's or the anonymous caller's role, since the holder of its token would
    /// then get what was written for those callers too. A token role mapped to the user's own role is that role,
    /// which the host makes.
    /// </para>
    /// <para>
    /// The bookkeeping role, <see cref="RowAccessRoleNames.System"/>, is made, checked and granted the same way
    /// where the script names it: because a rule or a contribution is for <c>RowAccessRoles.System</c>, or
    /// because the script writes privileges. A rule or a grant that spells that role's name out, instead of
    /// naming it by its symbol, is for a role of the host's own, as any other spelled-out name is: the script
    /// neither makes nor checks it, so a host whose system caller bypasses the policies gets the script it
    /// always got. A script that writes privileges takes them back from every role a caller could run as, so
    /// it makes the scoped system role and the roles of the mapped token roles as well, whether a policy names
    /// them or not: Postgres refuses to revoke from a role that does not exist.
    /// </para>
    /// </remarks>
    /// <param name="roles">The roles the script's policies and function grants name.</param>
    /// <param name="names">The roles of the script.</param>
    /// <param name="sql">How Postgres quotes a name.</param>
    /// <param name="grants">Whether the script writes privileges too.</param>
    /// <param name="bookkeeping">Whether the script is about the bookkeeping role: it writes privileges, or a rule or a contribution names the role by its symbol.</param>
    /// <exception cref="InvalidOperationException">The script names the bookkeeping role, and that is a role callers run as.</exception>
    private static string PreludeStatement(IEnumerable<string> roles, RowAccessRoleNames names, ISqlGenerationHelper sql, bool grants, bool bookkeeping)
    {
        var named = roles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var prelude = new StringBuilder()
            .Append("-- What the policies below ask: the ddd schema, and the function that tells a row written by the running").Append('\n')
            .Append("-- transaction, savepoints included, from one written before it began. With them, the procedure that sets a").Append('\n')
            .Append("-- caller for one transaction, which only the role the application logs in as may call. Each is made, replaced").Append('\n')
            .Append("-- or granted only when it is missing or differs, so a role that does not own them may run this as well.").Append('\n')
            .Append("DO $ddd$").Append('\n')
            .Append("DECLARE").Append('\n')
            .Append("    body constant text := $function$").Append(WrittenInThisTransactionBody).Append("$function$;").Append('\n')
            .Append("    use_caller constant text := $procedure$").Append(UseCallerBody).Append("$procedure$;").Append('\n')
            .Append("BEGIN").Append('\n')
            .Append("    IF pg_catalog.to_regnamespace('ddd') IS NULL THEN").Append('\n')
            .Append("        CREATE SCHEMA ddd;").Append('\n')
            .Append("    END IF;").Append('\n')
            .Append("    IF NOT EXISTS (SELECT FROM pg_catalog.pg_proc").Append('\n')
            .Append("                   WHERE oid = pg_catalog.to_regprocedure(").Append(Literal(WrittenInThisTransactionSignature)).Append(") AND prosrc = body) THEN").Append('\n')
            .Append("        EXECUTE ").Append(Literal(WrittenInThisTransactionHeader)).Append(" || pg_catalog.quote_literal(body);").Append('\n')
            .Append("    END IF;").Append('\n')
            .Append("    IF NOT EXISTS (SELECT FROM pg_catalog.pg_proc").Append('\n')
            .Append("                   WHERE oid = pg_catalog.to_regprocedure(").Append(Literal(UseCallerSignature)).Append(") AND prosrc = use_caller) THEN").Append('\n')
            .Append("        EXECUTE ").Append(Literal(UseCallerHeader)).Append(" || pg_catalog.quote_literal(use_caller);").Append('\n')
            .Append("    END IF;").Append('\n')
            .Append("    IF pg_catalog.has_function_privilege('public', ").Append(Literal(UseCallerSignature)).Append(", 'EXECUTE') THEN").Append('\n')
            .Append("        REVOKE ALL ON PROCEDURE ").Append(UseCallerSignature).Append(" FROM PUBLIC;").Append('\n')
            .Append("    END IF;").Append('\n');

        var tokenRoles = names.TokenDatabaseRoles;
        foreach (var tokenRole in tokenRoles.Where(role => grants || named.Contains(role, StringComparer.Ordinal)))
        {
            // The role the holders of a token role run as, held to the policies written for it as the scoped
            // system role is, and kept from having a signed-in user's or an anonymous caller's privileges besides.
            // Before that role, so one that is a superuser is refused for what it is.
            var identifier = sql.DelimitIdentifier(tokenRole);
            prelude
                .Append(RoleStatement(tokenRole, identifier))
                .Append(ConfinedRoleCheck(tokenRole, [names.User, names.Anonymous], bothWays: true))
                .Append(GrantToTheRunningRole(tokenRole, identifier));
        }

        if (grants || named.Contains(names.SystemIn, StringComparer.Ordinal))
        {
            // The role meant for the application's own work inside the policies, which may neither log in
            // nor bypass them, and which the role running this may switch to.
            var systemIn = sql.DelimitIdentifier(names.SystemIn);
            prelude
                .Append(RoleStatement(names.SystemIn, systemIn))
                .Append(ConfinedRoleCheck(names.SystemIn, [names.User, names.Anonymous, .. tokenRoles]))
                .Append(GrantToTheRunningRole(names.SystemIn, systemIn));
        }

        // A bookkeeping role that is another caller's role as well cannot be given the toolkit's tables. Where the
        // script gives nothing, such a name in a policy is that caller's role, and is handled as that above.
        var shared = names.NotABookkeepingRole();
        if (grants && shared is not null)
        {
            throw new InvalidOperationException(shared);
        }

        if (names.System is { } system && shared is null && bookkeeping)
        {
            // The role the application's own bookkeeping runs as, which holds the toolkit's tables and nothing
            // else: it may neither log in nor bypass the policies, and no role a caller runs as may reach it.
            var identifier = sql.DelimitIdentifier(system);
            prelude
                .Append(RoleStatement(system, identifier))
                .Append(ConfinedRoleCheck(system, [names.User, names.Anonymous, names.SystemIn, .. tokenRoles], given: "hold the outbox, the inbox and the migration history"))
                .Append(GrantToTheRunningRole(system, identifier));
        }

        foreach (var role in named)
        {
            // Asked by the name as the GRANT spells it, so both mean the same role.
            var identifier = sql.DelimitIdentifier(role);
            var oid = $"pg_catalog.to_regrole({Literal(identifier)})";
            prelude
                .Append("    IF NOT coalesce(pg_catalog.has_schema_privilege(").Append(oid).Append(", 'ddd', 'USAGE'), false) THEN").Append('\n')
                .Append("        GRANT USAGE ON SCHEMA ddd TO ").Append(identifier).Append(';').Append('\n')
                .Append("    END IF;").Append('\n')
                .Append("    IF NOT coalesce(pg_catalog.has_function_privilege(").Append(oid).Append(", ").Append(Literal(WrittenInThisTransactionSignature)).Append(", 'EXECUTE'), false) THEN").Append('\n')
                .Append("        GRANT EXECUTE ON FUNCTION ").Append(WrittenInThisTransactionSignature).Append(" TO ").Append(identifier).Append(';').Append('\n')
                .Append("    END IF;").Append('\n');
        }

        return Blocks(1, prelude
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .Append('\n')
            .ToString());
    }

    /// <summary>
    /// The policies of the tables of an aggregate's entities, one per command and role that a rule of the
    /// root grants. Reading follows the parent row, whose own policies decide. Writing asks the root's write
    /// rules about the root row, joined up from the entity through every level in between: inserting needs
    /// a Change rule, or a Create rule and a root row this transaction wrote; changing needs a Change rule;
    /// removing, a Change or a Remove rule.
    /// </summary>
    /// <param name="table">The aggregates' own table.</param>
    /// <param name="aggregates">
    /// Every aggregate on <paramref name="table"/>. A type of a hierarchy has the entities of the type it
    /// derives from as well, so a table of entities is written once, with the rules of every type that has it.
    /// </param>
    /// <param name="roles">The roles of each rule's policies.</param>
    /// <param name="writing">What the policies are written with.</param>
    /// <param name="contributed">The contributions' permissive policies, which are merged with those of a table of entities they are on.</param>
    /// <param name="secured">The tables with row level security turned on so far, which this adds the entities' tables to.</param>
    /// <param name="kept">
    /// The tables a contribution keeps to itself, which get nothing here: a rule that reaches the aggregate's own
    /// table where a contribution keeps it takes the place of a default read there, and leaves the tables of the
    /// entities to the contribution's policies.
    /// </param>
    /// <param name="policyNames">The names the script gave its policies so far, per table.</param>
    /// <param name="privileges">What the permissive policies allow, where the script writes privileges too.</param>
    private static string EntityStatements(
        StoreObjectIdentifier table,
        IReadOnlyList<AggregateRules> aggregates,
        IReadOnlyDictionary<RowAccessRule, IReadOnlyList<string>> roles,
        Writing writing,
        Dictionary<(StoreObjectIdentifier Table, string Command, string Role), List<Policy>> contributed,
        HashSet<StoreObjectIdentifier> secured,
        IReadOnlySet<StoreObjectIdentifier> kept,
        PolicyNames policyNames,
        Privileges? privileges)
    {
        var sql = writing.Sql;
        var entities = new List<(StoreObjectIdentifier Table, IReadOnlyList<Link> Chain, List<(RowAccessRule Rule, string Condition)> Reaching)>();
        foreach (var aggregate in aggregates)
        {
            // Each rule's condition about the root row, as the entities' policies ask it.
            List<(RowAccessRule Rule, string Condition)> conditions = [.. aggregate.Rules.Select(rule => (rule, Condition(aggregate.Root, table, rule, writing, RootAlias)))];

            foreach (var (entity, chain, _) in EntitiesOf(aggregate.Root, table, []))
            {
                if (kept.Contains(entity))
                {
                    continue;
                }

                var known = entities.FindIndex(each => each.Table == entity);
                if (known < 0)
                {
                    entities.Add((entity, chain, [.. conditions]));
                }
                else
                {
                    entities[known].Reaching.AddRange(conditions);
                }
            }
        }

        var statements = new StringBuilder();
        foreach (var (entity, chain, reaching) in entities)
        {
            secured.Add(entity);
            var named = reaching.SelectMany(each => roles[each.Rule])
                .Concat(contributed.Keys.Where(key => key.Table == entity).Select(key => key.Role))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var entityTable = sql.DelimitIdentifier(entity.Name, entity.Schema ?? DefaultSchema);
            var parent = chain[0].Parent;
            var follows = $"(EXISTS (SELECT 1 FROM {sql.DelimitIdentifier(parent.Name, parent.Schema ?? DefaultSchema)} parent WHERE "
                + string.Join(" AND ", chain[0].Key.Select(column => $"parent.{sql.DelimitIdentifier(column.Parent)} = {entityTable}.{sql.DelimitIdentifier(column.Child)}"))
                + "))";

            statements.Append('\n')
                .Append("-- ").Append(Commented(entity.Name)).Append(" belongs to the aggregate: it is read with its ").Append(Commented(parent.Name))
                .Append(", and written as the rules let a caller write its ").Append(Commented(table.Name)).Append('.').Append('\n')
                .Append(EnableStatement(entityTable, writing.Force));

            foreach (var command in EntityCommands)
            {
                foreach (var role in named)
                {
                    var granting = reaching.Where(each => roles[each.Rule].Contains(role, StringComparer.Ordinal)).OrderBy(each => each.Rule.Name, StringComparer.Ordinal).ToList();

                    // A rule that allows Change is asked once: what it allows besides adds nothing to an
                    // insert or a delete it already lets through.
                    string? Allowed(RowOperations operation) => AnyOf(granting
                        .Where(each => each.Rule.Operations.HasFlag(operation) && (operation == RowOperations.Change || !each.Rule.Operations.HasFlag(RowOperations.Change)))
                        .Select(each => each.Condition));

                    var condition = command switch
                    {
                        "SELECT" => granting.Any(each => each.Rule.Operations.HasFlag(RowOperations.Read)) ? follows : null,
                        "INSERT" => Written(entity, chain, AnyOf([
                            Allowed(RowOperations.Change),
                            Allowed(RowOperations.Create) is { } create ? $"({create} AND ddd.written_in_this_transaction({RootAlias}.xmin))" : null]), sql),
                        "UPDATE" => Written(entity, chain, Allowed(RowOperations.Change), sql),
                        _ => Written(entity, chain, AnyOf([Allowed(RowOperations.Change), Allowed(RowOperations.Remove)]), sql),
                    };

                    var added = Take(contributed, entity, command, role);
                    if (condition is null && added.Count == 0)
                    {
                        // Nothing grants this role this command: without a policy, Postgres refuses it.
                        continue;
                    }

                    var of = $" ({command.ToLowerInvariant()}) for {role}";
                    var name = policyNames.Claim(
                        entity,
                        PolicyName(condition is null && added.Count == 1 ? added[0].Declared.Name + of : entity.Name + of),
                        $"the permissive policy for {command} to {role}");
                    privileges?.Allow(entity, command, role);
                    if (added.Count > 0)
                    {
                        statements.Append("-- ").Append(Commented(name)).Append(" asks ")
                            .Append(Listed([.. condition is null ? [] : new[] { "the rules of its " + Commented(table.Name) }, .. added.Select(Described)]))
                            .Append(condition is null && added.Count == 1 ? "." : ": a row one of them allows is allowed.").Append('\n');
                    }

                    statements
                        .Append("CREATE POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(entityTable)
                        .Append(" FOR ").Append(command).Append(" TO ").Append(sql.DelimitIdentifier(role)).Append('\n')
                        .Append(Clauses(command, AnyOf([condition, .. added.Select(policy => policy.Using)]), AnyOf([condition, .. added.Select(policy => policy.Check)]))).Append(";\n")
                        .Append("COMMENT ON POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(entityTable)
                        .Append(" IS ").Append(Literal(PolicyComment)).Append(";\n");
                }
            }
        }

        return statements.ToString();
    }

    /// <summary>
    /// <c>EXISTS</c> a root row, <c>r</c>, that the entity's row belongs to, through every level in between,
    /// of which <paramref name="condition"/> is true; <see langword="null"/> when there is no condition.
    /// The root is read under the caller's own policies, so a caller who may not read it writes none of its
    /// entities.
    /// </summary>
    /// <remarks>
    /// Each level is an <c>EXISTS</c> of its own inside the one below it, so the root is alone in the
    /// innermost, where the condition is asked: a column a rule's own SQL names without an alias can only be
    /// the root's, even when a table in between has a column of that name. The entity's own row is named by
    /// its schema and table, which no alias inside can take the place of.
    /// </remarks>
    private static string? Written(StoreObjectIdentifier entity, IReadOnlyList<Link> chain, string? condition, ISqlGenerationHelper sql)
    {
        if (condition is null)
        {
            return null;
        }

        // Each level above the entity an alias, the root r; below the first is the entity's own row.
        var aliases = chain.Select((_, level) => level == chain.Count - 1 ? RootAlias : "p" + (level + 1)).ToList();
        var own = sql.DelimitIdentifier(entity.Name, entity.Schema ?? DefaultSchema);

        var exists = condition;
        for (var level = chain.Count - 1; level >= 0; level--)
        {
            var parent = chain[level].Parent;
            var below = level == 0 ? own : aliases[level - 1];
            var belongs = string.Join(" AND ", chain[level].Key.Select(column => $"{aliases[level]}.{sql.DelimitIdentifier(column.Parent)} = {below}.{sql.DelimitIdentifier(column.Child)}"));

            exists = $"EXISTS (SELECT 1 FROM {sql.DelimitIdentifier(parent.Name, parent.Schema ?? DefaultSchema)} {aliases[level]} WHERE {belongs} AND {exists})";
        }

        return "(" + exists + ")";
    }

    /// <summary>A name as a <c>--</c> comment can carry it: on one line, whatever the model calls it.</summary>
    private static string Commented(string name) => string.Concat(name.Select(character => char.IsControl(character) ? ' ' : character));

    /// <summary>The conditions OR-ed together, each in parentheses; <see langword="null"/> for none.</summary>
    private static string? AnyOf(IEnumerable<string?> conditions)
    {
        var present = conditions.OfType<string>().Select(Parenthesized).ToList();
        return present.Count switch
        {
            0 => null,
            1 => present[0],
            _ => "(" + string.Join(" OR ", present) + ")",
        };
    }

    /// <summary>The conditions AND-ed together, each in parentheses; <see langword="null"/> for none.</summary>
    private static string? AllOf(IEnumerable<string?> conditions)
    {
        var present = conditions.OfType<string>().Select(Parenthesized).ToList();
        return present.Count switch
        {
            0 => null,
            1 => present[0],
            _ => "(" + string.Join(" AND ", present) + ")",
        };
    }

    /// <summary>
    /// The comment above a policy whose <paramref name="rules"/> take the place of contributed defaults: which rules,
    /// in place of which defaults, held to what and beside what, and the contributed policies asked with them. A
    /// rule that names no role and was taken for the roles of the default alone says so, since its policy for the
    /// anonymous caller is left out.
    /// </summary>
    private static string Replacing(
        string name,
        IEnumerable<RowAccessRule> rules,
        IReadOnlyList<Policy> replaced,
        IReadOnlyList<Policy> others,
        IReadOnlyDictionary<RowAccessRule, IReadOnlyList<string>> roles,
        RowAccessRoleNames names)
    {
        var one = replaced.Count == 1;
        var kept = replaced.Count(policy => policy.Default!.Kept is not null);
        var text = new StringBuilder("-- ").Append(Commented(name)).Append(" asks ")
            .Append(Listed(rules.Select(rule => "the rule '" + Commented(rule.Name) + "'"
                + (rule.Roles.Count == 0 && !roles[rule].Contains(names.Anonymous, StringComparer.Ordinal) ? " (which names no role, so it is for the roles of the default)" : ""))))
            .Append(" in place of ")
            .Append(Listed(replaced.Select(policy => $"the default '{Commented(policy.Declared.Name)}' of the row access contribution {Commented(policy.From.Source)}")));

        if (replaced.Any(policy => policy.Default!.Within is not null))
        {
            text.Append(", held to what ").Append(one ? "that default holds" : "those defaults hold").Append(" a rule to");
        }

        if (kept > 0)
        {
            text.Append(", beside what ").Append(one ? "it keeps" : "they keep").Append(" whatever a rule says");
        }

        if (others.Count > 0)
        {
            text.Append(", and ").Append(Listed(others.Select(Described)));
        }

        return text.Append(1 + kept + others.Count > 1 ? ": a row one of them allows is allowed." : ".").ToString();
    }

    /// <summary>
    /// The access functions a rule's or a function's SQL asks, by name: <c>projects.is_member</c> for
    /// <c>ProjectMembership.Allows(project, caller)</c> and for <c>ProjectMembers.Allows(task.ProjectId)</c>,
    /// and a logical name, <c>desk/tickets_i_watch</c>, for a function named relative to its owner and for a
    /// question of an <c>[AccessFunctions]</c> class; and the name of a resource access set,
    /// <c>@Projects.Contracts.ProjectId/seen</c>, for a <c>[ResourceAccessContract]</c>, which the function a
    /// contribution says answers that set resolves (<see cref="ResourceAccessAnswer"/>). Not those written with
    /// <c>Sql.Call</c>, or a question named with its schema, which are functions of your own.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="sql"/> is null.</exception>
    public static IReadOnlyList<string> FunctionsAskedBy(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var names = new List<string>();
        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] is '{' or '}' && i + 1 < sql.Length && sql[i + 1] == sql[i])
            {
                i++;
                continue;
            }

            if (sql[i] != '{' || sql.IndexOf('}', i) is var end && end < 0)
            {
                continue;
            }

            var parts = sql[(i + 1)..end].Split(':');
            if (parts is ["call" or "fn", var name])
            {
                names.Add(name);
            }

            i = end;
        }

        return names;
    }

    /// <summary>The comment on every function a context made, an access function or a contributed one, which is how its next script finds them.</summary>
    private static string FunctionComment(DbContext context) => "DDDToolkit access function of " + context.GetType().Name;

    /// <summary>
    /// One access function, run as the function's owner so the tables of the aggregate's entities answer
    /// without their policies, and with an empty search path so nothing in the caller's session changes what
    /// it reads. A function about one row takes the aggregate's key, and its own parameters after it, and says
    /// whether its question is true of that row. A set-shaped one takes its own parameters and answers with
    /// the key of every row its question is true of.
    /// </summary>
    /// <remarks>
    /// A set-shaped function whose whole question is whether one of the aggregate's entities is so reads the
    /// entities' table alone, <c>SELECT DISTINCT e1."ProjectId" FROM ... e1 WHERE ...</c>, so its work grows with
    /// what the caller holds rather than with the aggregate's table.
    /// </remarks>
    /// <returns>How the function is named in a grant, and the statements that make it and give it its comment.</returns>
    /// <exception cref="InvalidOperationException">A set-shaped function is about an aggregate keyed on more than one column.</exception>
    private static (string Signature, string Statement) FunctionStatement(DbContext context, RowAccessFunction function, Writing writing)
    {
        var sql = writing.Sql;
        var root = RootOf(context, function.AggregateTypeName);
        var table = TableOf(root);
        var key = root.FindPrimaryKey()!.Properties;
        var name = writing.Names[function.LogicalName];
        var what = $"the access function '{function.LogicalName}'";
        List<string> parameters = function.Parameters.Length == 0 ? [] : [.. function.Parameters.Split(", ")];
        var qualified = sql.DelimitIdentifier(table.Name, table.Schema ?? DefaultSchema);

        string signature;
        string returns;
        string body;
        if (function.Shape == AccessFunctionShape.Set)
        {
            if (key.Count != 1)
            {
                throw new InvalidOperationException(
                    $"{char.ToUpperInvariant(what[0])}{what[1..]} answers with the keys of the rows of {root.ClrType.Name} it allows, and {root.ClrType.Name} is keyed on {key.Count} columns. A set-shaped function needs an aggregate whose key is one column.");
            }

            var filler = new Filler(what, root, table, writing, rootAlias: "root", argumentOffset: 0, arguments: parameters.Count);
            signature = name + "(" + string.Join(", ", parameters) + ")";
            returns = "SETOF " + key[0].GetColumnType(table);
            body = FromTheEntityTable(filler, function.Sql, root, table, sql)
                ?? "    SELECT root." + sql.DelimitIdentifier(key[0].GetColumnName(table)!) + " FROM " + qualified + " root\n"
                   + "    WHERE " + Parenthesized(OfItsType(root, table, filler.Fill(function.Sql), "root")) + "\n";
        }
        else
        {
            var filler = new Filler(what, root, table, writing, rootAlias: "root", argumentOffset: key.Count, arguments: parameters.Count);
            var byKey = string.Join(" AND ", filler.Key().Select((column, index) => $"{column} = ${index + 1}"));
            signature = name + "(" + string.Join(", ", [.. key.Select(property => property.GetColumnType(table)), .. parameters]) + ")";
            returns = "boolean";
            body = "    SELECT EXISTS (SELECT 1 FROM " + qualified + " root\n"
                   + "                   WHERE " + byKey + " AND " + Parenthesized(OfItsType(root, table, filler.Fill(function.Sql), "root")) + ")\n";
        }

        return (signature, new StringBuilder()
            .Append('\n')
            .Append("CREATE OR REPLACE FUNCTION ").Append(signature).Append(" RETURNS ").Append(returns).Append('\n')
            .Append("    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$").Append('\n')
            .Append(body)
            .Append("$function$;").Append('\n')
            .Append("COMMENT ON FUNCTION ").Append(signature).Append(" IS ").Append(Literal(FunctionComment(context))).Append(";\n")
            .ToString());
    }

    /// <summary>
    /// The body of a set-shaped function whose whole question is one <c>{exists}</c> over the aggregate's
    /// entities that asks nothing of the root row: the entities' own table, each root's key once. What it asks
    /// of the entities of those entities stays an <c>EXISTS</c> inside it, tied to the entity and not to the
    /// root. Null when the question is anything else, when the root's type shares its table with others of a
    /// hierarchy, whose rows only the root's table tells apart, or when the entities hold another key of the
    /// root than its primary key, which is not what the policy compares their answer with.
    /// </summary>
    private static string? FromTheEntityTable(Filler filler, string template, IEntityType root, StoreObjectIdentifier table, ISqlGenerationHelper sql)
    {
        if (OfItsType(root, table, "TRUE", "root") != "TRUE")
        {
            return null;
        }

        var tokens = Tokens(template).ToList();
        if (tokens.Count < 2
            || tokens[0].Start != 0
            || tokens[^1].End != template.Length - 1
            || tokens[0].Text.Split(':') is not ["exists", var navigation, var alias]
            || tokens[^1].Text != "/exists")
        {
            return null;
        }

        // How many blocks are open: the first one, and every block over an entity's own entities inside it.
        var open = 1;
        foreach (var (_, _, text) in tokens.Skip(1).SkipLast(1))
        {
            var parts = text.Split(':');
            switch (parts[0])
            {
                case "exists" when parts.Length == 4:
                    // The entities of an entity this block is looking at: nothing of the root's.
                    open++;
                    continue;
                case "/exists" when open > 1:
                    open--;
                    continue;
                case "exists" or "/exists" or "call" or "key":
                case "col" when parts.Length == 2:
                case "val" when parts.Length == 3:
                    // A second block next to the first, or a question about the root row, which only the
                    // root's table answers.
                    return null;
            }
        }

        if (open != 1)
        {
            return null;
        }

        if (root.FindNavigation(navigation) is not { ForeignKey.PrincipalKey: var principal } || !principal.IsPrimaryKey())
        {
            // The entities hold an alternate key of the root, which only the root's table turns into its key.
            return null;
        }

        var (entityTable, foreignKey) = filler.Enter(navigation, alias);
        if (foreignKey.Count != 1)
        {
            return null;
        }

        var condition = filler.Fill(template[(tokens[0].End + 1)..tokens[^1].Start]);
        return "    SELECT DISTINCT " + alias + "." + sql.DelimitIdentifier(foreignKey[0]) + " FROM " + entityTable + " " + alias + "\n"
               + "    WHERE " + Parenthesized(condition) + "\n";
    }

    /// <summary>
    /// The places of a template that are filled in: each <c>{...}</c>, with where it starts and ends and what
    /// is between the braces. A doubled brace is a brace, not a place.
    /// </summary>
    private static IEnumerable<(int Start, int End, string Text)> Tokens(string template)
    {
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] is '{' or '}' && i + 1 < template.Length && template[i + 1] == template[i])
            {
                i++;
                continue;
            }

            if (template[i] != '{' || template.IndexOf('}', i) is var end && end < 0)
            {
                continue;
            }

            yield return (i, end, template[(i + 1)..end]);
            i = end;
        }
    }

    /// <summary>
    /// Drops the functions an earlier script of this context made that it no longer writes, access functions
    /// and contributed ones. Those it still writes stay, and are replaced in place: dropping them would take
    /// the policies of other modules that call them along, or be refused.
    /// </summary>
    /// <param name="context">The context whose functions carry its comment.</param>
    /// <param name="kept">The functions it still writes, by the names they have in the database, <c>schema.name</c>.</param>
    /// <remarks>
    /// A function that something still depends on is left for a later file: a policy of another module whose
    /// access file comes after this one may still ask it, until that file asks the function's new name, and
    /// Postgres refuses to drop it meanwhile. The next file of this context drops it, once nothing does.
    /// </remarks>
    private static string DropFunctionsStatement(DbContext context, IReadOnlyList<string> kept)
    {
        var names = NamePairs(kept);

        return Blocks(1, new StringBuilder()
            .Append("DO $ddd$").Append('\n')
            .Append("DECLARE").Append('\n')
            .Append("    generated record;").Append('\n')
            .Append("BEGIN").Append('\n')
            .Append("    FOR generated IN").Append('\n')
            .Append("        SELECT p.oid::regprocedure AS signature").Append('\n')
            .Append("        FROM pg_catalog.pg_proc p").Append('\n')
            .Append("        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace").Append('\n')
            .Append("        JOIN pg_catalog.pg_description d ON d.objoid = p.oid AND d.classoid = 'pg_catalog.pg_proc'::regclass").Append('\n')
            .Append("        WHERE d.description = ").Append(Literal(FunctionComment(context))).Append('\n')
            .Append(names.Count == 0 ? "" : "          AND (n.nspname, p.proname) NOT IN (" + string.Join(", ", names) + ")\n")
            .Append("          -- A function a policy still asks, of a module whose file comes after this one, stays until the next file.").Append('\n')
            .Append("          AND NOT EXISTS (SELECT FROM pg_catalog.pg_depend dependent").Append('\n')
            .Append("                          WHERE dependent.refclassid = 'pg_catalog.pg_proc'::regclass AND dependent.refobjid = p.oid AND dependent.deptype = 'n')").Append('\n')
            .Append("    LOOP").Append('\n')
            .Append("        EXECUTE format('DROP FUNCTION %s', generated.signature);").Append('\n')
            .Append("    END LOOP;").Append('\n')
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .ToString());
    }

    /// <summary>
    /// The functions a script writes, <c>schema.name</c>, as the schema and name Postgres keeps: in lower case,
    /// since a script writes them unquoted.
    /// </summary>
    private static List<string> NamePairs(IEnumerable<string> names)
        => [.. names
            .Order(StringComparer.Ordinal)
            .Select(name => name.ToLowerInvariant().Split('.'))
            .Select(parts => $"({Literal(parts[0])}, {Literal(parts[1])})")];

    private static IEntityType RootOf(DbContext context, string aggregateTypeName)
        => context.Model.GetEntityTypes().Single(entity => entity.ClrType.FullName == aggregateTypeName && !entity.IsOwned());

    /// <summary>
    /// Every table of an aggregate's entities that is not the table of the one it belongs to, with the
    /// chain up to the root: a line to its order; a line's parts to the line, and the line to the order.
    /// </summary>
    /// <param name="owner">The root, or an entity of it.</param>
    /// <param name="ownerTable">The table <paramref name="owner"/>'s rows are in.</param>
    /// <param name="above">The chain from <paramref name="ownerTable"/> up to the root; empty for the root.</param>
    private static IEnumerable<(StoreObjectIdentifier Table, IReadOnlyList<Link> Chain, IEntityType Entity)> EntitiesOf(IEntityType owner, StoreObjectIdentifier ownerTable, IReadOnlyList<Link> above)
    {
        foreach (var navigation in owner.GetNavigations().Where(navigation => navigation.ForeignKey.IsOwnership && navigation.TargetEntityType.IsOwned()))
        {
            var target = navigation.TargetEntityType;
            if (target.GetTableName() is not { } name)
            {
                continue;
            }

            var table = StoreObjectIdentifier.Table(name, target.GetSchema());
            if (table == ownerTable)
            {
                // Stored inline, in the owner's own rows: the owner's policies already cover it.
                foreach (var inner in EntitiesOf(target, ownerTable, above))
                {
                    yield return inner;
                }

                continue;
            }

            var key = navigation.ForeignKey.PrincipalKey.Properties
                .Zip(navigation.ForeignKey.Properties, (principal, dependent) => (principal.GetColumnName(ownerTable)!, dependent.GetColumnName(table)!))
                .ToList();
            List<Link> chain = [new Link(ownerTable, key), .. above];

            yield return (table, chain, target);

            foreach (var inner in EntitiesOf(target, table, chain))
            {
                yield return inner;
            }
        }
    }

    /// <summary>
    /// One step up from an entity's table: the table of the one it belongs to, and the columns that tie
    /// them, the parent's first.
    /// </summary>
    private sealed record Link(StoreObjectIdentifier Parent, IReadOnlyList<(string Parent, string Child)> Key);

    private static List<StoreObjectIdentifier> TablesOf(DbContext context)
        => [.. context.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null)
            .Select(TableOf)
            .Distinct()
            .OrderBy(table => table.Schema ?? DefaultSchema, StringComparer.Ordinal)
            .ThenBy(table => table.Name, StringComparer.Ordinal)];

    private static StoreObjectIdentifier TableOf(IEntityType entity)
        => StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

    /// <summary>
    /// What a policy for <paramref name="command"/> checks: the rows it lets a caller see or touch,
    /// <paramref name="existing"/>, and the rows it lets a caller leave behind, <paramref name="left"/>. A
    /// rule's condition is both.
    /// </summary>
    private static string Clauses(string command, string? existing, string? left)
        => command switch
        {
            "INSERT" => "    WITH CHECK " + Parenthesized(left!),
            "SELECT" or "DELETE" => "    USING " + Parenthesized(existing!),
            _ => "    USING " + Parenthesized(existing!) + "\n    WITH CHECK " + Parenthesized(left!),
        };

    /// <summary>
    /// A policy name Postgres keeps as it is: it cuts identifiers at 63 bytes, and two names that agree on
    /// their first 63 would be one policy. A longer one keeps a readable start and a hash of the whole.
    /// </summary>
    private static string PolicyName(string name)
    {
        if (Encoding.UTF8.GetByteCount(name) <= MaxIdentifierBytes)
        {
            return name;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8];
        var start = name;
        while (Encoding.UTF8.GetByteCount(start) > MaxIdentifierBytes - hash.Length - 3)
        {
            start = start[..^1];
        }

        return start.TrimEnd() + " ~ " + hash;
    }

    /// <summary>
    /// A condition in parentheses, which Postgres wants around a policy's and a function's conditions, and
    /// which a rule that is one column, <c>"IsPublic"</c>, does not have. A condition that one pair of
    /// parentheses encloses from its first character to its last keeps that pair, unless the pair is a
    /// subquery's own: a rule that is one scalar question is <c>(SELECT f())</c>, and
    /// <c>WITH CHECK (SELECT f())</c> is no condition Postgres reads, so it gets a pair of its own. A
    /// parenthesis inside a literal, a quoted name or a comment is not counted. A condition with a
    /// <c>--</c> comment in it gets its closing parenthesis on a line of its own, since that comment runs to
    /// the end of its line and would hold the parenthesis, and whatever was written after it, too.
    /// </summary>
    private static string Parenthesized(string condition)
    {
        var masked = Masked(condition);
        if (masked.StartsWith('(') && ClosingParenthesis(masked, 0) == masked.Length - 1 && !IsQuery(masked[1..^1]))
        {
            return condition;
        }

        return QuotedOrComment().Matches(condition).Any(static match => IsLineComment(match.Value))
            ? "(" + condition + "\n)"
            : "(" + condition + ")";
    }

    /// <summary>
    /// <paramref name="sql"/> as long as it was, with every literal and quoted name a run of zeros and every
    /// comment blank: what is left to read are the parentheses and the words outside them.
    /// </summary>
    private static string Masked(string sql)
        => QuotedOrComment().Replace(sql, match => new string(IsComment(match.Value) ? ' ' : '0', match.Length));

    /// <summary>Whether a match of <see cref="QuotedOrComment"/> is a comment, rather than a literal or a quoted name.</summary>
    private static bool IsComment(string match) => IsLineComment(match) || match.StartsWith("/*", StringComparison.Ordinal);

    /// <summary>Whether a match of <see cref="QuotedOrComment"/> is a comment that runs to the end of its line.</summary>
    private static bool IsLineComment(string match) => match.StartsWith("--", StringComparison.Ordinal);

    /// <summary>
    /// The index of the parenthesis that closes the one at <paramref name="open"/> in masked SQL, the ones in
    /// between counted; -1 when none does.
    /// </summary>
    private static int ClosingParenthesis(string masked, int open)
    {
        var depth = 0;
        for (var i = open; i < masked.Length; i++)
        {
            depth += masked[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether masked SQL is a query rather than a value: it starts with <c>SELECT</c>, <c>WITH</c>,
    /// <c>VALUES</c> or <c>TABLE</c>, or with a query in parentheses that a set operation or a clause of a
    /// query goes on from, <c>(SELECT 1) UNION (SELECT 2)</c>. A query in parentheses with nothing after it,
    /// <c>(SELECT f())</c>, is a value: a subquery.
    /// </summary>
    private static bool IsQuery(string masked)
    {
        var text = masked.TrimStart();
        if (!text.StartsWith('('))
        {
            return StartsAQuery().IsMatch(text);
        }

        var close = ClosingParenthesis(text, 0);
        return close > 0 && IsQueryInParentheses(text[..(close + 1)]) && GoesOnAsAQuery().IsMatch(text[(close + 1)..].TrimStart());
    }

    /// <summary>
    /// Whether masked SQL that one pair of parentheses encloses holds a query, however many pairs deep:
    /// <c>(SELECT 1)</c> and <c>((SELECT 1))</c> do, <c>((SELECT 1) = 1)</c> does not.
    /// </summary>
    private static bool IsQueryInParentheses(string enclosed)
    {
        var inner = enclosed[1..^1].Trim();
        return IsQuery(inner) || (inner.StartsWith('(') && ClosingParenthesis(inner, 0) == inner.Length - 1 && IsQueryInParentheses(inner));
    }

    /// <summary>The word a query starts with.</summary>
    [GeneratedRegex(@"^(SELECT|WITH|VALUES|TABLE)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex StartsAQuery();

    /// <summary>A word that goes on from a query in parentheses and makes the whole a query: a set operation, or a clause of one.</summary>
    [GeneratedRegex(@"^(UNION|INTERSECT|EXCEPT|ORDER|LIMIT|OFFSET|FETCH|FOR)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex GoesOnAsAQuery();

    /// <summary>An enum constant as the column stores it: through the property's converter, or as its number.</summary>
    private static string Stored(IProperty property, string number)
    {
        var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (!enumType.IsEnum)
        {
            return number;
        }

        var value = Enum.ToObject(enumType, long.Parse(number, CultureInfo.InvariantCulture));
        var converter = property.GetValueConverter() ?? property.GetTypeMapping().Converter;
        var stored = converter is null ? Convert.ChangeType(value, Enum.GetUnderlyingType(enumType), CultureInfo.InvariantCulture) : converter.ConvertToProvider(value);

        return stored switch
        {
            null => "NULL",
            string text => Literal(text),
            bool flag => flag ? "TRUE" : "FALSE",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Literal(stored.ToString()!),
        };
    }

    /// <summary>
    /// Fills in the SQL the generator wrote for one place it goes: a policy on the aggregate's table, where a
    /// column is the row's own, or the body of an access function, where the row is <c>root</c> and the
    /// aggregate's entities can be asked. Every <c>{{</c> and <c>}}</c> is a brace again.
    /// </summary>
    /// <remarks>
    /// In a function's body, <paramref name="argumentOffset"/> is how many parameters come before the function's
    /// own, the key's, so its <paramref name="arguments"/> parameters are <c>$offset+1</c> onwards; in a policy
    /// there are none. A function a rule asks by its logical name is written as the name it has in the database.
    /// </remarks>
    private sealed class Filler(string what, IEntityType root, StoreObjectIdentifier table, Writing writing, string? rootAlias, int argumentOffset = -1, int arguments = 0)
    {
        /// <summary>The entities an <c>{exists}</c> is looking at, by their alias.</summary>
        private readonly Dictionary<string, (IEntityType Entity, StoreObjectIdentifier Table)> _aliases = new(StringComparer.Ordinal);

        /// <summary>
        /// Whether what was filled in so far reads a column of the row besides its key: a rule that reads only the
        /// key, or nothing of the row, answers the same of a row before and after a change that keeps the key.
        /// </summary>
        public bool ReadsBeyondTheKey { get; private set; }

        public string Fill(string template)
        {
            var filled = new StringBuilder(template.Length * 2);

            for (var i = 0; i < template.Length; i++)
            {
                var character = template[i];
                if ((character is '{' or '}') && i + 1 < template.Length && template[i + 1] == character)
                {
                    filled.Append(character);
                    i++;
                }
                else if (character == '{')
                {
                    var end = template.IndexOf('}', i);
                    if (end < 0)
                    {
                        throw new InvalidOperationException($"The SQL of {what} has a '{{' without its '}}': {template}");
                    }

                    filled.Append(Token(template[(i + 1)..end]));
                    i = end;
                }
                else
                {
                    filled.Append(character);
                }
            }

            return filled.ToString();
        }

        /// <summary>The row's key, as an access function is called with it: <c>"Id"</c> in a policy, <c>root."Id"</c> in a function.</summary>
        public IEnumerable<string> Key()
            => root.FindPrimaryKey()!.Properties.Select(property => Qualified(rootAlias, property.GetColumnName(table)!));

        private string Token(string token)
        {
            var parts = token.Split(':');
            switch (parts[0])
            {
                case "col" when parts.Length == 2:
                    ReadsBeyondTheKey |= !PropertyOf(root, table, parts[1]).IsPrimaryKey();
                    return Qualified(rootAlias, ColumnOf(root, table, parts[1]));
                case "col" when parts.Length == 3:
                    var (entity, entityTable) = Alias(parts[1], token);
                    return Qualified(parts[1], ColumnOf(entity, entityTable, parts[2]));
                case "val" when parts.Length == 3:
                    return Stored(PropertyOf(root, table, parts[1]), parts[2]);
                case "val" when parts.Length == 4:
                    var (aliased, aliasedTable) = Alias(parts[1], token);
                    return Stored(PropertyOf(aliased, aliasedTable, parts[2]), parts[3]);
                case "caller" when parts.Length >= 2:
                    return AboutTheCaller(parts, writing) ?? throw Unknown(token);
                case "exists" when parts.Length == 3:
                    return Exists(parts[1], parts[2]);
                case "exists" when parts.Length == 4:
                    return ExistsWithin(parts[1], parts[2], parts[3], token);
                case "/exists" when parts.Length == 1:
                    return "))";
                case "call" when parts.Length == 2:
                    return Function(parts[1]) + "(" + string.Join(", ", Key()) + ")";
                case "fn" when parts.Length == 2:
                    // Asked by a key the rule holds, whose argument the template writes itself.
                    return Function(parts[1]);
                case "key" when parts.Length == 1:
                    // The row's key, as a rule that passes an access function more than the row writes it.
                    return string.Join(", ", Key());
                case "arg" when parts.Length == 2:
                    return Argument(parts[1], token);
                default:
                    throw Unknown(token);
            }
        }

        /// <summary>
        /// The name a function has in the database: a name with its schema as it is, and a logical one,
        /// <c>owner/name</c>, as the functions the script is written with resolve it.
        /// </summary>
        /// <exception cref="InvalidOperationException">No function the script is written with has the logical name.</exception>
        private string Function(string name) => FunctionName(what, name, writing);

        /// <summary>The access function's own parameter <paramref name="number"/>, counted from 1, as its body refers to it: <c>$n</c>.</summary>
        private string Argument(string number, string token)
            => argumentOffset >= 0 && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 1 && index <= arguments
                ? "$" + (argumentOffset + index).ToString(CultureInfo.InvariantCulture)
                : throw new InvalidOperationException(
                    $"The SQL of {what} asks for '{{{token}}}', a parameter it does not have. Rebuild the project that declares it with this version of the toolkit.");

        /// <summary>
        /// Starts looking at the entities of <paramref name="navigationName"/> as <paramref name="alias"/>: their
        /// table, and the columns of it that hold the key of the root they belong to.
        /// </summary>
        public (string Table, IReadOnlyList<string> ForeignKey) Enter(string navigationName, string alias)
        {
            var (navigation, entityTable) = EntitiesAt(navigationName);
            _aliases[alias] = (navigation.TargetEntityType, entityTable);
            return (writing.Sql.DelimitIdentifier(entityTable.Name, entityTable.Schema ?? DefaultSchema), [.. navigation.ForeignKey.Properties.Select(property => property.GetColumnName(entityTable)!)]);
        }

        /// <summary>The navigation to a collection of the root's entities in a table of their own, and that table.</summary>
        private (INavigation Navigation, StoreObjectIdentifier Table) EntitiesAt(string navigationName)
        {
            if (rootAlias is null)
            {
                throw new InvalidOperationException(
                    $"{Capitalized(what)} reads {root.ClrType.Name}.{navigationName}, the aggregate's entities, which only an access function can: a policy that read their table would ask itself. Rebuild the project that declares it with this version of the toolkit.");
            }

            if (root.FindNavigation(navigationName) is not { IsCollection: true, ForeignKey.IsOwnership: true } navigation
                || TableOf(navigation.TargetEntityType) is var entityTable && entityTable == table)
            {
                throw new InvalidOperationException(
                    $"{Capitalized(what)} reads {root.ClrType.Name}.{navigationName}, which the Entity Framework model does not map as a collection of {root.ClrType.Name}'s entities in a table of their own.");
            }

            return (navigation, entityTable);
        }

        /// <summary>
        /// <c>EXISTS (SELECT 1 FROM the entities' table e1 WHERE e1 belongs to root AND (</c>, closed by
        /// <c>{/exists}</c>. Only in an access function, which reads the entities' table without its policies.
        /// </summary>
        private string Exists(string navigationName, string alias)
        {
            var (navigation, entityTable) = EntitiesAt(navigationName);
            _aliases[alias] = (navigation.TargetEntityType, entityTable);

            var belongs = navigation.ForeignKey.PrincipalKey.Properties.Zip(
                navigation.ForeignKey.Properties,
                (principal, dependent) => $"{Qualified(alias, dependent.GetColumnName(entityTable)!)} = {Qualified(rootAlias, principal.GetColumnName(table)!)}");

            return $"EXISTS (SELECT 1 FROM {writing.Sql.DelimitIdentifier(entityTable.Name, entityTable.Schema ?? DefaultSchema)} {alias} WHERE {string.Join(" AND ", belongs)} AND (";
        }

        /// <summary>
        /// <c>EXISTS (SELECT 1 FROM the table of e1's entities e2 WHERE e2 belongs to e1 AND (</c>, closed by
        /// <c>{/exists}</c>: the entities of an entity an outer <c>{exists}</c> is looking at, tied to that
        /// entity's row by the key Entity Framework gives them.
        /// </summary>
        private string ExistsWithin(string ownerAlias, string navigationName, string alias, string token)
        {
            var (owner, ownerTable) = Alias(ownerAlias, token);
            if (owner.FindNavigation(navigationName) is not { IsCollection: true, ForeignKey.IsOwnership: true } navigation
                || TableOf(navigation.TargetEntityType) is var entityTable && entityTable == ownerTable)
            {
                throw new InvalidOperationException(
                    $"{Capitalized(what)} reads {owner.ClrType.Name}.{navigationName}, which the Entity Framework model does not map as a collection of {owner.ClrType.Name}'s entities in a table of their own.");
            }

            _aliases[alias] = (navigation.TargetEntityType, entityTable);

            var belongs = navigation.ForeignKey.PrincipalKey.Properties.Zip(
                navigation.ForeignKey.Properties,
                (principal, dependent) => $"{Qualified(alias, dependent.GetColumnName(entityTable)!)} = {Qualified(ownerAlias, principal.GetColumnName(ownerTable)!)}");

            return $"EXISTS (SELECT 1 FROM {writing.Sql.DelimitIdentifier(entityTable.Name, entityTable.Schema ?? DefaultSchema)} {alias} WHERE {string.Join(" AND ", belongs)} AND (";
        }

        private (IEntityType Entity, StoreObjectIdentifier Table) Alias(string alias, string token)
            => _aliases.TryGetValue(alias, out var entity)
                ? entity
                : throw new InvalidOperationException($"The SQL of {what} asks for '{{{token}}}' outside the {{exists}} that names '{alias}'. Rebuild the project that declares it with this version of the toolkit.");

        private string ColumnOf(IEntityType entity, StoreObjectIdentifier entityTable, string path)
            => PropertyOf(entity, entityTable, path).GetColumnName(entityTable)
                ?? throw new InvalidOperationException($"{Capitalized(what)} reads {entity.ClrType.Name}.{path}, which has no column in {entityTable.DisplayName()}.");

        /// <summary>The property at <paramref name="path"/>: <c>Status</c>, or <c>Amount.Amount</c> through a value object stored inline.</summary>
        private IProperty PropertyOf(IEntityType entity, StoreObjectIdentifier entityTable, string path)
        {
            ITypeBase type = entity;
            var names = path.Split('.');

            for (var i = 0; i < names.Length - 1; i++)
            {
                if (type.FindComplexProperty(names[i]) is { } complex)
                {
                    type = complex.ComplexType;
                    continue;
                }

                if (type is IEntityType owner
                    && owner.FindNavigation(names[i]) is { ForeignKey.IsOwnership: true } owned
                    && TableOf(owned.TargetEntityType) == entityTable)
                {
                    type = owned.TargetEntityType;
                    continue;
                }

                throw new InvalidOperationException(
                    $"{Capitalized(what)} reads {entity.ClrType.Name}.{path}, and {names[i]} is not a value object stored in {entity.ClrType.Name}'s own table.");
            }

            return type.FindProperty(names[^1])
                ?? throw new InvalidOperationException($"{Capitalized(what)} reads {entity.ClrType.Name}.{path}, which the Entity Framework model does not map to a column.");
        }

        private InvalidOperationException Unknown(string token)
            => new($"The SQL of {what} asks for '{{{token}}}', which this version of DDDToolkit.EntityFramework.Postgres does not know. Use the same version of the toolkit's packages everywhere.");

        private static string Qualified(string? alias, string column) => alias is null ? Quote(column) : alias + "." + Quote(column);
    }

    /// <summary>
    /// A <c>{caller:...}</c> place of a template, <paramref name="parts"/> split at <c>:</c>, as a condition asks
    /// it: <c>(SELECT auth.uid())</c> for <c>uid</c>, and likewise <c>signedin</c>, <c>role</c>, <c>claims</c>
    /// and <c>claim:path</c>; null for anything else.
    /// </summary>
    private static string? AboutTheCaller(string[] parts, Writing writing)
        => parts[1] switch
        {
            "uid" when parts.Length == 2 => $"(SELECT {writing.Callers.UserId})",
            "signedin" when parts.Length == 2 => $"((SELECT {writing.Callers.UserId}) IS NOT NULL)",
            "role" when parts.Length == 2 => $"(SELECT {writing.Callers.Role})",
            "claims" when parts.Length == 2 => $"(SELECT {writing.Callers.Claims})",
            "claim" when parts.Length == 3 => Claim(parts[2], writing),
            _ => null,
        };

    /// <summary><c>ddd.caller_claims() -&gt;&gt; 'email'</c> for a claim, <c>ddd.caller_claims() #&gt;&gt; '{app_metadata,role}'</c> for a path into one.</summary>
    private static string Claim(string path, Writing writing)
        => path.Contains('.', StringComparison.Ordinal)
            ? $"(SELECT {writing.Callers.Claims} #>> {Literal("{" + path.Replace('.', ',') + "}")})"
            : $"(SELECT {writing.Callers.Claims} ->> {Literal(path)})";

    /// <summary>
    /// The name a function <paramref name="what"/> asks has in the database: a name with its schema as it is,
    /// and a logical one, <c>owner/name</c>, as the functions the script is written with resolve it.
    /// </summary>
    /// <exception cref="InvalidOperationException">No function the script is written with has the logical name.</exception>
    private static string FunctionName(string what, string name, Writing writing)
    {
        if (!RowAccessNames.IsLogical(name))
        {
            return name;
        }

        if (writing.Names.TryGetValue(name, out var resolved))
        {
            return resolved;
        }

        throw new InvalidOperationException(ResourceAccessAnswer.IsName(name)
            ? $"{Capitalized(what)} asks {ResourceAccessAnswer.Described(name)}, and no row access contribution answers it for the contexts this is written with. "
              + "Hand the script the contribution that keeps that resource's access in RowAccessExport.Contributions, the Membership package's for a resource with members, which the Supabase export makes from the rules the application marks [MembershipRules<TMember>]. "
              + "Where it answers for another context, the one that maps the resource, write the contexts together with PostgresRowAccess.Scripts, "
              + "or hand this script that context's names in RowAccessExport.FunctionNames, from PostgresRowAccess.FunctionNamesOf(contexts, functions, export)."
            : $"{Capitalized(what)} asks the function {name}, and none of the functions this is written with is called that. Define it with [AccessFunction<TAggregate>(\"{name}\")] in the module whose aggregate it is about, or name it with its schema, schema.name.");
    }

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];


    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Literal(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
}
