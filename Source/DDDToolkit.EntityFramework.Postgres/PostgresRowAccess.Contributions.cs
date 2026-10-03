using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDDToolkit.EntityFramework.Postgres;

// What a script writes besides the rules' policies: the functions and policies of the contributions, the order
// the functions are created in, within a script and between the scripts of several contexts, and who may
// execute each function.
public static partial class PostgresRowAccess
{
    /// <summary>The commands a policy is for, in the order a script writes them.</summary>
    private static readonly string[] PolicyCommands = ["SELECT", "INSERT", "UPDATE", "DELETE"];

    /// <summary>
    /// Everything one context's script is written from: its rules with the roles of their policies, its access
    /// functions, what each contribution answered for it, and the functions it defines, by logical name.
    /// </summary>
    private sealed record Prepared(
        DbContext Context,
        IReadOnlyList<RowAccessRule> Rules,
        IReadOnlyList<RowAccessFunction> Functions,
        IReadOnlyList<Contributed> Contributions,
        IReadOnlyList<Named> Names,
        IReadOnlyDictionary<RowAccessRule, IReadOnlyList<string>> Roles,
        RowAccessRoleNames RoleNames);

    /// <summary>What a contribution answered for a context, with the owner its functions' logical names start with.</summary>
    private sealed record Contributed(IRowAccessContribution Contribution, string Owner, RowAccessContributionResult Result)
    {
        /// <summary>The contribution's type, its assembly and the assembly's version, as the comments above what it writes name it.</summary>
        public string Source { get; } = SourceOf(Contribution.GetType());
    }

    /// <summary>A function a context defines: the name rules ask it by, the name it has in the database, and what it is.</summary>
    private sealed record Named(string Logical, string Resolved, string About);

    /// <summary>
    /// A contribution's policy as a script writes it: for one command, <c>ALL</c> spread over the four for a
    /// permissive one, with its role resolved and its conditions filled in.
    /// </summary>
    private sealed record Policy(Contributed From, ContributedPolicy Declared, StoreObjectIdentifier Table, string Command, string Role, string? Using, string? Check, bool Restrictive);

    /// <summary>
    /// A function a script writes, an access function or a contributed one: its names, how a grant names it,
    /// the statements that make it, the functions its body asks by the names they have in the database, and
    /// the roles that may execute it.
    /// </summary>
    private sealed record Definition(string Logical, string Resolved, string Signature, string Statement, IReadOnlyList<string> Asks, IReadOnlyList<string> GrantTo);

    /// <summary>
    /// The names a script gives the policies it writes, per table: Postgres keeps one policy of a name on a
    /// table, so a second would fail the script where it runs, rather than where it is written.
    /// </summary>
    private sealed class PolicyNames
    {
        private readonly Dictionary<(StoreObjectIdentifier Table, string Name), string> _taken = [];

        /// <summary><paramref name="name"/>, now taken on <paramref name="table"/> by <paramref name="what"/>.</summary>
        /// <exception cref="InvalidOperationException">Another policy of the script on the table has the name.</exception>
        public string Claim(StoreObjectIdentifier table, string name, string what)
            => _taken.TryAdd((table, name), what)
                ? name
                : throw new InvalidOperationException(
                    $"Two policies on {table.DisplayName()} would be named \"{name}\": {_taken[(table, name)]} and {what}. Postgres keeps one policy of a name on a table; give the rule or the contributed policy another name.");
    }

    /// <summary>Who asks the access functions of every context a script is written with: the roles to grant each to, by the name it has in the database.</summary>
    private sealed record Knowledge(IReadOnlyDictionary<string, SortedSet<string>> Grantees)
    {
        public IReadOnlyList<string> GranteesOf(string resolved) => Grantees.TryGetValue(resolved, out var roles) ? [.. roles] : [];
    }

    /// <summary>
    /// Asks every contribution of <paramref name="export"/> what it writes for <paramref name="context"/>,
    /// checks it, and names every function the context defines.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A contribution writes what it may not, two functions of the context would have one name, a rule names a
    /// role no policy can be for, or a rule or a contribution adds a policy to a table another keeps to itself.
    /// </exception>
    private static Prepared Prepare(DbContext context, IReadOnlyList<RowAccessRule> rules, IReadOnlyList<RowAccessFunction> functions, RowAccessExport export)
    {
        ArgumentNullException.ThrowIfNull(context);

        var contributions = new List<Contributed>();
        foreach (var contribution in export.Contributions)
        {
            if (contribution.Contribute(context, export) is { } result)
            {
                contributions.Add(Checked(context, contribution, result, export.Roles));
            }
        }

        var names = new List<Named>();
        var byResolved = new Dictionary<string, Named>(StringComparer.OrdinalIgnoreCase);
        void Define(Named named)
        {
            if (byResolved.TryGetValue(named.Resolved, out var other))
            {
                throw new InvalidOperationException(
                    $"The functions {other.Logical} ({other.About}) and {named.Logical} ({named.About}) of {context.GetType().Name} would both be {named.Resolved}. A function has one definition; give one of them another name.");
            }

            byResolved[named.Resolved] = named;
            names.Add(named);
        }

        foreach (var function in functions)
        {
            Define(new Named(function.LogicalName, ResolvedName(context, function), "about " + function.AggregateTypeName));
        }

        // A contributed function lives in the context's default schema: a contribution may write for several
        // tables, and its functions are the context's, not one table's.
        var schema = context.Model.GetDefaultSchema() ?? DefaultSchema;
        foreach (var contributed in contributions)
        {
            foreach (var function in contributed.Result.Functions)
            {
                var logical = contributed.Owner + "/" + function.Name;
                Define(new Named(logical, FunctionSchema(context, schema, logical) + "." + function.Name, "from the row access contribution " + contributed.Contribution.GetType().FullName));
            }
        }

        var roles = rules.Distinct().ToDictionary(rule => rule, rule => RolesOf(rule, export.Roles));
        EnsureKeptToThemselves(context, rules, contributions);

        return new Prepared(context, rules, functions, contributions, names, roles, export.Roles);
    }

    /// <summary>
    /// <paramref name="result"/>, refused where it writes what a script cannot: a function or a policy Postgres
    /// would not take, a table the context does not map, a role no policy can be for, a definer function whose
    /// search path a caller's session would decide, or a function to fold into the queries that call it which
    /// Postgres would not fold.
    /// </summary>
    /// <exception cref="InvalidOperationException">The contribution writes what it may not.</exception>
    private static Contributed Checked(DbContext context, IRowAccessContribution contribution, RowAccessContributionResult result, RowAccessRoleNames roles)
    {
        var type = contribution.GetType().FullName;
        if (string.IsNullOrWhiteSpace(contribution.Owner))
        {
            throw new InvalidOperationException($"The row access contribution {type} has no Owner, which the logical names of its functions, owner/name, start with.");
        }

        if (result.Functions is null || result.Policies is null || result.Statements is null)
        {
            throw new InvalidOperationException($"The row access contribution {type} answered {context.GetType().Name} with no list of functions, policies or statements. Answer an empty list for none.");
        }

        foreach (var function in result.Functions)
        {
            var what = $"The function '{function.Name}' of the row access contribution {type}";
            if (function.Name is null || !RowAccessNames.IsIdentifier(function.Name))
            {
                throw new InvalidOperationException($"{what} is not named as a function a script can create: letters, digits and underscores, without a schema, which is the context's.");
            }

            if (function.Parameters is null || (function.Parameters.Trim().Length > 0 && !TopLevel(function.Parameters).All(parameter => RowAccessFunction.SqlType().IsMatch(parameter))))
            {
                throw new InvalidOperationException($"{what} has the parameters '{function.Parameters}', which are not SQL parameters such as 'key text, unit uuid'.");
            }

            if (function.Returns is null || !IsReturned(function.Returns.Trim()))
            {
                throw new InvalidOperationException($"{what} returns '{function.Returns}', which is not an SQL type such as boolean, uuid or SETOF uuid, nor rows of named columns, TABLE (\"Id\" uuid, due timestamp with time zone).");
            }

            if (string.IsNullOrWhiteSpace(function.Body) || function.Body.Contains("$function$", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{what} has {(string.IsNullOrWhiteSpace(function.Body) ? "no body" : "'$function$' in its body, which would end it early")}.");
            }

            if (function.Volatility?.Trim().ToUpperInvariant() is not ("STABLE" or "IMMUTABLE" or "VOLATILE"))
            {
                throw new InvalidOperationException($"{what} is '{function.Volatility}', and a function is STABLE, IMMUTABLE or VOLATILE.");
            }

            if (function.Inlinable)
            {
                // What Postgres asks of a function before it folds it into the query that calls it. One that runs
                // as its owner would also lose the empty search path every such function is pinned to.
                var unfoldable = function.SecurityDefiner ? "runs as its owner, and such a function keeps SET search_path = '', which Postgres does not fold"
                    : function.Volatility.Trim().ToUpperInvariant() == "VOLATILE" ? "is VOLATILE, and a function Postgres folds into a query is STABLE or IMMUTABLE"
                    : !IsOneSelect(function.Body) ? "has a body that is not one SELECT, which is all Postgres folds"
                    : null;
                if (unfoldable is not null)
                {
                    throw new InvalidOperationException($"{what} is Inlinable, to be folded into the queries that call it, but {unfoldable}. Leave Inlinable off, or write it as a function that can be folded.");
                }
            }

            ResolvedRoles(function.GrantTo ?? [], roles, what + " is granted to");
        }

        var owned = new HashSet<StoreObjectIdentifier>();
        foreach (var policy in result.Policies)
        {
            var what = $"The policy '{policy.Name}' of the row access contribution {type}";
            if (string.IsNullOrWhiteSpace(policy.Name))
            {
                throw new InvalidOperationException($"A policy of the row access contribution {type} has no name.");
            }

            owned.Add(TableOfTheContext(context, policy.Table, what));
            var command = policy.Command?.Trim().ToUpperInvariant();
            var problem = command switch
            {
                "SELECT" or "DELETE" when policy.Using is null => $"is for {command}, which needs Using.",
                "SELECT" or "DELETE" when policy.WithCheck is not null => $"is for {command}, which Postgres checks with Using alone.",
                "INSERT" when policy.WithCheck is null => "is for INSERT, which needs WithCheck.",
                "INSERT" when policy.Using is not null => "is for INSERT, which Postgres checks with WithCheck alone.",
                "UPDATE" or "ALL" when policy.Using is null => $"is for {command}, which needs Using.",
                "SELECT" or "INSERT" or "UPDATE" or "DELETE" or "ALL" => null,
                _ => $"is for '{policy.Command}', and a policy is for SELECT, INSERT, UPDATE, DELETE or ALL.",
            };

            if (problem is not null)
            {
                throw new InvalidOperationException($"{what} {problem}");
            }

            ResolvedRoles([policy.Role], roles, what + " is for");
        }

        foreach (var statement in result.Statements)
        {
            if (string.IsNullOrWhiteSpace(statement))
            {
                throw new InvalidOperationException($"The row access contribution {type} has an empty statement.");
            }

            if (Definer().IsMatch(statement))
            {
                // A statement's function gets no grants from the script, so only a trigger's may run as its
                // owner: Postgres never lets a trigger function be called on its own.
                if (!ReturnsTrigger().IsMatch(statement))
                {
                    throw new InvalidOperationException(
                        $"The row access contribution {type} has a statement that makes a SECURITY DEFINER function that is not a trigger's, which the script would leave executable by every role. Write it as a ContributedFunction, which the script grants to its GrantTo alone and pins to an empty search path.");
                }

                if (!EmptySearchPath().IsMatch(statement))
                {
                    throw new InvalidOperationException(
                        $"The row access contribution {type} has a statement that makes a SECURITY DEFINER function without SET search_path = '', so the schemas of a caller's session would decide what it runs, with the rights of its owner. Pin it in the same statement: SET search_path = ''.");
                }
            }
        }

        foreach (var table in result.ExclusiveTables ?? [])
        {
            TableOfTheContext(context, table, $"A table the row access contribution {type} keeps to itself");
        }

        return new Contributed(contribution, RowAccessNames.NormalizeOwner(contribution.Owner), result);
    }

    /// <summary>The table <paramref name="entity"/> is mapped to, when it is a type of <paramref name="context"/>'s model mapped to one.</summary>
    /// <exception cref="InvalidOperationException">It is not.</exception>
    private static StoreObjectIdentifier TableOfTheContext(DbContext context, IEntityType? entity, string what)
    {
        if (entity is null || context.Model.FindEntityType(entity.Name) is null || entity.GetTableName() is null)
        {
            throw new InvalidOperationException(
                $"{what} is on {entity?.DisplayName() ?? "no entity type"}, which {context.GetType().Name} does not map to a table of its own. Take the entity types from the context's model.");
        }

        return TableOf(entity);
    }

    /// <summary>
    /// Refuses a rule or a contribution that would add a policy to a table another contribution keeps to
    /// itself, and two contributions that keep one table.
    /// </summary>
    /// <exception cref="InvalidOperationException">One does.</exception>
    private static void EnsureKeptToThemselves(DbContext context, IReadOnlyList<RowAccessRule> rules, IReadOnlyList<Contributed> contributions)
    {
        var keepers = new Dictionary<StoreObjectIdentifier, Contributed>();
        foreach (var contributed in contributions)
        {
            foreach (var table in (contributed.Result.ExclusiveTables ?? []).Select(TableOf).Distinct())
            {
                if (keepers.TryGetValue(table, out var other))
                {
                    throw new InvalidOperationException(
                        $"The row access contributions {other.Contribution.GetType().FullName} and {contributed.Contribution.GetType().FullName} both keep {table.DisplayName()} to themselves. Only one of them can write its policies.");
                }

                keepers[table] = contributed;
            }
        }

        if (keepers.Count == 0)
        {
            return;
        }

        foreach (var rule in rules)
        {
            var root = RootOf(context, rule.AggregateTypeName);
            var rootTable = TableOf(root);
            foreach (var table in EntitiesOf(root, rootTable, []).Select(each => each.Table).Prepend(rootTable))
            {
                if (keepers.TryGetValue(table, out var keeper))
                {
                    throw new InvalidOperationException(
                        $"The rule '{rule.Name}' would add a policy to {table.DisplayName()}, which the row access contribution {keeper.Contribution.GetType().FullName} keeps to itself: only it writes that table's policies. Leave the rule out, or ask the contribution for what the rule needs.");
                }
            }
        }

        foreach (var contributed in contributions)
        {
            foreach (var policy in contributed.Result.Policies)
            {
                if (keepers.TryGetValue(TableOf(policy.Table), out var keeper) && !ReferenceEquals(keeper, contributed))
                {
                    throw new InvalidOperationException(
                        $"The row access contribution {contributed.Contribution.GetType().FullName} writes the policy '{policy.Name}' on {TableOf(policy.Table).DisplayName()}, which the row access contribution {keeper.Contribution.GetType().FullName} keeps to itself: only it writes that table's policies.");
                }
            }
        }
    }

    /// <summary>The functions <paramref name="prepared"/>'s script writes, access functions and contributed ones, with who may execute each.</summary>
    private static List<Definition> Definitions(Prepared prepared, Writing writing, Knowledge knowledge)
    {
        var context = prepared.Context;
        var definitions = new List<Definition>();
        foreach (var function in prepared.Functions)
        {
            var resolved = writing.Names[function.LogicalName];
            var (signature, statement) = FunctionStatement(context, function, writing);
            definitions.Add(new Definition(function.LogicalName, resolved, signature, statement, Asks(function.Sql, writing.Names), knowledge.GranteesOf(resolved)));
        }

        foreach (var contributed in prepared.Contributions)
        {
            foreach (var function in contributed.Result.Functions)
            {
                var logical = contributed.Owner + "/" + function.Name;
                var resolved = writing.Names[logical];
                var what = $"the function {logical} of the row access contribution {contributed.Contribution.GetType().FullName}";
                var signature = resolved + "(" + string.Join(", ", TopLevel(function.Parameters)) + ")";

                // A function to fold has no settings: with any, Postgres keeps it a step of its own.
                var settings = function.Inlinable ? "" : " SET search_path = ''";
                var statement = new StringBuilder()
                    .Append('\n')
                    .Append("-- Written by the row access contribution ").Append(Commented(contributed.Source)).Append('.').Append('\n')
                    .Append("CREATE OR REPLACE FUNCTION ").Append(signature).Append(" RETURNS ").Append(function.Returns.Trim()).Append('\n')
                    .Append("    LANGUAGE sql ").Append(function.Volatility.Trim().ToUpperInvariant()).Append(function.SecurityDefiner ? " SECURITY DEFINER" : "")
                    .Append(settings).Append(" AS $function$").Append('\n')
                    .Append(FillContributed(what, function.Body.Trim('\r', '\n'), writing)).Append('\n')
                    .Append("$function$;").Append('\n')
                    .Append("COMMENT ON FUNCTION ").Append(signature).Append(" IS ").Append(Literal(FunctionComment(context))).Append(";\n")
                    .ToString();

                definitions.Add(new Definition(logical, resolved, signature, statement, Asks(function.Body, writing.Names), ResolvedRoles(function.GrantTo ?? [], writing.Roles, what)));
            }
        }

        return definitions;
    }

    /// <summary>
    /// Who may execute the functions a script writes: every grant on them taken back but their owner's, and
    /// each granted to the roles that ask it. Each function is revoked from <c>PUBLIC</c> after it is created;
    /// this takes back what the rest were given, whatever gave it: an earlier script of the context that
    /// granted it to a role that no longer asks it, or a schema's default privileges, which on Supabase's
    /// <c>public</c> give every new function to the user's role, the anonymous caller's and the service role.
    /// </summary>
    /// <param name="context">The context whose functions carry its comment.</param>
    /// <param name="definitions">The functions the script writes.</param>
    /// <param name="kept">Their names in the database, <c>schema.name</c>; a function the script no longer writes keeps its grants while it stays.</param>
    /// <param name="sql">How Postgres quotes a role.</param>
    private static string GrantStatements(DbContext context, IReadOnlyList<Definition> definitions, IReadOnlyList<string> kept, ISqlGenerationHelper sql)
    {
        if (definitions.Count == 0)
        {
            return string.Empty;
        }

        var grants = new StringBuilder()
            .Append('\n')
            .Append("-- Who may execute the functions above: the roles granted it below, and nobody else. Every other grant").Append('\n')
            .Append("-- on them is taken back first, what an earlier file gave and what a schema's default privileges gave alike.").Append('\n')
            .Append(Blocks(1, new StringBuilder()
                .Append("DO $ddd$").Append('\n')
                .Append("DECLARE").Append('\n')
                .Append("    granted record;").Append('\n')
                .Append("BEGIN").Append('\n')
                .Append("    FOR granted IN").Append('\n')
                .Append("        SELECT DISTINCT p.oid AS function, acl.grantee").Append('\n')
                .Append("        FROM pg_catalog.pg_proc p").Append('\n')
                .Append("        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace").Append('\n')
                .Append("        JOIN pg_catalog.pg_description d ON d.objoid = p.oid AND d.classoid = 'pg_catalog.pg_proc'::regclass").Append('\n')
                .Append("        CROSS JOIN LATERAL pg_catalog.aclexplode(p.proacl) acl").Append('\n')
                .Append("        WHERE d.description = ").Append(Literal(FunctionComment(context))).Append('\n')
                .Append("          AND (n.nspname, p.proname) IN (").Append(string.Join(", ", NamePairs(kept))).Append(')').Append('\n')
                .Append("          AND acl.grantee <> 0 AND acl.grantee <> p.proowner").Append('\n')
                .Append("    LOOP").Append('\n')
                .Append("        EXECUTE pg_catalog.format('REVOKE ALL ON FUNCTION %s FROM %s', granted.function::pg_catalog.regprocedure, granted.grantee::pg_catalog.regrole);").Append('\n')
                .Append("    END LOOP;").Append('\n')
                .Append("END").Append('\n')
                .Append("$ddd$;").Append('\n')
                .ToString()));

        foreach (var definition in definitions.Where(definition => definition.GrantTo.Count > 0))
        {
            grants.Append("GRANT EXECUTE ON FUNCTION ").Append(definition.Signature).Append(" TO ")
                .Append(string.Join(", ", definition.GrantTo.Select(role => sql.DelimitIdentifier(role)))).Append(";\n");
        }

        return grants.ToString();
    }

    /// <summary>The contributions' policies of <paramref name="prepared"/>, each for one command, its role resolved and its conditions filled in.</summary>
    private static List<Policy> ContributedPolicies(Prepared prepared, Writing writing)
    {
        var policies = new List<Policy>();
        foreach (var contributed in prepared.Contributions)
        {
            foreach (var policy in contributed.Result.Policies)
            {
                var what = $"the policy '{policy.Name}' of the row access contribution {contributed.Contribution.GetType().FullName}";
                var table = TableOf(policy.Table);
                var role = ResolvedRoles([policy.Role], writing.Roles, what + " is for")[0];
                var command = policy.Command.Trim().ToUpperInvariant();
                var existing = policy.Using is null ? null : FillContributed(what, policy.Using, writing);
                var left = policy.WithCheck is null ? null : FillContributed(what, policy.WithCheck, writing);

                if (policy.Restrictive)
                {
                    policies.Add(new Policy(contributed, policy, table, command, role, existing, left, Restrictive: true));
                }
                else if (command == "ALL")
                {
                    policies.AddRange(PolicyCommands.Select(each => new Policy(contributed, policy, table, each, role, existing, left ?? existing, Restrictive: false)));
                }
                else
                {
                    policies.Add(new Policy(contributed, policy, table, command, role, existing, command == "UPDATE" ? left ?? existing : left, Restrictive: false));
                }
            }
        }

        return policies;
    }

    /// <summary>
    /// The contributions' policies no rule's table took in: the permissive ones on tables without rules, merged
    /// per command and role, then every restrictive one, each table with row level security turned on first.
    /// </summary>
    private static string ContributedPolicyStatements(
        Prepared prepared,
        IReadOnlyList<Policy> policies,
        Dictionary<(StoreObjectIdentifier Table, string Command, string Role), List<Policy>> permissive,
        HashSet<StoreObjectIdentifier> secured,
        Writing writing,
        PolicyNames policyNames,
        Privileges? privileges)
    {
        var sql = writing.Sql;
        var tables = permissive.Keys.Select(key => key.Table)
            .Concat(policies.Where(policy => policy.Restrictive).Select(policy => policy.Table))
            .Concat(prepared.Contributions.SelectMany(contributed => contributed.Result.ExclusiveTables ?? []).Select(TableOf))
            .Distinct()
            .OrderBy(table => table.Schema ?? DefaultSchema, StringComparer.Ordinal)
            .ThenBy(table => table.Name, StringComparer.Ordinal);

        var statements = new StringBuilder();
        foreach (var table in tables)
        {
            var qualified = sql.DelimitIdentifier(table.Name, table.Schema ?? DefaultSchema);
            if (secured.Add(table))
            {
                statements.Append('\n').Append(EnableStatement(qualified, writing.Force));
            }

            foreach (var command in PolicyCommands)
            {
                foreach (var role in permissive.Keys.Where(key => key.Table == table && key.Command == command).Select(key => key.Role).Order(StringComparer.Ordinal).ToList())
                {
                    var added = Take(permissive, table, command, role);
                    var of = $" ({command.ToLowerInvariant()}) for {role}";
                    var name = policyNames.Claim(table, PolicyName(added.Count == 1 ? added[0].Declared.Name + of : table.Name + of), $"the permissive policy for {command} to {role}");
                    privileges?.Allow(table, command, role);

                    statements.Append('\n')
                        .Append("-- ").Append(Commented(name)).Append(" asks ").Append(Listed(added.Select(Described)))
                        .Append(added.Count > 1 ? ": a row one of them allows is allowed." : ".").Append('\n')
                        .Append("CREATE POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(qualified)
                        .Append(" FOR ").Append(command).Append(" TO ").Append(sql.DelimitIdentifier(role)).Append('\n')
                        .Append(Clauses(command, AnyOf(added.Select(policy => policy.Using)), AnyOf(added.Select(policy => policy.Check)))).Append(";\n")
                        .Append("COMMENT ON POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(qualified)
                        .Append(" IS ").Append(Literal(PolicyComment)).Append(";\n");
                }
            }

            foreach (var policy in policies.Where(policy => policy.Restrictive && policy.Table == table))
            {
                var name = policyNames.Claim(table, PolicyName($"{policy.Declared.Name} ({policy.Command.ToLowerInvariant()}) for {policy.Role}"), "the restrictive " + Described(policy)["the ".Length..]);
                var clauses = policy.Command switch
                {
                    "INSERT" => "    WITH CHECK " + Parenthesized(policy.Check!),
                    "SELECT" or "DELETE" => "    USING " + Parenthesized(policy.Using!),
                    _ => "    USING " + Parenthesized(policy.Using!) + (policy.Check is null ? "" : "\n    WITH CHECK " + Parenthesized(policy.Check)),
                };

                statements.Append('\n')
                    .Append("-- ").Append(Commented(name)).Append(" is ").Append(Described(policy))
                    .Append(", which narrows what the permissive policies allow.").Append('\n')
                    .Append("CREATE POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(qualified)
                    .Append(" AS RESTRICTIVE FOR ").Append(policy.Command).Append(" TO ").Append(sql.DelimitIdentifier(policy.Role)).Append('\n')
                    .Append(clauses).Append(";\n")
                    .Append("COMMENT ON POLICY ").Append(sql.DelimitIdentifier(name)).Append(" ON ").Append(qualified)
                    .Append(" IS ").Append(Literal(PolicyComment)).Append(";\n");
            }
        }

        return statements.ToString();
    }

    /// <summary>The contributions' own statements, each contribution's under a comment that names it.</summary>
    private static string ContributedStatements(Prepared prepared, Writing writing)
    {
        var statements = new StringBuilder();
        foreach (var contributed in prepared.Contributions.Where(contributed => contributed.Result.Statements.Count > 0))
        {
            var what = $"a statement of the row access contribution {contributed.Contribution.GetType().FullName}";
            statements.Append('\n').Append("-- Written by the row access contribution ").Append(Commented(contributed.Source)).Append('.').Append('\n');
            foreach (var statement in contributed.Result.Statements)
            {
                // A statement that ends in a line comment gets its ';' on a line of its own, outside the comment.
                var filled = FillContributed(what, statement.Trim(), writing);
                statements.Append(filled)
                    .Append(filled.EndsWith(';') ? "" : filled[(filled.LastIndexOf('\n') + 1)..].Contains("--", StringComparison.Ordinal) ? "\n;" : ";")
                    .Append('\n');
            }
        }

        return statements.ToString();
    }

    /// <summary><c>the policy 'Name' of the row access contribution Type in Assembly 1.0.0</c>, as a comment names it.</summary>
    private static string Described(Policy policy)
        => $"the policy '{Commented(policy.Declared.Name)}' of the row access contribution {Commented(policy.From.Source)}";

    /// <summary>
    /// The type, its assembly and the assembly's version, without the build metadata after <c>+</c>: that names
    /// the commit a build came from, and would make every build of it a new access file.
    /// </summary>
    private static string SourceOf(Type type)
    {
        var assembly = type.Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informational) ? assembly.GetName().Version?.ToString() : informational.Split('+')[0];
        return $"{type.FullName} in {assembly.GetName().Name} {(string.IsNullOrWhiteSpace(version) ? "without a version" : version)}";
    }

    /// <summary>
    /// SQL a contribution wrote, with its <c>{fn:owner/name}</c> places the names the functions have in the
    /// database and its <c>{caller:...}</c> places the caller functions; <c>{{</c> and <c>}}</c> are braces.
    /// </summary>
    /// <exception cref="InvalidOperationException">It has a place contributed SQL cannot have, or asks a logical name nothing defines.</exception>
    private static string FillContributed(string what, string template, Writing writing)
    {
        var filled = new StringBuilder(template.Length * 2);
        for (var i = 0; i < template.Length; i++)
        {
            var character = template[i];
            if ((character is '{' or '}') && i + 1 < template.Length && template[i + 1] == character)
            {
                filled.Append(character);
                i++;
                continue;
            }

            if (character != '{')
            {
                filled.Append(character);
                continue;
            }

            var end = template.IndexOf('}', i);
            var token = end < 0 ? template[(i + 1)..] : template[(i + 1)..end];
            var parts = token.Split(':');
            filled.Append(parts switch
            {
                ["fn", var name] when end >= 0 => FunctionName(what, name, writing),
                ["caller", ..] when end >= 0 && parts.Length >= 2 => AboutTheCaller(parts, writing) ?? throw NotAPlace(what, token),
                _ => throw NotAPlace(what, token),
            });
            i = end;
        }

        return filled.ToString();
    }

    private static InvalidOperationException NotAPlace(string what, string token)
        => new($"{Capitalized(what)} has '{{{token}}}', which contributed SQL cannot ask. It asks functions by {{fn:owner/name}} and the caller by {{caller:uid}}, {{caller:role}}, {{caller:claims}}, {{caller:signedin}} or {{caller:claim:path}}; write a brace itself as {{{{ or }}}}.");

    /// <summary>The functions <paramref name="sql"/> asks, by the names they have in the database.</summary>
    private static List<string> Asks(string sql, IReadOnlyDictionary<string, string> names)
        => [.. FunctionsAskedBy(sql).Select(name => names.TryGetValue(name, out var resolved) ? resolved : name).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The roles <paramref name="roles"/> names, resolved: symbolic ones to the roles of the script, and a
    /// role's own name as it is spelled.
    /// </summary>
    /// <exception cref="InvalidOperationException">One is a role no policy or grant can be for.</exception>
    private static IReadOnlyList<string> ResolvedRoles(IEnumerable<string> roles, RowAccessRoleNames names, string what)
    {
        var resolved = new List<string>();
        foreach (var role in roles)
        {
            var problem = "Name a role.";
            if (role is null || !names.TryResolve(role, out var name, out problem))
            {
                throw new InvalidOperationException($"{Capitalized(what)} '{role}', which no policy or grant can be for. {problem}");
            }

            resolved.Add(name);
        }

        return [.. resolved.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Who may execute each access function of <paramref name="prepared"/>: the roles of every policy that asks
    /// it, and of every contributed function that runs as its caller and asks it. Refuses a policy, or such a
    /// function, that asks a contributed function for a role it is not granted to: a contributed function's
    /// grants are exactly its <see cref="ContributedFunction.GrantTo"/>. An access function runs as its owner,
    /// so one a policy asks is followed to the functions it asks in turn: a contributed function it asks, which
    /// is granted to some roles, is asked for the policy's roles as well. One granted to none is a helper for
    /// the functions that run as their owner, and an access function may ask it.
    /// </summary>
    /// <exception cref="InvalidOperationException">One does.</exception>
    private static Knowledge Know(IReadOnlyList<Prepared> prepared, IReadOnlyDictionary<string, string> names)
    {
        string Resolve(string asked) => names.TryGetValue(asked, out var resolved) ? resolved : asked;

        // Each access function by the name it has in the database, with its logical name and the functions its body asks.
        var access = new Dictionary<string, (string Logical, IReadOnlyList<string> Asks)>(StringComparer.OrdinalIgnoreCase);
        foreach (var function in prepared.SelectMany(each => each.Functions))
        {
            access.TryAdd(Resolve(function.LogicalName), (function.LogicalName, [.. FunctionsAskedBy(function.Sql).Select(Resolve).Distinct(StringComparer.OrdinalIgnoreCase)]));
        }

        var contributedFunctions = new Dictionary<string, (string Logical, IReadOnlyList<string> GrantTo)>(StringComparer.OrdinalIgnoreCase);
        foreach (var each in prepared)
        {
            foreach (var contributed in each.Contributions)
            {
                foreach (var function in contributed.Result.Functions)
                {
                    var logical = contributed.Owner + "/" + function.Name;
                    contributedFunctions[Resolve(logical)] = (logical, ResolvedRoles(function.GrantTo ?? [], each.RoleNames, "the function is granted to"));
                }
            }
        }

        var grantees = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Asked(string what, IReadOnlyList<string> roles, string? sql, Func<string, IReadOnlyList<string>, string> advice)
        {
            foreach (var asked in FunctionsAskedBy(sql ?? "").Select(Resolve).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (contributedFunctions.ContainsKey(asked))
                {
                    Refuse(what, [], asked, roles, advice);
                }
                else if (access.ContainsKey(asked))
                {
                    if (!grantees.TryGetValue(asked, out var granted))
                    {
                        grantees[asked] = granted = new SortedSet<string>(StringComparer.Ordinal);
                    }

                    granted.UnionWith(roles);
                    Through(what, asked, roles, advice);
                }
            }
        }

        // The contributed functions an access function asks, itself or through the access functions it asks, run
        // as its owner for whoever may ask it: each must be granted to the roles of the policy that asks it. One
        // granted to no role is a helper for the functions that run as their owner, which an access function is.
        void Through(string what, string first, IReadOnlyList<string> roles, Func<string, IReadOnlyList<string>, string> advice)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { first };
            var path = new Stack<(string Function, IReadOnlyList<string> Via)>();
            path.Push((first, [access[first].Logical]));
            while (path.Count > 0)
            {
                var (function, via) = path.Pop();
                foreach (var asked in access[function].Asks)
                {
                    if (contributedFunctions.TryGetValue(asked, out var contributed) && contributed.GrantTo.Count > 0)
                    {
                        Refuse(what, via, asked, roles, advice);
                    }
                    else if (access.ContainsKey(asked) && seen.Add(asked))
                    {
                        path.Push((asked, [.. via, access[asked].Logical]));
                    }
                }
            }
        }

        // Refuses asking a contributed function for a role it is not granted to, directly or through access functions.
        void Refuse(string what, IReadOnlyList<string> via, string asked, IReadOnlyList<string> roles, Func<string, IReadOnlyList<string>, string> advice)
        {
            var function = contributedFunctions[asked];
            if (roles.FirstOrDefault(role => !function.GrantTo.Contains(role, StringComparer.Ordinal)) is { } refused)
            {
                var through = via.Count == 0 ? "" : $"through {string.Join(", which asks ", via)}, which runs as its owner, ";
                throw new InvalidOperationException(
                    $"{what} asks {function.Logical} {through}for {refused}, which that function is not granted to. " +
                    (function.GrantTo.Count == 0 ? "It is granted to no role, so only a function that runs as its owner may ask it." : advice(refused, function.GrantTo)));
            }
        }

        foreach (var each in prepared)
        {
            var roleNames = each.RoleNames;
            foreach (var (rule, ruleRoles) in each.Roles)
            {
                Asked($"The rule '{rule.Name}'", ruleRoles, rule.Sql, (_, granted) =>
                    $"Set To = [{string.Join(", ", granted.Select(role => Symbolic(role, roleNames)))}].");
            }

            foreach (var contributed in each.Contributions)
            {
                var type = contributed.Contribution.GetType().FullName;
                foreach (var policy in contributed.Result.Policies)
                {
                    Asked(
                        $"The policy '{policy.Name}' of the row access contribution {type}",
                        ResolvedRoles([policy.Role], roleNames, "the policy is for"),
                        policy.Using + " " + policy.WithCheck,
                        (_, granted) => $"Write the policy for a role it is granted to: {string.Join(", ", granted)}.");
                }

                foreach (var function in contributed.Result.Functions.Where(function => !function.SecurityDefiner))
                {
                    var logical = contributed.Owner + "/" + function.Name;
                    Asked(
                        $"The function {logical}, which runs as its caller,",
                        ResolvedRoles(function.GrantTo ?? [], roleNames, "the function is granted to"),
                        function.Body,
                        (refused, _) => $"A role that may execute {logical} executes what it asks: grant that function to {refused} as well, or let {logical} run as its owner.");
                }
            }
        }

        return new Knowledge(grantees);
    }

    /// <summary>
    /// <c>RowAccessRoles.User</c> for the user's role, and so on; <c>RowAccessRoles.Token("analyst")</c> for the role
    /// a token role is mapped to; <c>RowAccessRoles.System</c> for the bookkeeping role; a role of its own in quotes.
    /// </summary>
    private static string Symbolic(string role, RowAccessRoleNames names)
        => role == names.User ? "RowAccessRoles.User"
            : role == names.Anonymous ? "RowAccessRoles.Anonymous"
            : role == names.SystemIn ? "RowAccessRoles.SystemIn"
            : role == names.System ? "RowAccessRoles.System"
            : names.TokenRoles.FirstOrDefault(mapped => mapped.Value == role) is { Key: { } tokenRole } ? $"RowAccessRoles.Token(\"{tokenRole}\")"
            : "\"" + role + "\"";

    /// <summary>
    /// <paramref name="definitions"/>, each after the functions of the same script its body asks, and otherwise
    /// by name: Postgres checks a function's body when it creates it, so one that calls a function which does
    /// not exist yet is refused.
    /// </summary>
    /// <exception cref="InvalidOperationException">The functions ask each other in a circle.</exception>
    private static List<Definition> InDependencyOrder(DbContext context, IReadOnlyList<Definition> definitions)
    {
        var inScript = definitions.ToDictionary(definition => definition.Resolved, StringComparer.OrdinalIgnoreCase);
        var remaining = definitions.OrderBy(definition => definition.Resolved, StringComparer.Ordinal).ToList();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<Definition>(definitions.Count);

        IEnumerable<string> Waiting(Definition definition)
            => definition.Asks.Where(asked => inScript.ContainsKey(asked) && !written.Contains(asked) && !string.Equals(asked, definition.Resolved, StringComparison.OrdinalIgnoreCase));

        while (remaining.Count > 0)
        {
            if (remaining.FirstOrDefault(definition => !Waiting(definition).Any()) is not { } next)
            {
                var circle = Circle(remaining[0].Resolved, name => Waiting(inScript[name]).First());
                throw new InvalidOperationException(
                    $"The functions of {context.GetType().Name} ask each other in a circle, {string.Join(" asks ", circle)}: Postgres checks a function's body when it creates it, so none of them can be created first. Take one of the questions out of the circle.");
            }

            ordered.Add(next);
            written.Add(next.Resolved);
            remaining.Remove(next);
        }

        return ordered;
    }

    /// <summary>
    /// <paramref name="prepared"/>, each after the contexts that define a function it asks, in its rules, its
    /// access functions or what its contributions write, and otherwise in the order given.
    /// </summary>
    /// <exception cref="InvalidOperationException">The contexts ask each other's functions in a circle.</exception>
    private static List<Prepared> InDependencyOrder(IReadOnlyList<Prepared> prepared, IReadOnlyDictionary<string, string> names)
    {
        var definers = new Dictionary<string, List<Prepared>>(StringComparer.OrdinalIgnoreCase);
        foreach (var each in prepared)
        {
            foreach (var named in each.Names)
            {
                if (!definers.TryGetValue(named.Resolved, out var list))
                {
                    definers[named.Resolved] = list = [];
                }

                list.Add(each);
            }
        }

        // What each context waits for: the contexts that define a function it asks and does not define itself.
        var waitsFor = prepared.ToDictionary(
            each => each,
            each =>
            {
                var own = each.Names.Select(named => named.Resolved).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return AskedByAnything(each)
                    .SelectMany(asked => Asks(asked, names))
                    .Where(asked => !own.Contains(asked) && definers.ContainsKey(asked))
                    .SelectMany(asked => definers[asked].Select(definer => (Definer: definer, Function: asked)))
                    .ToList();
            });

        var remaining = prepared.ToList();
        var written = new HashSet<Prepared>();
        var ordered = new List<Prepared>(prepared.Count);
        while (remaining.Count > 0)
        {
            if (remaining.FirstOrDefault(each => waitsFor[each].All(waiting => written.Contains(waiting.Definer))) is not { } next)
            {
                var steps = new List<string>();
                var circle = Circle(remaining[0], each => waitsFor[each].First(waiting => !written.Contains(waiting.Definer)).Definer);
                for (var i = 0; i < circle.Count - 1; i++)
                {
                    var function = waitsFor[circle[i]].First(waiting => ReferenceEquals(waiting.Definer, circle[i + 1])).Function;
                    steps.Add($"{circle[i].Context.GetType().Name} asks {function}, which {circle[i + 1].Context.GetType().Name} defines");
                }

                throw new InvalidOperationException(
                    $"The contexts ask each other's functions in a circle: {string.Join("; ", steps)}. Postgres refuses a policy or a function that asks one which does not exist yet, so none of their scripts can run first. Move one of the functions into the module that asks it.");
            }

            ordered.Add(next);
            written.Add(next);
            remaining.Remove(next);
        }

        return ordered;
    }

    /// <summary>Every SQL text of a context that may ask a function: its rules', its access functions' and what its contributions write.</summary>
    private static IEnumerable<string> AskedByAnything(Prepared prepared)
        => prepared.Rules.Select(rule => rule.Sql)
            .Concat(prepared.Functions.Select(function => function.Sql))
            .Concat(prepared.Contributions.SelectMany(contributed =>
                contributed.Result.Functions.Select(function => function.Body)
                    .Concat(contributed.Result.Policies.Select(policy => policy.Using + " " + policy.WithCheck))
                    .Concat(contributed.Result.Statements)));

    /// <summary>The circle <paramref name="next"/> leads round from <paramref name="start"/>, its first step repeated at its end.</summary>
    private static List<T> Circle<T>(T start, Func<T, T> next) where T : notnull
    {
        var path = new List<T> { start };
        for (var step = next(start); ; step = next(step))
        {
            var seen = path.IndexOf(step);
            if (seen >= 0)
            {
                return [.. path.Skip(seen), step];
            }

            path.Add(step);
        }
    }

    /// <summary>The parts of a list separated by commas outside parentheses, trimmed: <c>key text, amount numeric(10, 2)</c> is two.</summary>
    private static List<string> TopLevel(string list)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < list.Length; i++)
        {
            depth += list[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (list[i] == ',' && depth == 0)
            {
                parts.Add(list[start..i].Trim());
                start = i + 1;
            }
        }

        parts.Add(list[start..].Trim());
        return parts.Where(part => part.Length > 0).ToList();
    }

    /// <summary>
    /// Every table of <paramref name="root"/>'s entities with a table of its own, and the way up to the root, as
    /// <see cref="RowAccessModel.EntityTablesOf"/> answers.
    /// </summary>
    internal static IReadOnlyList<EntityTableChain> EntityTableChainsOf(IEntityType root)
    {
        var table = RowAccessModel.TableOf(root);
        return [.. EntitiesOf(root, table, []).Select(each => new EntityTableChain(
            each.Entity,
            Quote(each.Table.Schema ?? DefaultSchema) + "." + Quote(each.Table.Name),
            [.. each.Chain.Select(link => new EntityTableLink(
                Quote(link.Parent.Schema ?? DefaultSchema) + "." + Quote(link.Parent.Name),
                [.. link.Key.Select(column => Quote(column.Parent))],
                [.. link.Key.Select(column => Quote(column.Child))]))]))];
    }

    /// <summary>
    /// Whether <paramref name="returns"/> is what a function can return: a type, <c>SETOF</c> one, or rows of
    /// named columns, <c>TABLE ("Id" uuid, due timestamp with time zone)</c>, each a name, in double quotes or
    /// plain, and a type.
    /// </summary>
    private static bool IsReturned(string returns)
    {
        if (Returns().IsMatch(returns))
        {
            return true;
        }

        return ReturnsTable().Match(returns) is { Success: true } table
            && TopLevel(table.Groups["columns"].Value) is { Count: > 0 } columns
            && columns.All(column => ReturnedColumn().IsMatch(column));
    }

    /// <summary>
    /// Whether <paramref name="body"/> is one <c>SELECT</c>, which may start with <c>WITH</c>: what it starts
    /// with, after any comments, and with no statement after it.
    /// </summary>
    private static bool IsOneSelect(string body)
    {
        // Comments go, and what is quoted is emptied: a semicolon in either ends nothing.
        var sql = QuotedOrComment().Replace(body, match => IsComment(match.Value) ? " " : "''").Trim().TrimEnd(';').TrimEnd();
        return StartsASelect().IsMatch(sql) && !sql.Contains(';', StringComparison.Ordinal);
    }

    /// <summary>
    /// A string literal, a quoted name, or a comment, from <c>--</c> to the end of its line or between <c>/*</c>
    /// and <c>*/</c>: whichever starts first, so a quote in a comment opens nothing, and <c>--</c> in a literal
    /// is no comment. A literal is also one with escapes, <c>E'it\'s'</c>, in which a backslash escapes the quote
    /// after it, and one between dollar quotes, <c>$$it's$$</c> or <c>$tag$...$tag$</c>, in which nothing is
    /// special. A comment inside a comment ends with the inner one's end: Postgres nests them, and this does not.
    /// </summary>
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_$])[Ee]'(?:[^'\\]|\\.|'')*'|'(?:[^']|'')*'|""(?:[^""]|"""")*""|\$(?<tag>(?:[A-Za-z_][A-Za-z0-9_]*)?)\$.*?\$\k<tag>\$|--[^\r\n]*|/\*.*?\*/",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex QuotedOrComment();

    /// <summary><c>SELECT</c> or <c>WITH</c>, as the word a statement starts with, possibly in parentheses.</summary>
    [GeneratedRegex(@"^(\(\s*)*(SELECT|WITH)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex StartsASelect();

    /// <summary>
    /// What a function returns: an SQL type, possibly with its schema, its length and <c>[]</c>, or <c>SETOF</c>
    /// one. A name of more than one word is one of the types Postgres spells so, never a word that would be
    /// read as an option of the function, such as <c>SECURITY DEFINER</c>.
    /// </summary>
    [GeneratedRegex(
        @"^(SETOF\s+)?(double\s+precision|character\s+varying|bit\s+varying|(timestamp|time)(\s*\([0-9]+\))?\s+with(out)?\s+time\s+zone|[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?)(\s*\([0-9]+(\s*,\s*[0-9]+)?\))?(\[\])*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Returns();

    /// <summary><c>TABLE (...)</c>, with its columns between the parentheses.</summary>
    [GeneratedRegex(@"^TABLE\s*\((?<columns>.*)\)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ReturnsTable();

    /// <summary>
    /// One column a function returns rows of: a name, in double quotes or plain, and a type as
    /// <see cref="Returns"/> takes one, never a set.
    /// </summary>
    [GeneratedRegex(
        @"^(""[^""]+""|[A-Za-z_][A-Za-z0-9_]*)\s+(double\s+precision|character\s+varying|bit\s+varying|(timestamp|time)(\s*\([0-9]+\))?\s+with(out)?\s+time\s+zone|[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?)(\s*\([0-9]+(\s*,\s*[0-9]+)?\))?(\[\])*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ReturnedColumn();

    [GeneratedRegex(@"\bSECURITY\s+DEFINER\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Definer();

    [GeneratedRegex(@"\bRETURNS\s+trigger\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ReturnsTrigger();

    /// <summary><c>SET search_path = ''</c>, an empty path and nothing after it.</summary>
    [GeneratedRegex(@"\bSET\s+search_path\s*(=|TO)\s*''(?!\s*,)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex EmptySearchPath();
}
