using System.Text;
using System.Text.RegularExpressions;
using DDDToolkit.EntityFramework.Postgres;

namespace DDDToolkit.EntityFramework.Supabase;

// The migration that makes the role the application logs in as, SupabaseMigrationOptions.LoginRole: a role that
// owns nothing, holds no privilege, and may switch to the roles its callers run as and to nothing else. Its only
// fact of its own is the role's name; the roles it is given are the ones the policies are written for, so it is
// written from those, and a role mapped there reaches the login role with the next build instead of with a
// migration somebody remembers to write.
//
// One file per change, as the access files are, never one rewritten: Supabase applies a version once, so a file it
// applied is never read again, and a database that is only as far as an older version runs that file as it is. So
// the newest file says everything: it makes the role where the database lacks it, takes back the roles the file
// before it granted and no caller runs as any more, and grants the roles they run as now. It is numbered after
// every file in the directory when it is written, so it comes after the access file that made the roles it grants;
// it makes those the access files make where they are missing all the same, as an access file makes the roles its
// policies name, so it is applied in a working order whatever came first.
//
// No login and no password: a migration is kept in a repository, and a password is not. The deployment turns the
// login on, once, as the database's owner.
public static partial class SupabaseMigrations
{
    /// <summary>A login role a message offers where it refuses one.</summary>
    private const string ExampleLoginRole = "sample_api";

    /// <summary>How long a name Postgres keeps: it cuts a longer one, so it would name another role than the one asked about.</summary>
    private const int MaxRoleNameBytes = 63;

    /// <summary>What a login role file's name says after its version and before the role, as in <c>20261005120000_login_role.sample_api.ddd.sql</c>.</summary>
    private const string LoginRoleFileName = "login_role";

    // The first line of every login role file, naming the role it makes.
    private static readonly Regex LoginRoleHeader = new(
        @"^-- Written by DDDToolkit for the role the application logs in as, (?<role>\S+)\.$",
        RegexOptions.CultureInvariant);

    // What a login role file says before it takes back the roles the file before it granted.
    private const string LoginRevokeNote = "-- Granted by the file before this one, and no role a caller runs as any more:";

    private static readonly Regex LoginRevokeBlock = new(
        Regex.Escape(LoginRevokeNote) + @".*?\$ddd\$;\n\n",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    // A name Postgres reads as it is written, without quotes: a lowercase letter or an underscore, then lowercase
    // letters, digits and underscores.
    private static readonly Regex PlainIdentifier = new(@"^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The words SQL keeps for itself, and those it keeps for the names of types and functions: either names no
    /// role in a <c>GRANT</c> without quotes. Postgres 17's, which are those of every version before it.
    /// </summary>
    private static readonly HashSet<string> Keywords = new(
        """
        all analyse analyze and any array as asc asymmetric both case cast check collate column constraint create
        current_catalog current_date current_role current_time current_timestamp current_user default deferrable desc
        distinct do else end except false fetch for foreign from grant group having in initially intersect into lateral
        leading limit localtime localtimestamp not null offset on only or order placing primary references returning
        select session_user some symmetric system_user table then to trailing true union unique user using variadic
        when where window with
        authorization binary collation concurrently cross current_schema freeze full ilike inner is isnull join left
        like natural notnull outer overlaps right similar tablesample verbose
        """.Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
        StringComparer.Ordinal);

    /// <summary>
    /// Postgres's and Supabase's own roles, besides those whose names start with one of <see cref="PlatformPrefixes"/>:
    /// the roles the platform logs in as, switches to or keeps for itself, and the two names Postgres keeps from
    /// every role.
    /// </summary>
    private static readonly HashSet<string> PlatformRoles = new(
        ["postgres", "authenticated", "anon", "service_role", "authenticator", "dashboard_user", "pgbouncer", "pgtle_admin", "public", "none"],
        StringComparer.Ordinal);

    /// <summary>The beginnings of the names of Postgres's and Supabase's own roles.</summary>
    private static readonly string[] PlatformPrefixes = ["pg_", "pgsodium_", "supabase_"];

    /// <summary>
    /// Why <paramref name="role"/> cannot be the role the application logs in as, looked at on its own; <see langword="null"/>
    /// when it can. The migration writes it as it is, so it is a plain lowercase identifier and no word SQL keeps,
    /// and it is a role of the application's own, so none of Postgres's or Supabase's.
    /// </summary>
    internal static string? NotALoginRole(string role)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (!PlainIdentifier.IsMatch(role))
        {
            return $"'{role}' is not a plain lowercase identifier, which is how the migration writes it: a lowercase letter or '_', then lowercase letters, digits and '_'. Use a name such as {ExampleLoginRole}.";
        }

        if (Encoding.UTF8.GetByteCount(role) > MaxRoleNameBytes)
        {
            return $"'{role}' is longer than the {MaxRoleNameBytes} bytes of a name Postgres keeps, so the role it made would not be the one the migration asks about. Use a shorter name, such as {ExampleLoginRole}.";
        }

        if (Keywords.Contains(role))
        {
            return $"'{role}' is a word SQL keeps for itself, which names no role where the migration writes it. Use another name, such as {ExampleLoginRole}.";
        }

        return PlatformRoles.Contains(role) || PlatformPrefixes.Any(prefix => role.StartsWith(prefix, StringComparison.Ordinal))
            ? $"'{role}' is one of Postgres's or Supabase's own roles. The application logs in as a role of its own, which owns nothing and only switches to the roles its callers run as; use a name such as {ExampleLoginRole}."
            : null;
    }

    /// <summary>
    /// What <paramref name="roles"/> maps to <paramref name="role"/>, as a key of <c>SupabaseRowAccessRoles</c>:
    /// <c>user</c>, <c>anonymous</c>, <c>system-in</c>, <c>system</c> or <c>token:&lt;role&gt;</c>; <see langword="null"/>
    /// for a role no caller runs as. The role the application logs in as is never one of them: it switches to
    /// them, and whoever reaches its connection could otherwise be every caller at once without switching.
    /// </summary>
    internal static string? CallerRoleNamed(string role, RowAccessRoleNames roles)
        => CallerRoles(roles).FirstOrDefault(caller => string.Equals(caller.Role, role, StringComparison.Ordinal))?.Key;

    /// <summary>
    /// The file that makes <paramref name="options"/>' login role: unchanged when the newest one for that role says
    /// what it would say now, and otherwise a new one, numbered after every file in the directory.
    /// </summary>
    /// <param name="login">The role the application logs in as.</param>
    /// <param name="directory">The Supabase migrations directory, with every module's files written as this run writes them.</param>
    /// <param name="options">The roles callers run as, and the clock a new file takes its version from.</param>
    /// <param name="write">Whether to write a new file; when not, the file a write would add is reported missing.</param>
    private static SupabaseMigrationEntry LoginRoleEntry(string login, string directory, SupabaseMigrationOptions options, bool write)
    {
        var existing = ExistingFiles(directory);
        var newest = existing.Values.SelectMany(paths => paths)
            .Where(path => LoginRoleOf(path) == login)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .LastOrDefault();

        var granted = CallerRoles(options.Roles);
        var previous = newest is null ? "" : Normalize(File.ReadAllText(newest));
        var revoked = GrantedBy(previous, login).Except(granted.Select(role => role.Identifier), StringComparer.Ordinal).ToList();
        var sql = LoginRoleScript(login, granted, revoked);

        // The roles the file before took back are no part of what a file says the role is now, so a file that says
        // the same of it otherwise is the same file.
        if (newest is not null && string.Equals(LoginRevokeBlock.Replace(previous, ""), LoginRevokeBlock.Replace(sql, ""), StringComparison.Ordinal))
        {
            return new($"{VersionOf(newest)}_{LoginRoleFileName}", SupabaseMigrationStatus.Unchanged, newest);
        }

        if (!write)
        {
            return new($"{login} login role", SupabaseMigrationStatus.Missing, Path.Combine(directory, $"_{LoginRoleFileName}.{login}.ddd.sql"));
        }

        var version = NextVersion(options.TimeProvider.GetUtcNow().UtcDateTime, existing.Keys);
        var path = Path.Combine(directory, $"{version}_{LoginRoleFileName}.{login}.ddd.sql");

        Directory.CreateDirectory(directory);
        File.WriteAllText(path, sql, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new($"{version}_{LoginRoleFileName}", SupabaseMigrationStatus.Created, path);
    }

    /// <summary>
    /// The login role file: the role made where it is missing and refused where it could get past the policies,
    /// the roles callers run as made where the toolkit makes them and an access file has not, the roles of the
    /// file before it that no caller runs as any more taken back, and the roles callers run as granted.
    /// <para>
    /// A role that exists already is refused rather than changed, as an access file refuses a bookkeeping role it
    /// did not make: the file cannot tell why it is so, and whoever made it can. Refused are the attributes that
    /// take a statement on its connection past the policies without switching to a caller's role: a superuser, a
    /// role that bypasses row level security, one that may create roles, which before Postgres 16 may grant itself
    /// any role but a superuser, <c>service_role</c> for one, and one that may replicate, which reads every change
    /// of every table from a replication slot. And a role that has the privileges of a role it is a member of: by
    /// its attribute, which is all there is before Postgres 16, and since then by a grant that says so, as a grant
    /// made while the role inherited does, whatever the role was altered to since.
    /// </para>
    /// <para>
    /// Each role is granted on its own, unless the role may switch to it already, and a grant made at the same
    /// moment by a migration of another database on the server is found made, as the access files grant theirs:
    /// roles are the server's.
    /// </para>
    /// </summary>
    /// <param name="login">The role the application logs in as, a plain lowercase identifier.</param>
    /// <param name="granted">The roles callers run as, in the order the file grants them.</param>
    /// <param name="revoked">The roles the file before this one granted and this one does not, as it spelled them.</param>
    private static string LoginRoleScript(string login, IReadOnlyList<CallerRole> granted, IReadOnlyList<string> revoked)
    {
        var width = granted.Max(role => role.Identifier.Length) + 2;
        var sql = new StringBuilder()
            .Append("-- Written by DDDToolkit for the role the application logs in as, ").Append(login).Append('.').Append('\n')
            .Append("-- Written from SupabaseLoginRole and SupabaseRowAccessRoles; change those, not this file. Every file like it").Append('\n')
            .Append("-- says what the role is now: it makes it, takes back what the one before it granted and no caller runs as").Append('\n')
            .Append("-- any more, and grants the roles callers run as.").Append('\n')
            .Append("--").Append('\n')
            .Append("-- The role owns nothing and is given no privilege on a table, a schema or a function. All it holds is the").Append('\n')
            .Append("-- right to switch to the roles its callers run as, each held to its policies and its privileges:").Append('\n');

        foreach (var role in granted)
        {
            sql.Append("--   ").Append(role.Identifier.PadRight(width)).Append(role.What).Append('\n');
        }

        sql
            .Append("-- It is NOINHERIT, so it has none of their privileges until it switches to one of them. A statement that").Append('\n')
            .Append("-- reaches the application's connection can always go back to the role that logged in; this is what makes").Append('\n')
            .Append("-- that role worth nothing.").Append('\n')
            .Append("--").Append('\n')
            .Append("-- No LOGIN and no password here: a migration is kept in a repository, and a password is not. Whoever deploys").Append('\n')
            .Append("-- turns the login on once, as the database's owner, with a secret of that deployment:").Append('\n')
            .Append("--     ALTER ROLE ").Append(login).Append(" WITH LOGIN PASSWORD '...';").Append('\n')
            .Append('\n')
            .Append("DO $ddd$").Append('\n')
            .Append("DECLARE").Append('\n')
            .Append("    attributes text;").Append('\n')
            .Append("    inherited text;").Append('\n')
            .Append("BEGIN").Append('\n')
            .Append(MadeWhereMissing(login, login))
            // The hint names only what the role has: only a superuser may write NOSUPERUSER at all, even of a role that
            // is none, and the owner of a Supabase project is no superuser.
            .Append("    SELECT pg_catalog.concat_ws(' ', CASE WHEN rolsuper THEN 'NOSUPERUSER' END, CASE WHEN rolbypassrls THEN 'NOBYPASSRLS' END,").Append('\n')
            .Append("               CASE WHEN rolcreaterole THEN 'NOCREATEROLE' END, CASE WHEN rolreplication THEN 'NOREPLICATION' END,").Append('\n')
            .Append("               CASE WHEN rolinherit THEN 'NOINHERIT' END) INTO attributes").Append('\n')
            .Append("    FROM pg_catalog.pg_roles WHERE rolname = ").Append(Literal(login)).Append(';').Append('\n')
            .Append("    IF attributes <> '' THEN").Append('\n')
            .Append("        RAISE EXCEPTION USING").Append('\n')
            .Append("            MESSAGE = ").Append(Literal(
                $"The role {login} exists, and is a superuser, may bypass row level security, may create roles, may replicate or has the privileges of the roles it is granted without switching to them: the hint says which. Whoever reaches its connection would not be held to the policies.")).Append(',').Append('\n')
            .Append("            HINT = ").Append(Literal($"ALTER ROLE {login} ")).Append(" || attributes || ';';").Append('\n')
            .Append("    END IF;").Append('\n')
            .Append("    SELECT pg_catalog.string_agg(DISTINCT member_of, ', ' ORDER BY member_of) INTO inherited").Append('\n')
            .Append("    FROM (SELECT m.roleid::pg_catalog.regrole::pg_catalog.text AS member_of FROM pg_catalog.pg_auth_members m").Append('\n')
            .Append("          WHERE m.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = ").Append(Literal(login)).Append(')').Append('\n')
            .Append("            AND coalesce((pg_catalog.to_jsonb(m) ->> 'inherit_option')::pg_catalog.bool, false)) inheriting;").Append('\n')
            .Append("    IF inherited IS NOT NULL THEN").Append('\n')
            .Append("        RAISE EXCEPTION USING").Append('\n')
            .Append("            MESSAGE = ").Append(Literal($"The role {login} has the privileges of ")).Append(" || inherited || ").Append(Literal(
                " without switching to them: they were granted while it inherited, and NOINHERIT holds only for the grants after it. Whoever reaches its connection would not be held to their policies.")).Append(',').Append('\n')
            .Append("            HINT = 'GRANT ' || inherited || ").Append(Literal($" TO {login} WITH INHERIT FALSE;")).Append(';').Append('\n')
            .Append("    END IF;").Append('\n');

        foreach (var role in granted)
        {
            if (role.Made)
            {
                // A role the access files make where a policy names it or the privileges are written: made here as
                // they make it, so the grant below finds it whatever the access files wrote, and checked by them.
                sql.Append(MadeWhereMissing(role.Role, role.Identifier));
            }
            else if (role.Role is not (PostgresRowLevelSecurityOptions.AuthenticatedRole or PostgresRowLevelSecurityOptions.AnonRole))
            {
                // A role of the project's own for its users or its anonymous callers, which nothing of the toolkit makes.
                sql
                    .Append("    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = ").Append(Literal(role.Role)).Append(") THEN").Append('\n')
                    .Append("        RAISE EXCEPTION USING MESSAGE = ").Append(Literal(
                        $"The role {role.Role}, which {role.What} runs as, does not exist. It is the project's own to make, in a migration before this one.")).Append(';').Append('\n')
                    .Append("    END IF;").Append('\n');
            }
        }

        sql
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .Append('\n');

        if (revoked.Count > 0)
        {
            sql
                .Append(LoginRevokeNote).Append(' ').Append(string.Join(", ", revoked)).Append('.').Append('\n')
                .Append("DO $ddd$").Append('\n')
                .Append("BEGIN").Append('\n');
            foreach (var role in revoked)
            {
                sql
                    .Append("    IF pg_catalog.to_regrole(").Append(Literal(role)).Append(") IS NOT NULL THEN").Append('\n')
                    .Append("        REVOKE ").Append(role).Append(" FROM ").Append(login).Append(';').Append('\n')
                    .Append("    END IF;").Append('\n');
            }

            sql
                .Append("END").Append('\n')
                .Append("$ddd$;").Append('\n')
                .Append('\n');
        }

        sql
            .Append("DO $ddd$").Append('\n')
            .Append("BEGIN").Append('\n');
        foreach (var role in granted)
        {
            sql.Append(GrantedWhereMissing(login, role));
        }

        return sql
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .ToString();
    }

    /// <summary>
    /// Statements of a <c>DO</c> block that make <paramref name="role"/>, written as <paramref name="identifier"/>,
    /// where the database does not have it, as a role that can neither log in nor have the privileges of the roles
    /// it is given. A migration of another database on the server may make it at the same moment, since roles are
    /// the server's: this one then finds it made rather than failing.
    /// </summary>
    private static string MadeWhereMissing(string role, string identifier)
        => new StringBuilder()
            .Append("    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = ").Append(Literal(role)).Append(") THEN").Append('\n')
            .Append("        BEGIN").Append('\n')
            .Append("            CREATE ROLE ").Append(identifier).Append(" NOLOGIN NOINHERIT;").Append('\n')
            .Append("        EXCEPTION WHEN duplicate_object OR unique_violation THEN").Append('\n')
            .Append("            NULL; -- made by a migration that ran at the same time").Append('\n')
            .Append("        END;").Append('\n')
            .Append("    END IF;").Append('\n')
            .ToString();

    /// <summary>
    /// Statements of a <c>DO</c> block that grant <paramref name="role"/> to <paramref name="login"/>, unless it may
    /// switch to it already: what <c>SET ROLE</c> asks, said per membership since Postgres 16 and by membership alone
    /// before it. Each role in a statement of its own, which is also how a later file reads back what this one
    /// granted. A migration of another database on the server may grant it at the same moment, since roles are the
    /// server's, and before Postgres 16 the slower of two such grants fails on the catalog's unique index: this one
    /// then finds it granted.
    /// </summary>
    private static string GrantedWhereMissing(string login, CallerRole role)
        => new StringBuilder()
            .Append("    IF NOT pg_catalog.pg_has_role(").Append(Literal(login)).Append("::pg_catalog.name, ").Append(Literal(role.Role)).Append("::pg_catalog.name,").Append('\n')
            .Append("            CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN").Append('\n')
            .Append("        BEGIN").Append('\n')
            .Append("            GRANT ").Append(role.Identifier).Append(" TO ").Append(login).Append(';').Append('\n')
            .Append("        EXCEPTION WHEN unique_violation THEN").Append('\n')
            .Append("            NULL; -- granted by a migration that ran at the same time").Append('\n')
            .Append("        END;").Append('\n')
            .Append("    END IF;").Append('\n')
            .ToString();

    /// <summary>
    /// The roles callers run as, each once, in the order a login role file grants them: the anonymous caller's, the
    /// signed-in user's, the scoped system role, the bookkeeping role where there is one, and the roles of the
    /// mapped token roles. A token role mapped to the user's role is that role.
    /// </summary>
    private static List<CallerRole> CallerRoles(RowAccessRoleNames roles)
    {
        List<CallerRole> callers =
        [
            new(roles.Anonymous, "anonymous", "a caller without a token", Made: false),
            new(roles.User, "user", "a signed-in user", Made: false),
            new(roles.SystemIn, "system-in", "the application's own work, inside the policies", Made: true),
        ];

        if (roles.System is { } system)
        {
            callers.Add(new(system, "system", "the toolkit's bookkeeping: the outbox, the inbox and which migrations ran", Made: true));
        }

        foreach (var mapped in roles.TokenRoles
                     .Where(pair => !string.Equals(pair.Value, roles.User, StringComparison.Ordinal))
                     .GroupBy(pair => pair.Value, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var tokenRoles = mapped.Select(pair => pair.Key).Order(StringComparer.Ordinal).ToList();
            callers.Add(new(
                mapped.Key,
                "token:" + tokenRoles[0],
                $"a signed-in user whose token carries the role {string.Join(" or ", tokenRoles)}",
                Made: true));
        }

        return [.. callers.DistinctBy(caller => caller.Role, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The roles the login role file <paramref name="sql"/> grants <paramref name="login"/>, as it spells them; none
    /// for no file. Read from its statements <c>GRANT … TO</c>, one for each role, where a name is quoted when it
    /// has to be, so a comma inside quotes is part of a name; a statement that names several is read whole.
    /// </summary>
    private static List<string> GrantedBy(string sql, string login)
    {
        var suffix = $" TO {login};";
        var roles = new List<string>();
        foreach (var line in sql.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("GRANT ", StringComparison.Ordinal) && line.EndsWith(suffix, StringComparison.Ordinal)))
        {
            var role = new StringBuilder();
            var quoted = false;
            foreach (var character in line["GRANT ".Length..^suffix.Length])
            {
                if (character == '"')
                {
                    quoted = !quoted;
                }

                if (character == ',' && !quoted)
                {
                    roles.Add(role.ToString().Trim());
                    role.Clear();
                    continue;
                }

                role.Append(character);
            }

            roles.Add(role.ToString().Trim());
        }

        return [.. roles.Where(name => name.Length > 0).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The role a login role file makes, or null for a file that is not one.</summary>
    private static string? LoginRoleOf(string path)
    {
        using var reader = new StreamReader(path);
        return LoginRoleHeader.Match(reader.ReadLine() ?? "") is { Success: true } match ? match.Groups["role"].Value : null;
    }

    /// <summary>
    /// A role as a statement names it: as it is where Postgres reads it so, and otherwise in double quotes, each
    /// quote inside doubled. A role callers run as may be any name a host configures.
    /// </summary>
    private static string Identifier(string role)
        => PlainIdentifier.IsMatch(role) && !Keywords.Contains(role) ? role : "\"" + role.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>A string literal, each quote inside doubled.</summary>
    private static string Literal(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>A role callers run as, as a login role file grants it.</summary>
    /// <param name="Role">The role, as the database has it.</param>
    /// <param name="Key">What maps it, as a key of <c>SupabaseRowAccessRoles</c>.</param>
    /// <param name="What">Who runs as it, as the file's comment says.</param>
    /// <param name="Made">Whether the toolkit makes it, as the access files do, rather than the project or Supabase.</param>
    private sealed record CallerRole(string Role, string Key, string What, bool Made)
    {
        /// <summary>The role as a statement names it.</summary>
        public string Identifier { get; } = SupabaseMigrations.Identifier(Role);
    }
}
