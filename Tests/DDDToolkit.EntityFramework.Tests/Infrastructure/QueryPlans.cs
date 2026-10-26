using System.Text.Json;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Reads what Postgres says it does with a query, from <c>EXPLAIN (..., FORMAT JSON)</c>: every node of the
/// plan, and whether it sits under an InitPlan, which Postgres runs once before the rest of the statement.
/// </summary>
public static class QueryPlans
{
    /// <summary>One node of a plan: its JSON, and whether it runs inside an InitPlan.</summary>
    public sealed record Node(JsonElement Json, bool InInitPlan)
    {
        public string Type => Text("Node Type") ?? "";

        public string? Relation => Text("Relation Name");

        public string? Filter => Text("Filter");

        public string? IndexCondition => Text("Index Cond") ?? Text("Recheck Cond");

        public string? ParentRelationship => Text("Parent Relationship");

        /// <summary>The index the node reads, for a scan of one.</summary>
        public string? Index => Text("Index Name");

        /// <summary>The function the node calls for its rows, for a scan of one.</summary>
        public string? Function => Text("Function Name");

        /// <summary>The expressions the node puts out, which <c>VERBOSE</c> lists: a function a node calls is named there.</summary>
        public IReadOnlyList<string> Output
            => Json.TryGetProperty("Output", out var output) && output.ValueKind == JsonValueKind.Array
                ? [.. output.EnumerateArray().Select(item => item.GetString() ?? "")]
                : [];

        public long Number(string property) => Json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;

        private string? Text(string property) => Json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>Runs <c>EXPLAIN (<paramref name="options"/>, FORMAT JSON) <paramref name="query"/></c> on <paramref name="connection"/>.</summary>
    public static async Task<JsonElement> ExplainAsync(NpgsqlConnection connection, string query, string options, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"EXPLAIN ({options}, FORMAT JSON) {query}", connection);
        var json = (string)(await command.ExecuteScalarAsync(cancellationToken))!;
        using var document = JsonDocument.Parse(json);
        return document.RootElement[0].GetProperty("Plan").Clone();
    }

    /// <summary>Every node of the plan, the root first.</summary>
    public static IReadOnlyList<Node> Nodes(JsonElement plan)
    {
        var nodes = new List<Node>();
        Walk(plan, inInitPlan: false, nodes);
        return nodes;
    }

    /// <summary>The InitPlans of the plan whose subtree calls <paramref name="function"/>, named as <c>schema.name(</c>.</summary>
    public static IReadOnlyList<Node> InitPlansCalling(JsonElement plan, string function)
    {
        var found = new List<Node>();
        foreach (var node in Nodes(plan).Where(node => node.ParentRelationship == "InitPlan"))
        {
            if (Nodes(node.Json).Any(inner => inner.Output.Any(output => output.Contains(function, StringComparison.Ordinal))))
            {
                found.Add(node);
            }
        }

        return found;
    }

    /// <summary>The nodes that read <paramref name="relation"/>, outside every InitPlan.</summary>
    public static IReadOnlyList<Node> Scans(JsonElement plan, string relation)
        => [.. Nodes(plan).Where(node => !node.InInitPlan && node.Relation == relation)];

    /// <summary>Whether a scan finds its rows through an index: an index scan, or a bitmap heap scan over one.</summary>
    public static bool UsesAnIndex(Node scan) => scan.Type is "Index Scan" or "Index Only Scan" or "Bitmap Heap Scan";

    private static void Walk(JsonElement node, bool inInitPlan, List<Node> nodes)
    {
        var here = inInitPlan || (node.TryGetProperty("Parent Relationship", out var relationship) && relationship.GetString() == "InitPlan");
        nodes.Add(new Node(node, here));
        if (node.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                Walk(child, here, nodes);
            }
        }
    }
}
