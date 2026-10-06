using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;
using FluentAssertions;
using HotChocolate.Fusion.Configuration;
using Xunit;
using static DDDToolkit.HotChocolate.Fusion.InMemory.Tests.InMemoryFusionGatewayTests;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests;

/// <summary>
/// A tool reads a gateway's schema without a user, the way GraphQL Codegen and the Relay compiler fetch it: with the
/// application's <see cref="GraphQLSchemaKey"/>, outside Development; with nothing in Development. The key reads the
/// schema and nothing else, a request without it reads no schema outside Development, signed in or not, and the key
/// is in no log. A gateway's <see cref="SchemaReaders"/> say otherwise where the host wants otherwise, for the file and
/// introspection alike.
/// </summary>
public sealed class SchemaKeyTests
{
    /// <summary>The application's key, as configuration holds it: 44 characters, what <c>openssl rand -base64 32</c> writes.</summary>
    private const string Key = "q7Vb2LkP9xWm4RtZ8cYn3HsJ6dFg1AeU5oIr0TlKwQ8=";

    /// <summary>A key of the same length that is not the application's.</summary>
    private const string WrongKey = "Zx4Cv7Bn1Mq8Wl2Ek5Rt9Yu3Io6Pa0Sd4Fg7Hj1Kl2=";

    /// <summary>What GraphQL Codegen and graphql-js's getIntrospectionQuery() send, word for word in its parts.</summary>
    private const string CodegenIntrospection = """
        query IntrospectionQuery {
          __schema {
            queryType { name }
            mutationType { name }
            subscriptionType { name }
            types { ...FullType }
            directives { name description locations args { ...InputValue } }
          }
        }

        fragment FullType on __Type {
          kind
          name
          description
          fields(includeDeprecated: true) { name description args { ...InputValue } type { ...TypeRef } isDeprecated deprecationReason }
          inputFields { ...InputValue }
          interfaces { ...TypeRef }
          enumValues(includeDeprecated: true) { name description isDeprecated deprecationReason }
          possibleTypes { ...TypeRef }
        }

        fragment InputValue on __InputValue { name description type { ...TypeRef } defaultValue }

        fragment TypeRef on __Type {
          kind name ofType { kind name ofType { kind name ofType { kind name ofType { kind name ofType { kind name ofType { kind name ofType { kind name } } } } } } }
        }
        """;

    private const string Product = "{ productById(id: 1) { name } }";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_the_key_a_tool_downloads_the_schema_without_a_token()
    {
        await using var app = await StartAsync(Environments.Production);
        var printed = await app.Schemas.PrintGatewayAsync("user", Cancellation);

        foreach (var file in new[] { "/graphql?sdl", "/graphql/schema.graphql", "/admin/graphql?sdl" })
        {
            using var response = await GetAsync(app, file, Key);

            response.StatusCode.Should().Be(HttpStatusCode.OK, "{0} is the schema, and the key reads it", file);
            var sdl = await response.Content.ReadAsStringAsync(Cancellation);
            sdl.Should().Contain("type Query");
            if (!file.StartsWith("/admin", StringComparison.Ordinal))
            {
                sdl.ReplaceLineEndings("\n").Trim().Should().Be(printed.ReplaceLineEndings("\n").Trim(), "it is the gateway's composed schema");
            }
        }
    }

    [Fact]
    public async Task With_the_key_a_tool_introspects_the_schema_without_a_token()
    {
        await using var app = await StartAsync(Environments.Production);

        var schema = await app.DataAsync("/graphql", CodegenIntrospection, request: WithKey(Key));
        schema.GetProperty("__schema").GetProperty("queryType").GetProperty("name").GetString().Should().Be("Query");
        schema.GetProperty("__schema").GetProperty("types").EnumerateArray().Select(type => type.GetProperty("name").GetString()).Should().Contain("Product");

        // A fragment spread at the root is read through, and __type and __typename are introspection too.
        var spread = await app.DataAsync("/admin/graphql", "query { ...Root } fragment Root on Query { __typename __type(name: \"Query\") { fields { name } } }", request: WithKey(Key));
        spread.GetProperty("__type").GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()).Should().Contain("stockValue");
    }

    [Fact]
    public async Task Without_the_key_a_request_without_a_token_reads_no_schema()
    {
        await using var app = await StartAsync(Environments.Production);

        using (var file = await GetAsync(app, "/graphql?sdl", key: null))
        {
            file.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the endpoint's authorization challenges it, as it challenges any request without a token");
        }

        using (var introspection = await app.SendAsync("/graphql", CodegenIntrospection))
        {
            introspection.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task A_wrong_key_is_no_key_and_is_logged_as_wrong_without_either_key()
    {
        var logs = new LogRecorder();
        await using var app = await StartAsync(Environments.Production, logs);

        using (var file = await GetAsync(app, "/graphql?sdl", WrongKey))
        {
            file.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var introspection = await app.SendAsync("/graphql", CodegenIntrospection, request: WithKey(WrongKey)))
        {
            introspection.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // Two keys in one request are no key either.
        using (var twice = await GetAsync(app, "/graphql?sdl", Key, WrongKey))
        {
            twice.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        logs.Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Category == typeof(GraphQLSchemaKey).FullName)
            .Should().HaveCount(3, "each wrong key is logged once, as wrong")
            .And.AllSatisfy(entry => entry.Message.Should().Contain("/graphql").And.Contain(GraphQLSchemaKey.HeaderName).And.Contain(GraphQLSchemaKey.Setting));
        logs.Everything.Should().NotContain(WrongKey).And.NotContain(Key);
    }

    [Fact]
    public async Task No_log_of_the_application_holds_the_key_whatever_it_was_asked()
    {
        var logs = new LogRecorder();
        await using var app = await StartAsync(Environments.Production, logs);

        using (await GetAsync(app, "/graphql?sdl", Key))
        using (await GetAsync(app, "/graphql/schema.graphql", Key))
        using (await app.SendAsync("/graphql", CodegenIntrospection, request: WithKey(Key)))
        using (await app.SendAsync("/graphql", Product, request: WithKey(Key)))
        using (await app.SendAsync("/admin/graphql", Product, request: request => { request.As("bob"); WithKey(Key)(request); }))
        {
        }

        logs.Entries.Should().NotBeEmpty("the application logged at every level while it answered");
        logs.Everything.Should().NotContain(Key);
    }

    [Fact]
    public async Task A_signed_in_caller_without_the_key_reads_no_schema_outside_Development()
    {
        await using var app = await StartAsync(Environments.Production);

        using (var file = await GetAsync(app, "/graphql?sdl", key: null, caller: "bob"))
        {
            file.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the schema of an application in production is its tools' to read, not its users'");
        }

        var introspection = await app.PostAsync("/graphql", "{ __schema { queryType { name } } }", request: request => request.As("bob"));
        (introspection.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object).Should().BeFalse("nothing of the schema is answered");
        introspection.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain(GraphQLSchemaKey.HeaderName, "the refusal says what reads the schema");

        // Introspection within an operation is introspection as well.
        var mixed = await app.PostAsync("/graphql", "{ productById(id: 1) { name } __type(name: \"Product\") { name } }", request: request => request.As("bob"));
        mixed.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain(GraphQLSchemaKey.HeaderName);

        // What a signed-in caller asks besides is answered as ever, and __typename is no schema.
        (await app.DataAsync("/graphql", "{ __typename productById(id: 1) { name } }", request: request => request.As("bob")))
            .GetProperty("productById").GetProperty("name").GetString().Should().Be("Coffee");
    }

    [Fact]
    public async Task A_signed_in_caller_with_the_key_reads_the_schema_where_the_endpoint_forbids_him_everything_else()
    {
        await using var app = await StartAsync(Environments.Production);

        // Bob is no administrator: the administration's endpoint forbids him its fields, and its schema reads with the key.
        using (var forbidden = await app.SendAsync("/admin/graphql", "{ stockValue }", request: request => request.As("bob")))
        {
            forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var schema = await app.DataAsync("/admin/graphql", "{ __schema { queryType { name } } }", request: request => { request.As("bob"); WithKey(Key)(request); });
        schema.GetProperty("__schema").GetProperty("queryType").GetProperty("name").GetString().Should().Be("Query");
    }

    [Theory]
    [InlineData("an operation", Product)]
    [InlineData("an operation with introspection beside it", "{ __schema { queryType { name } } productById(id: 1) { name } }")]
    [InlineData("a field aliased __schema", "{ __schema: productById(id: 1) { name } }")]
    [InlineData("a fragment that ends in a field", "query { ...Root } fragment Root on Query { productById(id: 1) { name } }")]
    [InlineData("an inline fragment that does", "query { ... on Query { productById(id: 1) { name } } }")]
    [InlineData("a second operation that is not introspection", "query Schema { __schema { queryType { name } } } query Data { productById(id: 1) { name } }")]
    [InlineData("a mutation", "mutation { __typename }")]
    public async Task The_key_reads_the_schema_and_nothing_else(string what, string document)
    {
        await using var app = await StartAsync(Environments.Production);

        using var response = await app.SendAsync("/graphql", document, request: request =>
        {
            WithKey(Key)(request);
            if (document.Contains("query Schema", StringComparison.Ordinal))
            {
                request.Content = JsonContent.Create(new { query = document, operationName = "Schema" });
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "{0} needs what the endpoint requires, key or no key", what);
    }

    [Theory]
    [InlineData("a body that names its query twice", """{ "query": "{ __schema { queryType { name } } }", "query": "{ productById(id: 1) { name } }" }""")]
    [InlineData("a body whose query is spelled twice", """{ "query": "{ __schema { queryType { name } } }", "Query": "{ productById(id: 1) { name } }" }""")]
    [InlineData("a persisted operation", """{ "id": "abc" }""")]
    [InlineData("a persisted query in the extensions", """{ "query": "{ __typename }", "extensions": { "persistedQuery": { "version": 1, "sha256Hash": "abc" } } }""")]
    [InlineData("a batch", """[ { "query": "{ __schema { queryType { name } } }" } ]""")]
    [InlineData("no JSON", """{ "query": """)]
    [InlineData("a query that is no text", """{ "query": "\ud800" }""")]
    [InlineData("a name that is no text", """{ "\ud800": 1, "query": "{ __typename }" }""")]
    [InlineData("an empty query", """{ "query": "" }""")]
    public async Task A_body_that_could_be_read_two_ways_is_no_schema_request(string what, string body)
    {
        await using var app = await StartAsync(Environments.Production);

        using var message = new HttpRequestMessage(HttpMethod.Post, "/graphql") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        WithKey(Key)(message);
        using var response = await app.Http.SendAsync(message, Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "{0} is decided by the endpoint's authorization", what);
    }

    [Fact]
    public async Task A_body_of_bytes_that_are_no_text_and_a_get_with_an_empty_query_are_no_schema_requests()
    {
        await using var app = await StartAsync(Environments.Production);

        byte[] invalid = [.. Encoding.UTF8.GetBytes("""{ "query": "{ __typename """), 0xC3, 0x28, .. Encoding.UTF8.GetBytes("""}" }""")];
        using var message = new HttpRequestMessage(HttpMethod.Post, "/graphql") { Content = new ByteArrayContent(invalid) };
        message.Content.Headers.ContentType = new("application/json");
        WithKey(Key)(message);
        using (var response = await app.Http.SendAsync(message, Cancellation))
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a body that is no UTF-8 is the endpoint's to decide, and no failure of the gateway's");
        }

        using (var empty = await GetAsync(app, "/graphql?query=", Key))
        {
            empty.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Theory]
    [InlineData("GET operations turned off", false, false)]
    [InlineData("the preflight header enforced", true, true)]
    public async Task Whatever_a_host_makes_of_get_requests_the_schema_file_is_refused_to_whoever_does_not_read_the_schema(string what, bool enableGetRequests, bool enforcePreflight)
    {
        // HotChocolate runs an operation a GET carries, or serves the file regardless, as its server options say.
        await using var app = await GatewayHost.StartAsync(
            Environments.Production,
            builder =>
            {
                builder.Configuration[GraphQLSchemaKey.Setting] = Key;
                builder.Services.AddNamedCallers();
                builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>(q => q.Name("Query"));
                builder.Services.AddInMemoryFusionGateway("user", ["catalog"], options => options.ConfigureGateway = gateway => gateway.ModifyServerOptions(server =>
                {
                    server.EnableGetRequests = enableGetRequests;
                    server.EnforceGetRequestsPreflightHeader = enforcePreflight;
                }));
            },
            app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapInMemoryFusionGateway("/graphql", "user").RequireAuthorization();
            });

        foreach (var file in new[] { "/graphql?sdl&query=" + Uri.EscapeDataString("{ __typename }"), "/graphql?sdl&id=x", "/graphql/schema.graphql?extensions=%7B%7D" })
        {
            using var refused = await GetAsync(app, file, key: null, caller: "bob");
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "with {0}, {1} asks for the file, and bob reads no schema", what, file);
        }

        using var read = await GetAsync(app, "/graphql?sdl", Key);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_one_gateway_that_requires_nothing_refuses_its_schema_file_whatever_operation_a_get_carries_besides()
    {
        await using var app = await GatewayHost.StartAsync(
            Environments.Production,
            builder =>
            {
                builder.Configuration[GraphQLSchemaKey.Setting] = Key;
                builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>(q => q.Name("Query"));
                builder.Services.AddInMemoryFusionGateway(options => options.ConfigureGateway = gateway => gateway.ModifyServerOptions(server => server.EnableGetRequests = false));
            },
            endpoints => endpoints.MapInMemoryFusionGateway());

        foreach (var file in new[] { "/graphql?sdl&query=x", "/graphql?sdl&id=x", "/graphql/schema.graphql?extensions=%7B%7D" })
        {
            using var refused = await GetAsync(app, file, key: null);
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "{0} asks for the file", file);
        }
    }

    [Fact]
    public async Task A_gateway_mapped_in_a_group_serves_its_schema_file_behind_the_groups_prefix_to_a_reader_only()
    {
        await using var app = await GatewayHost.StartAsync(
            Environments.Production,
            builder => Registered(builder, Key, logs: null),
            app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapGroup("/api").RequireAuthorization().MapInMemoryFusionGateway("/graphql", "user");
                app.MapGroup("/open").MapInMemoryFusionGateway("/admin/graphql", "admin");
            });
        var printed = (await app.Schemas.PrintGatewayAsync("user", Cancellation)).ReplaceLineEndings("\n").Trim();

        foreach (var file in new[] { "/api/graphql/schema.graphql", "/api/graphql/schema", "/api/graphql?sdl" })
        {
            using var read = await GetAsync(app, file, Key);
            read.StatusCode.Should().Be(HttpStatusCode.OK, "{0} is the gateway's file behind the group's prefix", file);
            (await read.Content.ReadAsStringAsync(Cancellation)).ReplaceLineEndings("\n").Trim().Should().Be(printed);
        }

        using (var anonymous = await GetAsync(app, "/api/graphql/schema.graphql", key: null))
        {
            anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the group requires a token, and no key stands in for it");
        }

        using (var signedIn = await GetAsync(app, "/api/graphql/schema.graphql", key: null, caller: "bob"))
        {
            signedIn.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var open = await GetAsync(app, "/open/admin/graphql/schema.graphql", key: null))
        {
            open.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a group that requires nothing still keeps the file to a reader");
        }

        using (var openRead = await GetAsync(app, "/open/admin/graphql/schema.graphql", Key))
        {
            openRead.StatusCode.Should().Be(HttpStatusCode.OK);
            (await openRead.Content.ReadAsStringAsync(Cancellation)).Should().Contain("stockValue");
        }

        (await app.DataAsync("/api/graphql", Product, request: request => request.As("bob"))).GetProperty("productById").GetProperty("name").GetString().Should().Be("Coffee");
    }

    [Fact]
    public async Task A_gateway_whose_readers_hold_the_key_takes_it_in_Development_as_well()
    {
        await using var app = await StartAsync(Environments.Development, readers: SchemaReaders.KeyOnly);

        using (var anonymous = await GetAsync(app, "/graphql?sdl", key: null))
        {
            anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Development lets nobody past the endpoint where the host said the key");
        }

        using (var signedIn = await GetAsync(app, "/graphql?sdl", key: null, caller: "bob"))
        {
            signedIn.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var introspection = await app.PostAsync("/graphql", "{ __schema { queryType { name } } }", request: request => request.As("bob"));
        introspection.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain(GraphQLSchemaKey.HeaderName);

        using (var file = await GetAsync(app, "/graphql?sdl", Key))
        {
            file.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await app.DataAsync("/graphql", CodegenIntrospection, request: WithKey(Key))).GetProperty("__schema").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task A_gateway_whose_schema_nobody_reads_refuses_it_with_the_key_and_in_Development()
    {
        foreach (var environment in new[] { Environments.Development, Environments.Production })
        {
            await using var app = await StartAsync(environment, readers: SchemaReaders.Nobody);

            using (var anonymous = await GetAsync(app, "/graphql?sdl", Key))
            {
                anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "in {0} the key reads nothing here", environment);
            }

            using (var signedIn = await GetAsync(app, "/graphql/schema.graphql", Key, caller: "bob"))
            {
                signedIn.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            }

            var introspection = await app.PostAsync("/graphql", "{ __schema { queryType { name } } }", request: request => { request.As("bob"); WithKey(Key)(request); });
            (introspection.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object).Should().BeFalse();
            introspection.GetProperty("errors")[0].GetProperty("message").GetString().Should().StartWith("Nobody reads this endpoint's schema", "the key reads nothing at a gateway whose schema nobody reads");
        }

        await using var tested = await StartAsync(Environments.Production, readers: SchemaReaders.Nobody);
        (await tested.Schemas.PrintGatewayAsync("user", Cancellation)).Should().Contain("type Query", "a test still prints the schema, for the committed file");
    }

    [Fact]
    public async Task A_public_schema_is_read_by_every_request_and_its_data_still_needs_the_token()
    {
        await using var app = await StartAsync(Environments.Production, readers: SchemaReaders.Everyone);

        using (var file = await GetAsync(app, "/graphql?sdl", key: null))
        {
            file.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await app.DataAsync("/graphql", CodegenIntrospection)).GetProperty("__schema").GetProperty("queryType").GetProperty("name").GetString().Should().Be("Query");

        using var operation = await app.SendAsync("/graphql", Product);
        operation.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a public schema opens the schema, not the data");
    }

    [Fact]
    public async Task Who_reads_the_schema_is_the_readers_to_say_whatever_DisableIntrospection_in_ConfigureGateway_says()
    {
        // Introspection turned on everywhere: still only a reader introspects, as only a reader reads the file.
        await using (var opened = await StartAsync(Environments.Production, configureGateway: gateway => gateway.DisableIntrospection(false)))
        {
            var refused = await opened.PostAsync("/graphql", "{ __schema { queryType { name } } }", request: request => request.As("bob"));
            refused.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain(GraphQLSchemaKey.HeaderName);
        }

        // Introspection turned off everywhere: a reader still introspects, in Development as the readers say.
        await using var closed = await StartAsync(Environments.Development, configureGateway: gateway => gateway.DisableIntrospection(true));
        (await closed.DataAsync("/graphql", "{ __schema { queryType { name } } }", request: request => request.As("bob")))
            .GetProperty("__schema").GetProperty("queryType").GetProperty("name").GetString().Should().Be("Query");
    }

    [Fact]
    public async Task A_get_that_carries_an_operation_beside_the_file_is_no_schema_request()
    {
        await using var app = await StartAsync(Environments.Production);

        using var response = await GetAsync(app, "/graphql?sdl&query=" + Uri.EscapeDataString(Product), Key);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "an operation beside the file is no schema request: HotChocolate may run it, so it needs what the endpoint requires");
    }

    [Fact]
    public async Task In_Development_a_tool_reads_the_schema_without_the_key_and_an_operation_still_needs_a_token()
    {
        await using var app = await StartAsync(Environments.Development);

        using (var file = await GetAsync(app, "/graphql?sdl", key: null))
        {
            file.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await app.DataAsync("/graphql", CodegenIntrospection)).GetProperty("__schema").GetProperty("queryType").GetProperty("name").GetString().Should().Be("Query");
        (await app.DataAsync("/admin/graphql", "{ __schema { queryType { name } } }")).GetProperty("__schema").ValueKind.Should().Be(JsonValueKind.Object);

        using var operation = await app.SendAsync("/graphql", Product);
        operation.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Development opens the schema, not the data");
    }

    [Fact]
    public async Task Without_a_key_in_configuration_no_request_reads_the_schema_outside_Development_and_one_that_sends_a_key_is_logged()
    {
        var logs = new LogRecorder();
        await using var app = await StartAsync(Environments.Production, logs, key: null);

        using var file = await GetAsync(app, "/graphql?sdl", Key);

        file.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning && entry.Category == typeof(GraphQLSchemaKey).FullName)
            .Which.Message.Should().Contain("the application has no key at GraphQL:SchemaKey");
        logs.Everything.Should().NotContain(Key);
    }

    [Fact]
    public async Task A_key_shorter_than_thirty_two_characters_fails_the_mapping_and_its_message_holds_nothing_of_it()
    {
        const string Short = "letmein-letmein";

        var starting = async () => await GatewayHost.StartAsync(Environments.Production, builder => Registered(builder, Short, logs: null), Mapped);

        var failure = await starting.Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Message.Should().Contain("GraphQL:SchemaKey is 15 characters long; a schema key has at least 32").And.NotContain(Short);
    }

    [Fact]
    public async Task A_gateway_that_requires_nothing_serves_its_schema_file_with_the_key_only_and_everything_else_as_before()
    {
        // The one gateway of every schema, mapped without a requirement, as an application without users maps it.
        await using var app = await GatewayHost.StartAsync(
            Environments.Production,
            builder =>
            {
                builder.Configuration[GraphQLSchemaKey.Setting] = Key;
                builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>(q => q.Name("Query"));
                builder.Services.AddInMemoryFusionGateway();
            },
            endpoints => endpoints.MapInMemoryFusionGateway());

        using (var refused = await GetAsync(app, "/graphql?sdl", key: null))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "outside Development the schema file takes the key, whoever asks");
        }

        using (var read = await GetAsync(app, "/graphql?sdl", Key))
        {
            read.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await app.DataAsync(Product)).GetProperty("productById").GetProperty("name").GetString().Should().Be("Coffee");
    }

    private static Task<GatewayHost> StartAsync(
        string environment,
        LogRecorder? logs = null,
        string? key = Key,
        SchemaReaders readers = SchemaReaders.DevelopmentOrKey,
        Action<IFusionGatewayBuilder>? configureGateway = null)
        => GatewayHost.StartAsync(environment, builder => Registered(builder, key, logs, readers, configureGateway), Mapped);

    private static void Registered(
        WebApplicationBuilder builder,
        string? key,
        LogRecorder? logs,
        SchemaReaders readers = SchemaReaders.DevelopmentOrKey,
        Action<IFusionGatewayBuilder>? configureGateway = null)
    {
        if (logs is not null)
        {
            builder.Logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace);
        }

        builder.Configuration[GraphQLSchemaKey.Setting] = key;
        builder.Services.AddNamedCallers();
        builder.Services.AddGraphQLServer("catalog").AddSourceSchemaDefaults().AddQueryType<CatalogQuery>(q => q.Name("Query"));
        builder.Services.AddGraphQLServer("inventory").AddSourceSchemaDefaults().AddQueryType<InventoryQuery>(q => q.Name("Query"));
        builder.Services.AddGraphQLServer("admin").AddSourceSchemaDefaults().AddQueryType<AdminQuery>(q => q.Name("Query"));
        builder.Services.AddInMemoryFusionGateway("user", ["catalog", "inventory"], options =>
        {
            options.SchemaReaders = readers;
            options.ConfigureGateway = configureGateway;
        });
        builder.Services.AddInMemoryFusionGateway("admin", ["admin"]);
    }

    private static void Mapped(WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapInMemoryFusionGateway("/graphql", "user").RequireAuthorization();
        app.MapInMemoryFusionGateway("/admin/graphql", "admin").RequireAuthorization(NamedCallers.Administrators);
    }

    private static Action<HttpRequestMessage> WithKey(string key) => request => request.Headers.Add(GraphQLSchemaKey.HeaderName, key);

    private static async Task<HttpResponseMessage> GetAsync(GatewayHost app, string path, string? key, string? secondKey = null, string? caller = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (key is not null)
        {
            request.Headers.Add(GraphQLSchemaKey.HeaderName, secondKey is null ? new[] { key } : new[] { key, secondKey });
        }

        if (caller is not null)
        {
            request.As(caller);
        }

        return await app.Http.SendAsync(request, Cancellation);
    }
}
