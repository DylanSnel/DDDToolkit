using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
/// </remarks>
public static class PostgresRowAccess
{
    /// <summary>The comment on every policy made from a rule, which is how the next script finds it to drop.</summary>
    public const string PolicyComment = "DDDToolkit row access rule";

    /// <summary>The schema of a table the model gives none.</summary>
    public const string DefaultSchema = "public";

    private const int MaxIdentifierBytes = 63;

    /// <summary>
    /// What this context's rules are now: every policy an earlier script made on its tables dropped, and
    /// the policies of <paramref name="rules"/> that guard an aggregate it maps made again.
    /// </summary>
    /// <param name="context">The context whose model says which tables and columns the rules read.</param>
    /// <param name="rules">The rules; those of aggregates other contexts map are left out.</param>
    /// <param name="functions">How a policy asks about the caller; <see cref="PostgresCallerFunctions.Toolkit"/> when left out.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="rules"/> is null.</exception>
    public static string Script(DbContext context, IEnumerable<RowAccessRule> rules, PostgresCallerFunctions? functions = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rules);

        return DropStatement(context) + CreateStatements(context, RulesOf(context, rules), functions);
    }

    /// <summary>
    /// What row level security needs on a Postgres that is not Supabase's, where every project has it
    /// already: the roles a caller's queries run as, which the application's login role may switch to,
    /// and the <c>ddd.caller_id()</c>, <c>ddd.caller_role()</c> and <c>ddd.caller_claims()</c> functions
    /// policies ask about the caller. It can run again, so it can go in the first migration and stay there.
    /// </summary>
    /// <param name="options">The roles; PostgREST's, <c>anon</c> and <c>authenticated</c>, when left out.</param>
    /// <param name="loginRole">
    /// The role the application logs in as, which gets to switch to the two; the role running the script
    /// when left out, which is right when the application migrates its own database.
    /// </param>
    /// <remarks>
    /// The tables still need the privileges a caller's queries use, <c>GRANT SELECT, INSERT, UPDATE, DELETE</c>
    /// to both roles, as they would for PostgREST; the policies then decide which rows.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="loginRole"/> is empty, or a role in <paramref name="options"/> is.</exception>
    public static string SetupScript(PostgresRowLevelSecurityOptions? options = null, string? loginRole = null)
    {
        options ??= new PostgresRowLevelSecurityOptions();
        options.Validate();
        if (loginRole is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(loginRole);
        }

        var roles = new[] { options.AnonymousRole, options.UserRole }.Distinct(StringComparer.Ordinal).ToList();
        var quoted = string.Join(", ", roles.Select(Quote));

        var script = new StringBuilder()
            .Append("-- What row level security needs on a Postgres that is not Supabase's: the roles a caller's queries").Append('\n')
            .Append("-- run as, and the functions a policy asks about the caller. Written by DDDToolkit; it can run again.").Append('\n')
            .Append("DO $ddd$").Append('\n')
            .Append("BEGIN").Append('\n');

        foreach (var role in roles)
        {
            script
                .Append("    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = ").Append(Literal(role)).Append(") THEN").Append('\n')
                .Append("        CREATE ROLE ").Append(Quote(role)).Append(" NOLOGIN NOINHERIT;").Append('\n')
                .Append("    END IF;").Append('\n');
        }

        return script
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .Append("GRANT ").Append(quoted).Append(" TO ").Append(loginRole is null ? "CURRENT_USER" : Quote(loginRole)).Append(';').Append('\n')
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
            .ToString();
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
    /// A statement that drops every policy made from a rule on this context's tables. The Supabase export
    /// puts it at the start of every migration of a module with rules too, because a policy stands in the
    /// way of dropping a column it reads or changing its type; the script after it makes them again.
    /// Empty for a context without tables.
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

        return new StringBuilder()
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
            .Append("END").Append('\n')
            .Append("$ddd$;").Append('\n')
            .ToString();
    }

    /// <summary>
    /// The statements that make every rule's policies, and the policies of the aggregates' entities, which
    /// follow their root. <paramref name="rules"/> are rules of aggregates this context maps; see <see cref="RulesOf"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="rules"/> is null.</exception>
    public static string CreateStatements(DbContext context, IReadOnlyList<RowAccessRule> rules, PostgresCallerFunctions? functions = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rules);
        functions ??= PostgresCallerFunctions.Toolkit;

        var sql = context.GetService<ISqlGenerationHelper>();
        var statements = new StringBuilder();

        foreach (var aggregate in rules.GroupBy(rule => rule.AggregateTypeName, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var root = context.Model.GetEntityTypes().Single(entity => entity.ClrType.FullName == aggregate.Key && !entity.IsOwned());
            var table = TableOf(root);
            var qualified = sql.DelimitIdentifier(table.Name, table.Schema ?? DefaultSchema);

            statements.Append('\n').Append("ALTER TABLE ").Append(qualified).Append(" ENABLE ROW LEVEL SECURITY;").Append('\n');

            foreach (var rule in aggregate.OrderBy(rule => rule.Name, StringComparer.Ordinal))
            {
                var condition = Fill(rule, root, table, functions);
                foreach (var (command, suffix) in CommandsOf(rule.Operations))
                {
                    var name = PolicyName(rule.Name + suffix);
                    statements.Append('\n')
                        .Append("CREATE POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(qualified)
                        .Append(" FOR ").Append(command).Append(" TO ").Append(RolesOf(rule.Roles)).Append('\n')
                        .Append(Clauses(command, condition)).Append(";\n")
                        .Append("COMMENT ON POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(qualified)
                        .Append(" IS ").Append(Literal(PolicyComment)).Append(";\n");
                }
            }

            var roles = RolesOf(aggregate.SelectMany(rule => rule.Roles).ToList());
            foreach (var (child, parent, key) in EntitiesOf(root, table))
            {
                var childTable = sql.DelimitIdentifier(child.Name, child.Schema ?? DefaultSchema);
                var name = PolicyName($"{child.Name} goes with its {parent.Name}");
                var follows = $"(EXISTS (SELECT 1 FROM {sql.DelimitIdentifier(parent.Name, parent.Schema ?? DefaultSchema)} parent WHERE "
                    + string.Join(" AND ", key.Select(column => $"parent.{sql.DelimitIdentifier(column.Parent)} = {sql.DelimitIdentifier(child.Name)}.{sql.DelimitIdentifier(column.Child)}"))
                    + "))";

                statements.Append('\n')
                    .Append("-- ").Append(child.Name).Append(" belongs to the aggregate, and is seen and changed with it.").Append('\n')
                    .Append("ALTER TABLE ").Append(childTable).Append(" ENABLE ROW LEVEL SECURITY;").Append('\n')
                    .Append("CREATE POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(childTable)
                    .Append(" FOR ALL TO ").Append(roles).Append('\n')
                    .Append("    USING ").Append(follows).Append('\n')
                    .Append("    WITH CHECK ").Append(follows).Append(";\n")
                    .Append("COMMENT ON POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(childTable)
                    .Append(" IS ").Append(Literal(PolicyComment)).Append(";\n");
            }
        }

        return statements.ToString();
    }

    /// <summary>
    /// Every table of an aggregate's entities that is not the table of the one it belongs to, with that
    /// one and the columns that tie them: a line to its order, a line's parts to the line.
    /// </summary>
    private static IEnumerable<(StoreObjectIdentifier Child, StoreObjectIdentifier Parent, List<(string Parent, string Child)> Key)> EntitiesOf(IEntityType owner, StoreObjectIdentifier ownerTable)
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
                foreach (var inner in EntitiesOf(target, ownerTable))
                {
                    yield return inner;
                }

                continue;
            }

            var key = navigation.ForeignKey.PrincipalKey.Properties
                .Zip(navigation.ForeignKey.Properties, (principal, dependent) => (principal.GetColumnName(ownerTable)!, dependent.GetColumnName(table)!))
                .ToList();

            yield return (table, ownerTable, key);

            foreach (var inner in EntitiesOf(target, table))
            {
                yield return inner;
            }
        }
    }

    private static List<StoreObjectIdentifier> TablesOf(DbContext context)
        => [.. context.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null)
            .Select(TableOf)
            .Distinct()
            .OrderBy(table => table.Schema ?? DefaultSchema, StringComparer.Ordinal)
            .ThenBy(table => table.Name, StringComparer.Ordinal)];

    private static StoreObjectIdentifier TableOf(IEntityType entity)
        => StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

    private static IEnumerable<(string Command, string Suffix)> CommandsOf(RowOperations operations)
    {
        if (operations == RowOperations.All)
        {
            yield return ("ALL", "");
            yield break;
        }

        var single = operations is RowOperations.Read or RowOperations.Create or RowOperations.Change or RowOperations.Remove;
        if (operations.HasFlag(RowOperations.Read))
        {
            yield return ("SELECT", single ? "" : " (read)");
        }

        if (operations.HasFlag(RowOperations.Create))
        {
            yield return ("INSERT", single ? "" : " (create)");
        }

        if (operations.HasFlag(RowOperations.Change))
        {
            yield return ("UPDATE", single ? "" : " (change)");
        }

        if (operations.HasFlag(RowOperations.Remove))
        {
            yield return ("DELETE", single ? "" : " (remove)");
        }
    }

    /// <summary>
    /// What a policy for <paramref name="command"/> checks: the rows it lets a caller see or touch, and the
    /// rows it lets a caller leave behind.
    /// </summary>
    private static string Clauses(string command, string condition)
    {
        // Postgres wants the condition in parentheses, and a rule that is one column, "IsPublic", has none.
        var parenthesized = condition.StartsWith('(') && Balanced(condition) ? condition : "(" + condition + ")";
        return command switch
        {
            "INSERT" => "    WITH CHECK " + parenthesized,
            "SELECT" or "DELETE" => "    USING " + parenthesized,
            _ => "    USING " + parenthesized + "\n    WITH CHECK " + parenthesized,
        };
    }

    /// <summary>Whether the whole condition is one pair of parentheses, rather than two groups side by side.</summary>
    private static bool Balanced(string condition)
    {
        var depth = 0;
        var quoted = false;
        for (var i = 0; i < condition.Length; i++)
        {
            var character = condition[i];
            if (character == '\'' || character == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && character == '(')
            {
                depth++;
            }
            else if (!quoted && character == ')' && --depth == 0 && i < condition.Length - 1)
            {
                return false;
            }
        }

        return depth == 0;
    }

    private static string RolesOf(IReadOnlyCollection<string> roles)
        => roles.Count == 0
            ? PostgresRowLevelSecurityOptions.AnonRole + ", " + PostgresRowLevelSecurityOptions.AuthenticatedRole
            : string.Join(", ", roles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

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

    /// <summary>The rule's SQL, with every <c>{...}</c> filled in and every <c>{{</c> and <c>}}</c> a brace again.</summary>
    private static string Fill(RowAccessRule rule, IEntityType root, StoreObjectIdentifier table, PostgresCallerFunctions functions)
    {
        var template = rule.Sql;
        var sql = new StringBuilder(template.Length * 2);

        for (var i = 0; i < template.Length; i++)
        {
            var character = template[i];
            if ((character is '{' or '}') && i + 1 < template.Length && template[i + 1] == character)
            {
                sql.Append(character);
                i++;
            }
            else if (character == '{')
            {
                var end = template.IndexOf('}', i);
                if (end < 0)
                {
                    throw new InvalidOperationException($"The SQL of the rule '{rule.Name}' has a '{{' without its '}}': {template}");
                }

                sql.Append(Token(rule, root, table, template[(i + 1)..end], functions));
                i = end;
            }
            else
            {
                sql.Append(character);
            }
        }

        return sql.ToString();
    }

    private static string Token(RowAccessRule rule, IEntityType root, StoreObjectIdentifier table, string token, PostgresCallerFunctions functions)
    {
        var parts = token.Split(':', 3);
        switch (parts[0])
        {
            case "col" when parts.Length == 2:
                return Quote(PropertyOf(rule, root, parts[1]).GetColumnName(table) ?? throw NotMapped(rule, root, parts[1], table));
            case "val" when parts.Length == 3:
                return Stored(PropertyOf(rule, root, parts[1]), parts[2]);
            case "caller" when parts.Length >= 2:
                return parts[1] switch
                {
                    "uid" => $"(SELECT {functions.UserId})",
                    "signedin" => $"((SELECT {functions.UserId}) IS NOT NULL)",
                    "role" => $"(SELECT {functions.Role})",
                    "claim" when parts.Length == 3 => Claim(functions, parts[2]),
                    _ => throw Unknown(rule, token),
                };
            default:
                throw Unknown(rule, token);
        }
    }

    /// <summary><c>ddd.caller_claims() -&gt;&gt; 'email'</c> for a claim, <c>ddd.caller_claims() #&gt;&gt; '{app_metadata,role}'</c> for a path into one.</summary>
    private static string Claim(PostgresCallerFunctions functions, string path)
        => path.Contains('.', StringComparison.Ordinal)
            ? $"(SELECT {functions.Claims} #>> {Literal("{" + path.Replace('.', ',') + "}")})"
            : $"(SELECT {functions.Claims} ->> {Literal(path)})";

    /// <summary>The property at <paramref name="path"/>: <c>Status</c>, or <c>Amount.Amount</c> through a value object stored inline.</summary>
    private static IProperty PropertyOf(RowAccessRule rule, IEntityType root, string path)
    {
        ITypeBase type = root;
        var names = path.Split('.');

        for (var i = 0; i < names.Length - 1; i++)
        {
            if (type.FindComplexProperty(names[i]) is { } complex)
            {
                type = complex.ComplexType;
                continue;
            }

            if (type is IEntityType entity
                && entity.FindNavigation(names[i]) is { ForeignKey.IsOwnership: true } owned
                && TableOf(owned.TargetEntityType) == TableOf(root))
            {
                type = owned.TargetEntityType;
                continue;
            }

            throw new InvalidOperationException(
                $"The rule '{rule.Name}' reads {root.ClrType.Name}.{path}, and {names[i]} is not a value object stored in {root.ClrType.Name}'s own table.");
        }

        return type.FindProperty(names[^1])
            ?? throw new InvalidOperationException($"The rule '{rule.Name}' reads {root.ClrType.Name}.{path}, which the Entity Framework model does not map to a column.");
    }

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

    private static InvalidOperationException NotMapped(RowAccessRule rule, IEntityType root, string path, StoreObjectIdentifier table)
        => new($"The rule '{rule.Name}' reads {root.ClrType.Name}.{path}, which has no column in {table.DisplayName()}.");

    private static InvalidOperationException Unknown(RowAccessRule rule, string token)
        => new($"The SQL of the rule '{rule.Name}' asks for '{{{token}}}', which this version of DDDToolkit.EntityFramework.Postgres does not know. Use the same version of the toolkit's packages everywhere.");

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Literal(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
}
