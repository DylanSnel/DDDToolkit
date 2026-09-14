using DDDToolkit.ExampleLibrary.GraphQl;
using DDDToolkit.HotChocolate.Tests.Domain;
using DDDToolkit.HotChocolate.Tests.GraphQl;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// Builds the schema the tests run against, in memory: no web host, no HTTP, just an
/// <see cref="IRequestExecutor"/> resolved out of a plain <see cref="ServiceCollection"/>.
/// </summary>
internal static class TestSchema
{
    /// <summary>
    /// The full schema: the toolkit's conventions, the runtime bindings generated for the example
    /// library and for this test assembly, and <see cref="Query"/> as the query root.
    /// </summary>
    public static ValueTask<IRequestExecutor> BuildExecutorAsync()
        => new ServiceCollection()
            .AddGraphQL()
            .AddDDDToolkitTypes()
            .AddCommonGraphQlRuntimeBindings()
            .AddGraphQlTestsGraphQlRuntimeBindings()
            .AddQueryType<Query>()
            .BuildRequestExecutorAsync(cancellationToken: Cancellation);

    /// <summary>The running test's cancellation token.</summary>
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The printed SDL of that schema.</summary>
    public static async Task<string> PrintSchemaAsync()
    {
        var executor = await BuildExecutorAsync();
        return executor.Schema.ToString();
    }

    /// <summary>
    /// Executes <paramref name="query"/> and returns its <c>data</c>, failing the test if the
    /// response carries any GraphQL error.
    /// </summary>
    public static async Task<JsonElement> QueryDataAsync(
        string query,
        IReadOnlyDictionary<string, object?>? variables = null)
    {
        var executor = await BuildExecutorAsync();

        var result = variables is null
            ? await executor.ExecuteAsync(query, Cancellation)
            : await executor.ExecuteAsync(query, variables, Cancellation);

        return ReadData(result);
    }

    /// <summary>Executes <paramref name="query"/> and returns the raw JSON response, errors and all.</summary>
    public static async Task<JsonElement> QueryRawAsync(string query)
    {
        var executor = await BuildExecutorAsync();
        var result = await executor.ExecuteAsync(query, Cancellation);
        return Parse(result.ToJson());
    }

    private static JsonElement ReadData(IExecutionResult result)
    {
        var json = Parse(result.ToJson());

        if (json.TryGetProperty("errors", out var errors))
        {
            Assert.Fail("The GraphQL request failed: " + errors.ToString());
        }

        return json.GetProperty("data");
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
