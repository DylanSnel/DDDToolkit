using System.Text;
using DDDToolkit.Abstractions.Attributes;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Postgres;

public static partial class PostgresRowAccess
{
    /// <summary>
    /// The commands a restrictive rule narrows on an entity's table, or a table that follows the aggregate, with the
    /// operations of the rule that narrow each: changing the aggregate is adding, changing and removing the rows of its
    /// entities, as it is for a rule that allows.
    /// </summary>
    private static readonly (string Command, RowOperations Operations)[] FollowingCommands =
    [
        ("SELECT", RowOperations.Read),
        ("INSERT", RowOperations.Create | RowOperations.Change),
        ("UPDATE", RowOperations.Change),
        ("DELETE", RowOperations.Remove | RowOperations.Change),
    ];

    /// <summary>
    /// The policies of the restrictive rules: for each rule, each role it is for and each command it narrows, a
    /// <c>RESTRICTIVE</c> policy on the aggregate's table, on the tables of its entities, and on the tables a
    /// contribution says follow the aggregate. Postgres lets a row through only where a permissive policy allows it
    /// and every restrictive one does too, so these never allow anything; they hold back the roles they are for
    /// and nobody else, so a package's own work, and its functions that run as their owner, are not held back.
    /// </summary>
    /// <remarks>
    /// On the aggregate's table the condition is asked of the row as it is. On an entity's table, a condition that
    /// reads the aggregate's row is asked of the aggregate's row it belongs to, under the caller's own policies; one
    /// that reads nothing of the row, a question about the caller alone, is asked as it is, so it holds alike on every
    /// table. A table that follows the aggregate without being one of its entities in the model, such as the closure
    /// of an organization's tree, has no row of the aggregate to ask about: a rule that reads the row is refused there.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A restrictive rule narrows a table nothing lets anybody use, or reads the aggregate's row where a table follows
    /// the aggregate.
    /// </exception>
    private static string RestrictiveRuleStatements(Prepared prepared, Writing writing, HashSet<StoreObjectIdentifier> secured, PolicyNames policyNames)
    {
        var rules = prepared.Rules.Where(rule => rule.Restrictive)
            .Distinct()
            .OrderBy(rule => rule.AggregateTypeName, StringComparer.Ordinal)
            .ThenBy(rule => rule.Name, StringComparer.Ordinal)
            .ToList();
        if (rules.Count == 0)
        {
            return string.Empty;
        }

        var context = prepared.Context;
        var sql = writing.Sql;
        var following = prepared.Contributions
            .SelectMany(contributed => (contributed.Result.Following ?? new Dictionary<IEntityType, IEntityType>()).Select(pair => (Table: TableOf(pair.Key), Aggregate: pair.Value)))
            .ToList();

        var statements = new StringBuilder();
        foreach (var rule in rules)
        {
            var root = RootOf(context, rule.AggregateTypeName);
            var table = TableOf(root);
            var filler = new Filler($"the rule '{rule.Name}'", root, table, writing, rootAlias: null);
            var condition = OfItsType(root, table, filler.Fill(rule.Sql), alias: null);
            var readsTheRow = filler.ReadsTheRow;

            var narrowed = new List<(StoreObjectIdentifier Table, IReadOnlyList<(string Command, string Condition)> Commands)>
            {
                (table, [.. RootCommands.Where(each => rule.Operations.HasFlag(each.Operation)).Select(each => (each.Command, Condition: condition))]),
            };

            foreach (var (entity, chain, _) in EntitiesOf(root, table, []))
            {
                var aboutTheRoot = readsTheRow ? Written(entity, chain, Condition(root, table, rule, writing, RootAlias), sql)! : condition;
                narrowed.Add((entity, [.. FollowingCommands.Where(each => (rule.Operations & each.Operations) != 0).Select(each => (each.Command, Condition: aboutTheRoot))]));
            }

            foreach (var follower in following.Where(each => each.Aggregate == root).Select(each => each.Table).Distinct())
            {
                if (readsTheRow)
                {
                    throw new InvalidOperationException(
                        $"The restrictive rule '{rule.Name}' reads a row of {root.ClrType.Name}, and {follower.DisplayName()} follows {root.ClrType.Name} without being one of its entities in the model, " +
                        $"so it has no row of {root.ClrType.Name} to ask about. Say it about the caller alone, as TenancyRowAccess.CallerSeat<TSeat>() does, or narrow {root.ClrType.Name}'s own table with a rule of its own.");
                }

                narrowed.Add((follower, [.. FollowingCommands.Where(each => (rule.Operations & each.Operations) != 0).Select(each => (each.Command, Condition: condition))]));
            }

            foreach (var (onTable, commands) in narrowed)
            {
                if (!secured.Contains(onTable))
                {
                    throw new InvalidOperationException(
                        $"The restrictive rule '{rule.Name}' narrows {onTable.DisplayName()}, which no rule and no row access contribution lets anybody use, so there is nothing for it to narrow: " +
                        "a restrictive rule only takes away from what a permissive one allows. Give the aggregate a rule that allows, without Restrictive, or leave the rule out.");
                }

                var qualified = QualifiedTable(onTable, sql);
                foreach (var (command, about) in commands)
                {
                    foreach (var role in prepared.Roles[rule])
                    {
                        var name = policyNames.Claim(onTable, PolicyName($"{rule.Name} ({command.ToLowerInvariant()}) for {role}"), $"the restrictive rule '{rule.Name}' for {command} to {role}");
                        var clauses = command switch
                        {
                            "INSERT" => "    WITH CHECK " + Parenthesized(about),
                            "UPDATE" => "    USING " + Parenthesized(about) + "\n    WITH CHECK " + Parenthesized(about),
                            _ => "    USING " + Parenthesized(about),
                        };

                        statements.Append('\n')
                            .Append("-- ").Append(Commented(name)).Append(" is the restrictive rule '").Append(Commented(rule.Name)).Append("'")
                            .Append(onTable == table ? "" : " of " + Commented(table.Name))
                            .Append(": a row passes only where it allows it as well as a permissive policy, so it never allows more.").Append('\n')
                            .Append("CREATE POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(qualified)
                            .Append(" AS RESTRICTIVE FOR ").Append(command).Append(" TO ").Append(sql.DelimitIdentifier(role)).Append('\n')
                            .Append(clauses).Append(";\n")
                            .Append("COMMENT ON POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(qualified)
                            .Append(" IS ").Append(Literal(PolicyComment)).Append(";\n");
                    }
                }
            }
        }

        return statements.ToString();
    }
}
