using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// Asking the host's GraphQL schema the way a client does: a document posted to <c>/graphql</c>, the gateway over
/// the modules' schemas, with the caller's token and <c>Tenant</c> header.
/// </summary>
public static class SampleGraphQLCalls
{
    /// <summary>The selection that reads any of a mutation's typed errors: its type, its code and what kind of refusal it is.</summary>
    public const string Errors = "errors { __typename ... on CodedError { code message } ... on RefusalError { kind } }";

    /// <summary>Where the tenant's administration is served: its own gateway, beside the user's at <c>/graphql</c>.</summary>
    public const string Administration = "/admin/graphql";

    /// <summary>Posts a document and returns the whole answer, <c>data</c> and <c>errors</c> alike.</summary>
    public static async Task<JsonElement> GraphQLAsync(this HttpClient client, string document, object? variables = null)
        => await client.GraphQLAsync("/graphql", document, variables);

    /// <summary>Posts a document to the schema at <paramref name="path"/> and returns the whole answer.</summary>
    public static async Task<JsonElement> GraphQLAsync(this HttpClient client, string path, string document, object? variables)
    {
        using var response = await client.PostAsJsonAsync(path, new { query = document, variables }, TestContext.Current.CancellationToken);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    /// <summary>Posts a document to the administration's schema and returns its <c>data</c>, failing the test when the answer carries errors.</summary>
    public static async Task<JsonElement> AdministrationDataAsync(this HttpClient client, string document, object? variables = null)
    {
        var answer = await client.GraphQLAsync(Administration, document, variables);
        answer.TryGetProperty("errors", out _).Should().BeFalse("the administration's schema answered {0}", answer.GetRawText());
        return answer.GetProperty("data");
    }

    /// <summary>Posts a document and returns its <c>data</c>, failing the test when the answer carries errors.</summary>
    public static async Task<JsonElement> GraphQLDataAsync(this HttpClient client, string document, object? variables = null)
    {
        var answer = await client.GraphQLAsync(document, variables);
        answer.TryGetProperty("errors", out _).Should().BeFalse("the gateway answered {0}", answer.GetRawText());
        return answer.GetProperty("data");
    }

    /// <summary>The one top-level error of an answer: what a refused query answers.</summary>
    public static JsonElement SingleError(this JsonElement answer)
    {
        answer.TryGetProperty("errors", out var errors).Should().BeTrue("the gateway answered {0}", answer.GetRawText());
        return errors.EnumerateArray().Should().ContainSingle("the gateway answered {0}", answer.GetRawText()).Subject;
    }

    /// <summary>The <c>code</c> a refused query's error carries in its extensions.</summary>
    public static string? Code(this JsonElement error) => error.GetProperty("extensions").GetProperty("code").GetString();

    /// <summary>The <c>kind</c> a refused query's error carries in its extensions.</summary>
    public static string? Kind(this JsonElement error) => error.GetProperty("extensions").GetProperty("kind").GetString();

    /// <summary>
    /// The node id of a demonstration project, as the schema writes it: asked of <c>projects</c>, since a client
    /// does not make node ids itself.
    /// </summary>
    public static async Task<string> ProjectNodeIdAsync(this HttpClient client, DemoProject project)
    {
        var data = await client.GraphQLDataAsync("query($text: String) { projects(text: $text) { nodes { id number } } }", new { text = project.Name });
        return data.GetProperty("projects").GetProperty("nodes").EnumerateArray()
            .Single(item => item.GetProperty("number").GetString() == project.Number)
            .GetProperty("id").GetString()!;
    }
}
