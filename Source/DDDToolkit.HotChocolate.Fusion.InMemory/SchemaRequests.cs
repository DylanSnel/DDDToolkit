using System.Buffers;
using System.Text.Json;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;

namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// Tells a request that reads a gateway's schema, and nothing else, from every other: the schema file, or an
/// operation whose every root field is <c>__schema</c>, <c>__type</c> or <c>__typename</c>.
/// </summary>
/// <remarks>
/// <para>
/// What it lets through is let past the endpoint's authorization, so it answers yes only for a request that
/// HotChocolate cannot take for anything else: one it would answer from the schema alone, however it reads it. A
/// request it cannot be sure of is no schema request, and the endpoint's authorization decides it as it decides
/// any other. So it refuses what it would have to guess at: a document with any operation that is not
/// introspection only (whichever one the request names, every one is), a fragment that ends in a field of the
/// schema, a field that is aliased <c>__schema</c> (the field's own name counts), a JSON body with a property twice
/// or one HotChocolate reads that it does not know (a persisted operation's id, a batch), a body larger than any
/// introspection query, a query string on a post, a form, a socket.
/// </para>
/// <para>
/// The schema file is asked for as HotChocolate's endpoint recognizes it: a <c>GET</c> with <c>?sdl</c>, or of
/// <c>/schema</c>, <c>/schema/</c> or <c>/schema.graphql</c> under the endpoint's path. Whether HotChocolate runs an
/// operation such a <c>GET</c> carries as well, in <c>query</c>, <c>id</c> or <c>extensions</c>, or serves the file
/// regardless, depends on its server options (<c>EnableGetRequests</c>, <c>EnforceGetRequestsPreflightHeader</c>).
/// So the two directions take it differently: whoever may not read the schema is refused every <c>GET</c> that asks
/// for the file (<see cref="AsksForSchemaFile"/>), whatever else it carries, and only one that carries nothing else
/// is let past authorization as the file (<see cref="IsSchemaFile"/>).
/// </para>
/// </remarks>
internal static class SchemaRequests
{
    /// <summary>
    /// The largest body read: HotChocolate's own introspection query is under 2 KiB, GraphQL Codegen's under 3. A
    /// larger one is no schema request, and is not read.
    /// </summary>
    internal const int LargestBody = 32 * 1024;

    private static readonly string[] OperationParameters = ["query", "id", "extensions"];

    /// <summary>
    /// Whether HotChocolate could answer <paramref name="request"/> with the schema file of the endpoint at
    /// <paramref name="path"/>: what its own schema middleware takes, whatever else the request carries.
    /// </summary>
    public static bool AsksForSchemaFile(HttpRequest request, PathString path)
        => HttpMethods.IsGet(request.Method)
           && (request.Query.ContainsKey("sdl") || IsSchemaPath(request.Path, path));

    /// <summary>
    /// Whether <paramref name="request"/> asks for the schema file of the endpoint at <paramref name="path"/> and for
    /// nothing else: no operation beside it, and no socket.
    /// </summary>
    public static bool IsSchemaFile(HttpRequest request, PathString path)
        => AsksForSchemaFile(request, path)
           && !request.HttpContext.WebSockets.IsWebSocketRequest
           && !OperationParameters.Any(request.Query.ContainsKey);

    /// <summary>
    /// Whether <paramref name="context"/> reads the schema of the endpoint at <paramref name="path"/> and nothing
    /// else. A body it reads is left where HotChocolate reads it from, at its start.
    /// </summary>
    public static async ValueTask<bool> IsSchemaRequestAsync(HttpContext context, PathString path)
    {
        var request = context.Request;
        if (context.WebSockets.IsWebSocketRequest)
        {
            return false;
        }

        if (HttpMethods.IsGet(request.Method))
        {
            return OperationParameters.Any(request.Query.ContainsKey) ? IsIntrospectionGet(request.Query) : IsSchemaFile(request, path);
        }

        if (!HttpMethods.IsPost(request.Method)
            || request.Query.Count > 0
            || request.ContentType?.StartsWith("application/json", StringComparison.Ordinal) != true
            || request.ContentLength > LargestBody)
        {
            return false;
        }

        return await ReadAsync(request, context.RequestAborted).ConfigureAwait(false) is { } body && IsIntrospectionBody(body);
    }

    /// <summary>An operation sent as a <c>GET</c>: a query, and its name and variables, each once.</summary>
    private static bool IsIntrospectionGet(IQueryCollection parameters)
        => parameters.All(parameter => parameter.Key is "query" or "operationName" or "variables" && parameter.Value.Count == 1)
           && parameters["query"] is [{ } query]
           && IsIntrospectionOnly(query);

    /// <summary>
    /// A JSON object with a query, and at most an operation name, variables and extensions besides, each once.
    /// Extensions that name a persisted query would have HotChocolate run a stored document, so they make it none.
    /// </summary>
    private static bool IsIntrospectionBody(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var json = JsonDocument.Parse(body, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 });
            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? query = null;
            foreach (var property in json.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    return false;
                }

                var known = property.Name switch
                {
                    "query" => property.Value.ValueKind == JsonValueKind.String,
                    "operationName" => property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null,
                    "variables" => property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null,
                    "extensions" => property.Value.ValueKind == JsonValueKind.Null
                                    || (property.Value.ValueKind == JsonValueKind.Object
                                        && !property.Value.EnumerateObject().Any(extension => string.Equals(extension.Name, "persistedQuery", StringComparison.OrdinalIgnoreCase))),
                    _ => false,
                };

                if (!known)
                {
                    return false;
                }

                if (property.Name == "query")
                {
                    query = property.Value.GetString();
                }
            }

            return query is not null && IsIntrospectionOnly(query);
        }
        catch (Exception unreadable) when (unreadable is JsonException or InvalidOperationException)
        {
            // Not JSON, or JSON whose names and strings are no text: a lone surrogate, bytes that are no UTF-8. The
            // reader takes such a string, and fails only where it is read as one.
            return false;
        }
    }

    /// <summary>Whether every operation of <paramref name="document"/> is a query of introspection fields only.</summary>
    internal static bool IsIntrospectionOnly(string document)
    {
        DocumentNode parsed;
        try
        {
            parsed = Utf8GraphQLParser.Parse(document);
        }
        catch (Exception unreadable) when (unreadable is SyntaxException or ArgumentException)
        {
            // No document, or an empty one, which the parser refuses with an ArgumentException.
            return false;
        }

        var operations = parsed.Definitions.OfType<OperationDefinitionNode>().ToList();
        var fragments = parsed.Definitions.OfType<FragmentDefinitionNode>().ToList();
        if (operations.Count == 0
            || operations.Count + fragments.Count != parsed.Definitions.Count
            || fragments.DistinctBy(fragment => fragment.Name.Value, StringComparer.Ordinal).Count() != fragments.Count)
        {
            return false;
        }

        var byName = fragments.ToDictionary(fragment => fragment.Name.Value, StringComparer.Ordinal);
        return operations.All(operation => operation.Operation == OperationType.Query && OnlyIntrospection(operation.SelectionSet, byName, []));
    }

    /// <summary>
    /// Whether a root selection holds introspection fields only, through the fragments it spreads. A fragment that
    /// spreads itself, directly or not, makes it none: HotChocolate refuses such a document anyway.
    /// </summary>
    private static bool OnlyIntrospection(SelectionSetNode selections, IReadOnlyDictionary<string, FragmentDefinitionNode> fragments, HashSet<string> spreading)
    {
        foreach (var selection in selections.Selections)
        {
            var introspection = selection switch
            {
                FieldNode field => field.Name.Value is "__schema" or "__type" or "__typename",
                InlineFragmentNode inline => OnlyIntrospection(inline.SelectionSet, fragments, spreading),
                FragmentSpreadNode spread => Spreads(spread.Name.Value, fragments, spreading),
                _ => false,
            };

            if (!introspection)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Spreads(string name, IReadOnlyDictionary<string, FragmentDefinitionNode> fragments, HashSet<string> spreading)
    {
        if (!fragments.TryGetValue(name, out var fragment) || !spreading.Add(name))
        {
            return false;
        }

        try
        {
            return OnlyIntrospection(fragment.SelectionSet, fragments, spreading);
        }
        finally
        {
            spreading.Remove(name);
        }
    }

    private static bool IsSchemaPath(PathString requested, PathString path)
        => requested.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase, out var remaining)
           && (remaining.Equals("/schema", StringComparison.OrdinalIgnoreCase)
               || remaining.Equals("/schema/", StringComparison.OrdinalIgnoreCase)
               || remaining.Equals("/schema.graphql", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The body, buffered so HotChocolate reads it again from its start; <see langword="null"/> when it is larger
    /// than <see cref="LargestBody"/>, which is then not read further.
    /// </summary>
    private static async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        var buffer = ArrayPool<byte>.Shared.Rent(LargestBody + 1);
        try
        {
            var length = 0;
            int read;
            while (length <= LargestBody
                   && (read = await request.Body.ReadAsync(buffer.AsMemory(length, LargestBody + 1 - length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                length += read;
            }

            return length > LargestBody ? null : buffer.AsSpan(0, length).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            request.Body.Position = 0;
        }
    }
}
