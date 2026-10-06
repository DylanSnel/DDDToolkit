using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.HotChocolate.Fusion.InMemory;
using FluentAssertions;
using Microsoft.Extensions.Hosting;

namespace Examples.Tenancy.Tests.GraphQL;

/// <summary>
/// How a tool reads the host's GraphQL schemas where the host runs for real, outside Development: GraphQL Codegen, the
/// Relay compiler's schema download, a build step that writes <c>schema.graphql</c>. It has no user to sign in as, so
/// it sends the key the host keeps at <c>GraphQL:SchemaKey</c>, in <c>X-GraphQL-Schema-Key</c>, and reads each
/// gateway's schema, and nothing else. Without the key the schema is refused.
/// </summary>
/// <remarks>
/// The host runs in Production, without a database: reading a schema reads no row. Its key comes in as configuration,
/// as a build server's secret does, and the host's code names none.
/// </remarks>
public sealed class GraphQLSchemaKeyTests(GraphQLSchemaKeyTests.InProduction host) : IClassFixture<GraphQLSchemaKeyTests.InProduction>
{
    /// <summary>What the build server's secret holds: 44 characters, what <c>openssl rand -base64 32</c> writes.</summary>
    private const string Key = "n3Rk8Tq1Wv6Yb9Lc2Xz5Md7Pf4Gh0Js8Ae1Ui6Oy3Qw=";

    /// <summary>The query GraphQL Codegen sends to read a schema, as graphql-js's getIntrospectionQuery() writes it, in short.</summary>
    private const string Introspection = """
        query IntrospectionQuery {
          __schema {
            queryType { name } mutationType { name } subscriptionType { name }
            types { ...FullType }
            directives { name locations args { ...InputValue } }
          }
        }
        fragment FullType on __Type {
          kind name
          fields(includeDeprecated: true) { name args { ...InputValue } type { ...TypeRef } isDeprecated deprecationReason }
          inputFields { ...InputValue } interfaces { ...TypeRef }
          enumValues(includeDeprecated: true) { name isDeprecated deprecationReason } possibleTypes { ...TypeRef }
        }
        fragment InputValue on __InputValue { name type { ...TypeRef } defaultValue }
        fragment TypeRef on __Type {
          kind name ofType { kind name ofType { kind name ofType { kind name ofType { kind name ofType { kind name ofType { kind name ofType { kind name } } } } } } }
        }
        """;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(SampleGateways.UserPath + "/schema.graphql", "schema.graphql")]
    [InlineData(SampleGateways.AdministrationPath + "?sdl", "admin.graphql")]
    public async Task With_the_key_a_tool_downloads_each_gateways_schema_and_it_is_the_committed_one(string path, string committed)
    {
        using var client = host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(GraphQLSchemaKey.HeaderName, Key);

        using var response = await client.SendAsync(request, Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var file = Path.Combine(Path.GetDirectoryName(SampleLayout.HostProjectFile())!, committed);
        Normalized(await response.Content.ReadAsStringAsync(Cancellation)).Should().Be(Normalized(await File.ReadAllTextAsync(file, Cancellation)), "a tool reads what GraphQLSchemaTests holds the gateway to");
    }

    [Fact]
    public async Task With_the_key_a_tool_introspects_each_gateway()
    {
        using var client = host.Client();

        foreach (var gateway in new[] { SampleGateways.UserPath, SampleGateways.AdministrationPath })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, gateway) { Content = JsonContent.Create(new { query = Introspection, operationName = "IntrospectionQuery" }) };
            request.Headers.Add(GraphQLSchemaKey.HeaderName, Key);

            using var response = await client.SendAsync(request, Cancellation);
            var answer = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);

            answer.TryGetProperty("errors", out _).Should().BeFalse("{0} answered {1}", gateway, answer.GetRawText());
            answer.GetProperty("data").GetProperty("__schema").GetProperty("types").EnumerateArray()
                .Select(type => type.GetProperty("name").GetString()).Should().Contain(["Project", "Seat"], "the gateway composes Projects and a Tenancy");
        }
    }

    [Fact]
    public async Task Without_the_key_the_schema_is_refused_outside_Development()
    {
        using var client = host.Client();

        foreach (var gateway in new[] { SampleGateways.UserPath, SampleGateways.AdministrationPath })
        {
            using var file = await client.GetAsync(gateway + "?sdl", Cancellation);
            using var introspection = await client.PostAsJsonAsync(gateway, new { query = Introspection }, Cancellation);

            file.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "{0} requires a token, and no key stands in for it", gateway);
            introspection.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // A key that is not the host's is no key.
        using var wrong = new HttpRequestMessage(HttpMethod.Get, SampleGateways.UserPath + "?sdl");
        wrong.Headers.Add(GraphQLSchemaKey.HeaderName, Key.Replace('n', 'm'));
        using var refused = await client.SendAsync(wrong, Cancellation);
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_key_reads_the_schema_and_no_data()
    {
        using var client = host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, SampleGateways.UserPath) { Content = JsonContent.Create(new { query = "{ projects { nodes { id } } }" }) };
        request.Headers.Add(GraphQLSchemaKey.HeaderName, Key);

        using var response = await client.SendAsync(request, Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a query of data needs a token, key or no key");
    }

    private static string Normalized(string schema) => schema.ReplaceLineEndings("\n").Trim();

    /// <summary>The host in Production, without a database, with the key a build server would hand it.</summary>
    public sealed class InProduction : IAsyncDisposable
    {
        private readonly SampleFactory _host = SampleFactory.WithoutDatabase(
            environment: Environments.Production,
            settings: new Dictionary<string, string>
            {
                // Where the tokens the host accepts come from: it starts nowhere without it.
                [SampleAuthentication.UrlSetting] = "http://127.0.0.1:54321",
                [GraphQLSchemaKey.Setting] = Key,
            });

        /// <summary>A client of the host, which starts on the first.</summary>
        public HttpClient Client() => _host.CreateClient();

        /// <inheritdoc />
        public ValueTask DisposeAsync() => _host.DisposeAsync();
    }
}
