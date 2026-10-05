using System.Security.Cryptography;
using System.Text;
using DDDToolkit.Abstractions.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Postgres;

// What a script writes for a column rule: a rule that holds a change of some columns of a row, which a policy cannot
// see, since a policy is asked of a row and a command and never of a column. So it is a trigger, before an update
// of those columns, that asks the rule of the row as it was and as it is about to be, as a policy for UPDATE asks a
// rule with USING and WITH CHECK, and refuses the statement where the answer is no.
public static partial class PostgresRowAccess
{
    /// <summary>What the body of a column rule's trigger function stands between.</summary>
    private const string ColumnRuleBody = "$body$";

    /// <summary>The alias of the row as it was, in a trigger's function and its condition.</summary>
    private const string RowAsItWas = "OLD";

    /// <summary>The alias of the row as it is about to be.</summary>
    private const string RowAsItWillBe = "NEW";

    /// <summary>
    /// A trigger a script writes for column rules: on one table, for the columns the same rules hold, named
    /// <see cref="Name"/>, running <see cref="Function"/>, <c>schema.name</c>, which <see cref="Statements"/> make.
    /// </summary>
    private sealed record ColumnTrigger(StoreObjectIdentifier Table, string Name, string Function, IReadOnlyList<string> Columns, string Statements);

    /// <summary>
    /// The triggers of <paramref name="prepared"/>'s column rules: per table, one for each set of columns the same
    /// rules hold, so a statement that changes several of them asks those rules once. None for a context without
    /// column rules.
    /// </summary>
    /// <param name="prepared">The context's rules, with the roles of each.</param>
    /// <param name="contributed">The contributions' policies, whose roles for <c>UPDATE</c> are held as well.</param>
    /// <param name="writing">What the conditions are written with.</param>
    /// <exception cref="InvalidOperationException">
    /// A column rule holds a property the model maps to no column of the aggregate's table, or a name of the model
    /// or a rule would end the trigger function's body early.
    /// </exception>
    private static List<ColumnTrigger> ColumnTriggers(Prepared prepared, IReadOnlyList<Policy> contributed, Writing writing)
    {
        var columnRules = prepared.Rules.Where(rule => rule.IsColumnRule).Distinct().ToList();
        if (columnRules.Count == 0)
        {
            return [];
        }

        var context = prepared.Context;
        var held = columnRules
            .Select(rule =>
            {
                var root = RootOf(context, rule.AggregateTypeName);
                var table = TableOf(root);
                return (Rule: rule, Root: root, Table: table, Columns: ColumnsHeldBy(rule, root, table));
            })
            .ToList();

        // Per table, each column with the rules that hold it; the columns the same rules hold are one trigger.
        var sets = new List<(StoreObjectIdentifier Table, IReadOnlyList<string> Columns, IReadOnlyList<(RowAccessRule Rule, IEntityType Root)> Rules)>();
        foreach (var onTable in held.GroupBy(each => each.Table).OrderBy(group => group.Key.Schema ?? DefaultSchema, StringComparer.Ordinal).ThenBy(group => group.Key.Name, StringComparer.Ordinal))
        {
            var byColumn = onTable
                .SelectMany(each => each.Columns.Select(column => (Column: column, each.Rule, each.Root)))
                .GroupBy(each => each.Column, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();

            foreach (var same in byColumn.GroupBy(group => string.Join(",", group.Select(each => columnRules.IndexOf(each.Rule)).Distinct().Order()), StringComparer.Ordinal))
            {
                sets.Add((
                    onTable.Key,
                    [.. same.Select(group => group.Key)],
                    [.. same.First().Select(each => (each.Rule, each.Root)).DistinctBy(each => each.Rule).OrderBy(each => each.Rule.Name, StringComparer.Ordinal)]));
            }
        }

        // A name per set, and one with a hash of what it holds where two would be one, or one is no plain name.
        var names = sets.Select(set => ColumnTriggerName(set.Table, set.Columns, hashed: false)).ToList();
        var twice = names.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);

        return [.. sets.Select((set, index) =>
        {
            var name = twice.Contains(names[index]) ? ColumnTriggerName(set.Table, set.Columns, hashed: true) : names[index];
            var schema = FunctionSchema(context, context.Model.GetDefaultSchema() ?? set.Table.Schema ?? DefaultSchema, name);
            return ColumnTriggerOf(prepared, contributed, writing, set.Table, set.Columns, set.Rules, name, schema + "." + name);
        })];
    }

    /// <summary>
    /// The statements of <paramref name="triggers"/>, under a comment that says what they are; nothing for a
    /// context without column rules.
    /// </summary>
    private static string ColumnTriggerStatements(IReadOnlyList<ColumnTrigger> triggers, Writing writing)
    {
        if (triggers.Count == 0)
        {
            return string.Empty;
        }

        var statements = new StringBuilder()
            .Append('\n')
            .Append("-- The column rules: a policy cannot see which column a statement changes, so a trigger before an update of").Append('\n')
            .Append("-- the columns a rule holds asks it of the row as it was and as it is about to be. The policies for UPDATE").Append('\n')
            .Append("-- above still decide which rows a caller changes at all.").Append('\n');

        foreach (var trigger in triggers)
        {
            statements.Append(trigger.Statements);
        }

        return statements.ToString();
    }

    /// <summary>
    /// The trigger of the column rules <paramref name="rules"/> on <paramref name="columns"/> of
    /// <paramref name="table"/>: its function, which holds every role a caller's statement runs as and lets the
    /// application's own work and the tables' owner through, the trigger, and their comments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The roles held are those the rules are for, the user's, the anonymous caller's and every mapped token role's,
    /// and every other role a policy lets change a row of the table. A held role changes the columns when one of
    /// the rules for it allows the row as it was and one allows it as it is about to be, as Postgres asks the
    /// permissive policies for <c>UPDATE</c>; a held role none of the rules is for may not change them. The scoped
    /// system role and the bookkeeping role are the application's own work and are not held, unless a rule is
    /// for them, and neither is any other role: the tables' owner, and a role that may bypass row level security.
    /// </para>
    /// <para>
    /// A rule that reads the row through its key alone, or reads nothing of it, answers the same of the row before
    /// and after a change that keeps the key, which every change Entity Framework makes does: it is asked once per
    /// changed row then, rather than twice. That is once per row, not once per statement as in a policy, so a
    /// statement that changes many rows asks a set for each of them. The function runs as its caller, so the rules
    /// ask what a policy would ask, under the caller's own rights, and it has an empty search path: every name the
    /// toolkit writes is qualified, and a function a rule calls with <c>Sql.Call</c> names its schema, which the
    /// generator holds it to; a name in its <c>Sql.Raw</c> has to as well. Where the table holds a hierarchy and
    /// no rule is about all of its rows, the trigger fires for the rows of the rules' types alone.
    /// </para>
    /// </remarks>
    private static ColumnTrigger ColumnTriggerOf(
        Prepared prepared,
        IReadOnlyList<Policy> contributed,
        Writing writing,
        StoreObjectIdentifier table,
        IReadOnlyList<string> columns,
        IReadOnlyList<(RowAccessRule Rule, IEntityType Root)> rules,
        string name,
        string function)
    {
        var context = prepared.Context;
        var sql = writing.Sql;
        var names = writing.Roles;
        var qualified = sql.DelimitIdentifier(table.Name, table.Schema ?? DefaultSchema);

        // Who may change a row of the table at all: the roles of the rules for Change about it, and those of the
        // contributions' permissive policies for UPDATE on it.
        var changers = prepared.Rules
            .Where(rule => !rule.IsColumnRule && rule.Operations.HasFlag(RowOperations.Change) && TableOf(RootOf(context, rule.AggregateTypeName)) == table)
            .SelectMany(rule => prepared.Roles[rule])
            .Concat(contributed.Where(policy => !policy.Restrictive && policy.Command == "UPDATE" && policy.Table == table).Select(policy => policy.Role));
        string?[] systemWork = [names.SystemIn, names.System];
        var held = new[] { names.User, names.Anonymous }
            .Concat(names.TokenDatabaseRoles)
            .Concat(changers)
            .Where(role => !systemWork.Contains(role, StringComparer.Ordinal))
            .Concat(rules.SelectMany(each => prepared.Roles[each.Rule]))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var changed = Listed(columns.Select(Quote), "or");
        var key = rules[0].Root.FindPrimaryKey()!.Properties.Select(property => Quote(property.GetColumnName(table)!)).ToList();

        // Each held role with the rules for it, the roles the same rules are for written as one branch, which asks
        // those rules and names them. Rules that ask the same SQL for different roles stay apart, so a refusal
        // names the rules that were asked of the caller.
        var branches = held
            .Select(role => (Role: role, For: Enumerable.Range(0, rules.Count).Where(index => prepared.Roles[rules[index].Rule].Contains(role, StringComparer.Ordinal)).ToList()))
            .GroupBy(each => string.Join(",", each.For), StringComparer.Ordinal)
            .Select(group => (Roles: group.Select(each => each.Role).ToList(), Rules: group.First().For.Select(index => rules[index]).ToList()))
            .OrderBy(branch => branch.Rules.Count == 0 ? 1 : 0)
            .ThenBy(branch => branch.Roles[0], StringComparer.Ordinal)
            .ToList();

        var body = new StringBuilder()
            .Append("CREATE OR REPLACE FUNCTION ").Append(function).Append("() RETURNS trigger").Append('\n')
            .Append("    LANGUAGE plpgsql SET search_path = '' AS ").Append(ColumnRuleBody).Append('\n')
            .Append("BEGIN").Append('\n')
            .Append("    -- The roles a caller's statement runs as are held; the application's own work and the tables' owner are not.").Append('\n');

        for (var i = 0; i < branches.Count; i++)
        {
            var roles = branches[i].Roles;
            body.Append(i == 0 ? "    IF " : "    ELSIF ")
                .Append(roles.Count == 1 ? "CURRENT_USER = " + Literal(roles[0]) : "CURRENT_USER IN (" + string.Join(", ", roles.Select(Literal)) + ")")
                .Append(" THEN").Append('\n');

            if (branches[i].Rules.Count == 0)
            {
                body.Append("        ").Append(Raise($"No column rule is for this caller's role, so it may not change {changed} of {qualified}.")).Append('\n');
            }
            else
            {
                var named = branches[i].Rules.Select(each => "'" + each.Rule.Name + "'").ToList();
                body.Append("        IF ").Append(Refusal(branches[i].Rules)).Append(" THEN").Append('\n')
                    .Append("            ").Append(Raise(named.Count == 1
                        ? $"The column rule {named[0]} does not let this caller change {changed} of {qualified}."
                        : $"None of the column rules {Listed(named, "and")} lets this caller change {changed} of {qualified}.")).Append('\n')
                    .Append("        END IF;").Append('\n');
            }
        }

        if (branches.Count > 0)
        {
            body.Append("    END IF;").Append('\n');
        }

        body.Append("    RETURN NEW;").Append('\n')
            .Append("END").Append('\n')
            .Append(ColumnRuleBody);

        var written = body.ToString();
        if (written.Split(ColumnRuleBody).Length != 3)
        {
            throw new InvalidOperationException(
                $"The trigger of the column rules on {table.DisplayName()} cannot be written: a name of the model, a role or what {Listed(rules.Select(each => "the rule '" + each.Rule.Name + "'"), "or")} asks has '{ColumnRuleBody}' in it, which would end the function's body early.");
        }

        // The rows the rules are about: every row, unless the table holds a hierarchy and no rule is about all of it.
        var covered = rules.Select(each => TypeCondition(each.Root, table, null)).Any(condition => condition is null)
            ? null
            : "(" + string.Join(" OR ", rules.SelectMany(each => new[] { TypeCondition(each.Root, table, RowAsItWas)!, TypeCondition(each.Root, table, RowAsItWillBe)! }).Distinct(StringComparer.Ordinal)) + ")";
        var distinct = columns.Count == 1
            ? $"{RowAsItWas}.{Quote(columns[0])} IS DISTINCT FROM {RowAsItWillBe}.{Quote(columns[0])}"
            : $"({string.Join(", ", columns.Select(column => RowAsItWas + "." + Quote(column)))}) IS DISTINCT FROM ({string.Join(", ", columns.Select(column => RowAsItWillBe + "." + Quote(column)))})";

        var statements = new StringBuilder()
            .Append('\n')
            .Append("-- A change of ").Append(Commented(changed)).Append(" of ").Append(Commented(qualified)).Append(" is held to ")
            .Append(rules.Count == 1 ? "the column rule '" + Commented(rules[0].Rule.Name) + "'." : "the column rules " + Commented(Listed(rules.Select(each => "'" + each.Rule.Name + "'"), "and")) + ": a change one of them allows is allowed.").Append('\n')
            .Append(written).Append(";\n")
            .Append("COMMENT ON FUNCTION ").Append(function).Append("() IS ").Append(Literal(FunctionComment(context))).Append(";\n")
            .Append("REVOKE ALL ON FUNCTION ").Append(function).Append("() FROM PUBLIC;\n")
            .Append("DROP TRIGGER IF EXISTS ").Append(name).Append(" ON ").Append(qualified).Append(";\n")
            .Append("CREATE TRIGGER ").Append(name).Append(" BEFORE UPDATE OF ").Append(string.Join(", ", columns.Select(Quote))).Append(" ON ").Append(qualified).Append('\n')
            .Append("    FOR EACH ROW WHEN (").Append(covered is null ? distinct : "(" + distinct + ") AND " + covered).Append(")\n")
            .Append("    EXECUTE FUNCTION ").Append(function).Append("();\n")
            .Append("COMMENT ON TRIGGER ").Append(name).Append(" ON ").Append(qualified).Append(" IS ").Append(Literal(ColumnRuleComment)).Append(";\n");

        return new ColumnTrigger(table, name, function, columns, statements.ToString());

        // What refuses a change for a role the rules of <for> are for; empty for a role none of them is for.
        string Refusal(IReadOnlyList<(RowAccessRule Rule, IEntityType Root)> @for)
        {
            if (@for.Count == 0)
            {
                return string.Empty;
            }

            var asWas = new List<string>();
            var asWillBe = new List<string>();
            var beyondTheKey = false;
            foreach (var (rule, root) in @for)
            {
                var what = $"the column rule '{rule.Name}'";
                var before = new Filler(what, root, table, writing, RowAsItWas);
                asWas.Add(OfItsType(root, table, before.Fill(rule.Sql), RowAsItWas));
                var after = new Filler(what, root, table, writing, RowAsItWillBe);
                asWillBe.Add(OfItsType(root, table, after.Fill(rule.Sql), RowAsItWillBe));
                beyondTheKey |= before.ReadsBeyondTheKey || TypeCondition(root, table, null) is not null;
            }

            var was = Parenthesized(AnyOf(asWas)!) + " IS NOT TRUE";
            var willBe = Parenthesized(AnyOf(asWillBe)!) + " IS NOT TRUE";
            if (string.Equals(was, willBe, StringComparison.Ordinal))
            {
                // Nothing of the row is read: one question.
                return was;
            }

            if (!beyondTheKey && key.Count > 0)
            {
                // The key alone is read, so the row as it will be is asked only where the key changes.
                var keyChanged = key.Count == 1
                    ? $"{RowAsItWas}.{key[0]} IS DISTINCT FROM {RowAsItWillBe}.{key[0]}"
                    : $"({string.Join(", ", key.Select(column => RowAsItWas + "." + column))}) IS DISTINCT FROM ({string.Join(", ", key.Select(column => RowAsItWillBe + "." + column))})";
                return $"{was} OR ({keyChanged} AND {willBe})";
            }

            return $"{was} OR {willBe}";
        }

        string Raise(string message)
            => $"RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = {Literal(name)}, MESSAGE = {Literal(message)};";
    }

    /// <summary>
    /// The name of the trigger on <paramref name="columns"/> of <paramref name="table"/>, and of its function:
    /// <c>projects_state_column_rule</c>, the table and the columns in lower case. Where they are not plain
    /// letters, digits and underscores, the name would be longer than Postgres keeps, or two triggers of a script
    /// would share it (<paramref name="hashed"/>), a hash of the table and the columns follows what fits.
    /// </summary>
    private static string ColumnTriggerName(StoreObjectIdentifier table, IReadOnlyList<string> columns, bool hashed)
    {
        var written = (table.Name + "_" + string.Join("_", columns) + "_column_rule").ToLowerInvariant();
        var plain = new string([.. written.Select(static character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' ? character : '_')]);
        if (!hashed && plain.Length <= MaxIdentifierBytes && plain[0] is (>= 'a' and <= 'z') or '_' && string.Equals(plain, written, StringComparison.Ordinal))
        {
            return plain;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes((table.Schema ?? DefaultSchema) + "." + table.Name + ":" + string.Join(",", columns))))[..8];
        var start = plain.TrimStart('_', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        start = start[..Math.Min(start.Length, MaxIdentifierBytes - hash.Length - 1)].TrimEnd('_');
        return (start.Length == 0 ? "column_rule" : start) + "_" + hash;
    }

    /// <summary>
    /// The columns of <paramref name="table"/> that <paramref name="rule"/>'s properties are stored in, each once:
    /// a property's own, and every column of a value object stored in the row.
    /// </summary>
    /// <exception cref="InvalidOperationException">A property is stored in no column of the table.</exception>
    private static List<string> ColumnsHeldBy(RowAccessRule rule, IEntityType root, StoreObjectIdentifier table)
    {
        var columns = new List<string>();
        foreach (var path in rule.Columns)
        {
            var found = ColumnsAt(root, table, path);
            if (found.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The column rule '{rule.Name}' holds {root.ClrType.Name}.{path}, which the Entity Framework model maps to no column of {table.DisplayName()}. A column rule holds a property stored in the aggregate's own row, or a value object stored there.");
            }

            columns.AddRange(found);
        }

        return [.. columns.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The columns of <paramref name="table"/> the property at <paramref name="path"/> is stored in: one for a
    /// property, every one of a value object stored in the row, through value objects stored there; none for a
    /// property stored elsewhere or nowhere.
    /// </summary>
    private static List<string> ColumnsAt(IEntityType root, StoreObjectIdentifier table, string path)
    {
        ITypeBase type = root;
        var names = path.Split('.');
        for (var i = 0; i < names.Length; i++)
        {
            if (type.FindProperty(names[i]) is { } property)
            {
                return i == names.Length - 1 && property.GetColumnName(table) is { } column ? [column] : [];
            }

            if (type.FindComplexProperty(names[i]) is { } complex)
            {
                type = complex.ComplexType;
                continue;
            }

            if (type is IEntityType owner
                && owner.FindNavigation(names[i]) is { IsCollection: false, ForeignKey.IsOwnership: true } owned
                && owned.TargetEntityType.GetTableName() == table.Name
                && owned.TargetEntityType.GetSchema() == table.Schema)
            {
                type = owned.TargetEntityType;
                continue;
            }

            return [];
        }

        return ColumnsOf(type, table);
    }

    /// <summary>
    /// Every column of <paramref name="table"/> a value object stored there is stored in: its properties', but its
    /// key's, which are the row's own, those of the value objects inside it, or the one column it is stored in as
    /// JSON.
    /// </summary>
    private static List<string> ColumnsOf(ITypeBase type, StoreObjectIdentifier table)
    {
        if (type.IsMappedToJson())
        {
            return type.GetContainerColumnName() is { } json ? [json] : [];
        }

        var columns = type.GetProperties()
            .Where(property => !(property is { DeclaringType: IEntityType } && property.IsPrimaryKey()))
            .Select(property => property.GetColumnName(table))
            .OfType<string>()
            .ToList();

        foreach (var complex in type.GetComplexProperties())
        {
            columns.AddRange(ColumnsOf(complex.ComplexType, table));
        }

        if (type is IEntityType entity)
        {
            foreach (var owned in entity.GetNavigations().Where(navigation => navigation is { IsCollection: false, ForeignKey.IsOwnership: true }
                         && navigation.TargetEntityType.GetTableName() == table.Name && navigation.TargetEntityType.GetSchema() == table.Schema))
            {
                columns.AddRange(ColumnsOf(owned.TargetEntityType, table));
            }
        }

        return columns;
    }

    /// <summary><c>a</c>, <c>a or b</c>, <c>a, b or c</c>, with <paramref name="last"/> before the last one.</summary>
    private static string Listed(IEnumerable<string> items, string last)
    {
        var all = items.ToList();
        return all.Count < 2 ? string.Concat(all) : string.Join(", ", all.Take(all.Count - 1)) + " " + last + " " + all[^1];
    }
}
