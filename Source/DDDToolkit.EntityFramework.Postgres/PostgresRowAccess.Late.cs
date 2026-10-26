using System.Text;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Postgres;

public static partial class PostgresRowAccess
{
    /// <summary>
    /// A function a rule of one context asks that another context writes, where that other context asks the first's
    /// functions in turn: a read rule on Tenancy's seats that asks who works on the same projects, say, which the
    /// projects' module answers with a function that asks who the calling seat is of Tenancy. Neither file can run
    /// first, since Postgres refuses a policy that asks a function that does not exist yet. So the asking context
    /// writes a function of its own, <see cref="Resolved"/>, that asks <see cref="Target"/> when it runs, and its
    /// policies ask that one: it is made without its body being checked, before the other file has run.
    /// </summary>
    /// <param name="Target">The function the rule asks, as the other context writes it.</param>
    /// <param name="Resolved">The asking context's own function that asks it.</param>
    /// <param name="Logicals">The names the rules ask it by, a logical name or the name of a resource access set.</param>
    /// <param name="Function">What the other context's contribution writes, whose parameters and result it takes.</param>
    /// <param name="DefinedBy">The context that writes <see cref="Target"/>, by its type's name.</param>
    private sealed record LateFunction(string Target, string Resolved, IReadOnlyList<string> Logicals, ContributedFunction Function, string DefinedBy);

    /// <summary>
    /// The functions each context's rules ask of a context that asks its functions in turn, so that each file can still
    /// run after every one it needs: only those a rule asks, a contribution writes, and nothing else of the asking
    /// context asks. Every other circle is left for <c>InDependencyOrder</c> to refuse.
    /// </summary>
    private static Dictionary<Prepared, List<LateFunction>> LateAsked(IReadOnlyList<Prepared> prepared, IReadOnlyDictionary<string, string> names)
    {
        var definedBy = new Dictionary<string, Prepared>(RowAccessNames.Functions);
        foreach (var each in prepared)
        {
            foreach (var named in each.Names)
            {
                definedBy.TryAdd(named.Resolved, each);
            }
        }

        // Whom each context waits for, every function it asks counted.
        var waits = prepared.ToDictionary(
            each => each,
            each => AskedByAnything(each)
                .SelectMany(sql => Asks(sql, names))
                .Where(definedBy.ContainsKey)
                .Select(asked => definedBy[asked])
                .Where(definer => !ReferenceEquals(definer, each))
                .ToHashSet());

        var late = prepared.ToDictionary(each => each, _ => new List<LateFunction>());
        foreach (var each in prepared)
        {
            var own = each.Names.Select(named => named.Resolved).ToHashSet(RowAccessNames.Functions);
            var askedOtherwise = each.Functions.Select(function => function.Sql)
                .Concat(each.Contributions.SelectMany(contributed =>
                    contributed.Result.Functions.Select(function => function.Body)
                        .Concat(contributed.Result.Policies.Select(SqlOf))
                        .Concat(contributed.Result.Statements)))
                .SelectMany(sql => Asks(sql, names))
                .ToHashSet(RowAccessNames.Functions);

            var byTarget = new Dictionary<string, LateFunction>(RowAccessNames.Functions);
            foreach (var logical in each.Rules.SelectMany(rule => FunctionsAskedBy(rule.Sql)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!names.TryGetValue(logical, out var target)
                    || own.Contains(target)
                    || askedOtherwise.Contains(target)
                    || !definedBy.TryGetValue(target, out var definer)
                    || !Reaches(definer, each, waits)
                    || ContributedFunctionOf(definer, target) is not { } function)
                {
                    continue;
                }

                if (byTarget.TryGetValue(target, out var found))
                {
                    byTarget[target] = found with { Logicals = [.. found.Logicals, logical] };
                    continue;
                }

                var schema = FunctionSchema(each.Context, each.Context.Model.GetDefaultSchema() ?? DefaultSchema, logical);
                var resolved = schema + "." + function.Name;
                if (own.Contains(resolved))
                {
                    throw new InvalidOperationException(
                        $"A rule of {each.Context.GetType().Name} asks {target}, which {definer.Context.GetType().Name} writes in a file that runs after this one, so the policies here ask it through {resolved}, which {each.Context.GetType().Name} defines already. Give its own function another name.");
                }

                byTarget[target] = new LateFunction(target, resolved, [logical], function, definer.Context.GetType().Name);
            }

            late[each].AddRange(byTarget.Values.OrderBy(function => function.Resolved, StringComparer.Ordinal));
        }

        return late;
    }

    /// <summary>Whether <paramref name="from"/> waits, itself or through the contexts it waits for, for <paramref name="to"/>.</summary>
    private static bool Reaches(Prepared from, Prepared to, IReadOnlyDictionary<Prepared, HashSet<Prepared>> waits)
    {
        var seen = new HashSet<Prepared>();
        var next = new Stack<Prepared>([from]);
        while (next.TryPop(out var each))
        {
            if (ReferenceEquals(each, to))
            {
                return true;
            }

            if (seen.Add(each))
            {
                foreach (var waited in waits[each])
                {
                    next.Push(waited);
                }
            }
        }

        return false;
    }

    /// <summary>The function a contribution writes for <paramref name="definer"/> as <paramref name="resolved"/>, or null for one it does not.</summary>
    private static ContributedFunction? ContributedFunctionOf(Prepared definer, string resolved)
    {
        var logical = definer.Names
            .FirstOrDefault(named => !ResourceAccessAnswer.IsName(named.Logical) && RowAccessNames.Functions.Equals(named.Resolved, resolved))?.Logical;
        return logical is null
            ? null
            : definer.Contributions
                .SelectMany(contributed => contributed.Result.Functions.Select(function => (Logical: contributed.Owner + "/" + function.Name, Function: function)))
                .FirstOrDefault(each => string.Equals(each.Logical, logical, StringComparison.OrdinalIgnoreCase)).Function;
    }

    /// <summary><paramref name="names"/> as a context's policies ask them: a function asked <paramref name="late"/>, by the context's own function that asks it.</summary>
    private static Dictionary<string, string> AskingLate(Dictionary<string, string> names, IReadOnlyList<LateFunction> late)
    {
        foreach (var function in late)
        {
            foreach (var logical in function.Logicals)
            {
                names[logical] = function.Resolved;
            }
        }

        return names;
    }

    /// <summary>
    /// The functions <paramref name="prepared"/> writes for what its rules ask late: each asks the other context's
    /// function by its name when it runs, made with Postgres' check of a function's body turned off for it alone,
    /// since that function is made by a file that runs later. It runs as its caller, who needs the right to execute
    /// the other function, which a script that grants it to the rule's roles refuses to write otherwise.
    /// </summary>
    private static List<Definition> LateDefinitions(Prepared prepared, IReadOnlyList<LateFunction> late)
    {
        var definitions = new List<Definition>();
        foreach (var function in late)
        {
            var parameters = TopLevel(function.Function.Parameters);
            var signature = function.Resolved + "(" + string.Join(", ", parameters) + ")";
            var arguments = string.Join(", ", Enumerable.Range(1, parameters.Count).Select(index => "$" + index));
            var returns = function.Function.Returns.Trim();
            var body = returns.StartsWith("SETOF ", StringComparison.OrdinalIgnoreCase) || returns.StartsWith("TABLE", StringComparison.OrdinalIgnoreCase)
                ? $"SELECT * FROM {function.Target}({arguments})"
                : $"SELECT {function.Target}({arguments})";
            var grantTo = prepared.Rules
                .Where(rule => FunctionsAskedBy(rule.Sql).Any(asked => function.Logicals.Contains(asked, StringComparer.OrdinalIgnoreCase)))
                .SelectMany(rule => prepared.Roles[rule])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            var statement = new StringBuilder()
                .Append('\n')
                .Append("-- ").Append(function.Target).Append(" is written by ").Append(function.DefinedBy).Append(", whose file runs after this one, since it asks a function of ")
                .Append(prepared.Context.GetType().Name).Append(" in turn.").Append('\n')
                .Append("-- A policy cannot ask a function that does not exist yet, so the policies here ask it through this one, which finds it when it runs.").Append('\n')
                .Append("SET check_function_bodies = off;").Append('\n')
                .Append("CREATE OR REPLACE FUNCTION ").Append(signature).Append(" RETURNS ").Append(returns).Append('\n')
                .Append("    LANGUAGE sql ").Append(function.Function.Volatility.Trim().ToUpperInvariant()).Append(" SET search_path = '' AS $function$").Append('\n')
                .Append(body).Append('\n')
                .Append("$function$;").Append('\n')
                .Append("RESET check_function_bodies;").Append('\n')
                .Append("COMMENT ON FUNCTION ").Append(signature).Append(" IS ").Append(Literal(FunctionComment(prepared.Context))).Append(";\n")
                .ToString();

            definitions.Add(new Definition(function.Logicals[0], function.Resolved, signature, statement, [], grantTo));
        }

        return definitions;
    }
}
