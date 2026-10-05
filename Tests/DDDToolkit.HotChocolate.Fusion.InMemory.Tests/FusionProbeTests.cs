using System.Globalization;
using System.Text.Json;
using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;
using DDDToolkit.HotChocolate.Tests.Infrastructure;
using DDDToolkit.HotChocolate.Tests.InternalTypes;
using DDDToolkit.Invariants;
using DDDToolkit.Localization;
using DDDToolkit.Validation;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using Xunit;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests;

/// <summary>
/// What the gateway does with the source schemas of modules that have the toolkit's conventions, asked over real
/// HTTP. Each test pins one fact a modular application's GraphQL rests on, so that a HotChocolate release that
/// changes one fails here and not in an application.
/// </summary>
public sealed class FusionProbeTests
{
    private const string Errors =
        """
        errors {
          __typename
          ... on CodedError { code message arguments { name value } }
          ... on RefusalError { kind field }
        }
        """;

    /// <summary>What every source schema with the conventions declares, and the gateway declares once.</summary>
    private static readonly string[] SharedDeclarations =
    [
        "interface CodedError ", "type RefusalError ", "type InvalidValuesError ", "type ValueFailure ",
        "type BrokenRulesError ", "type RuleViolation ", "type ConcurrencyConflictError ", "type FailureArgument ",
        "enum RefusalKind ",
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_source_schemas_with_the_conventions_and_one_spelling_compose()
    {
        // Both modules declare the same error types, the same interface and the same RefusalKind. They compose
        // because the error types say they are shared, and the gateway has each of them once.
        await using var shop = await GatewayHost.StartAsync(builder => builder.AddShop());

        var sdl = await shop.Schemas.PrintGatewayAsync(Cancellation);

        // Declarations are counted without the descriptions, which are text for a client and could quote one.
        var declarations = SchemaDescriptions.RemovedFrom(sdl);
        foreach (var declaration in SharedDeclarations)
        {
            Occurrences(declarations, declaration).Should().Be(1, "the gateway has one '{0}'", declaration.Trim());
        }

        Values(sdl, "enum RefusalKind").Should().Equal("invalid", "not_permitted", "not_found", "conflict");
        sdl.Should().Contain("type RefusalError implements CodedError");

        // Each module's mutation has its own payload and its own union of the shared errors.
        sdl.Should().Contain("union ProductRenameError").And.Contain("union ProductRestockError");
    }

    [Fact]
    public async Task Source_schemas_that_read_the_xml_documentation_differently_compose_and_describe_the_shared_types_alike()
    {
        // HotChocolate describes a type from the XML documentation beside its assembly, when the schema reads it.
        // Here one module's schema reads it and the other's does not, as two modules of one gateway would differ
        // when only one host was built with the file beside the toolkit's assembly. The toolkit describes its types
        // itself, so both say the same of every type they share, and the gateway has one of each with that
        // description: the composer has no two descriptions of one type to choose between.
        SchemaDescriptions.ToolkitXmlDocumentationIsBeside().Should().BeTrue("the module that reads XML documentation has the toolkit's to read");
        await using var shop = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddShopServices();
            builder.Services.AddCatalog();
            builder.Services.AddInventory().ModifyOptions(options => options.UseXmlDocumentation = false);
        });

        var executors = shop.Services.GetRequiredService<IRequestExecutorProvider>();
        var catalog = (await executors.GetExecutorAsync(ShopModules.Catalog, Cancellation)).Schema;
        var inventory = (await executors.GetExecutorAsync(ShopModules.Inventory, Cancellation)).Schema;

        foreach (var type in SharedDeclarations.Select(declaration => declaration.Split(' ')[1]))
        {
            SchemaDescriptions.Of(catalog, type).Should().Equal(SchemaDescriptions.Of(inventory, type), "both modules describe {0} as the toolkit does", type);
        }

        var sdl = await shop.Schemas.PrintGatewayAsync(Cancellation);
        var declarations = SchemaDescriptions.RemovedFrom(sdl);
        foreach (var declaration in SharedDeclarations)
        {
            Occurrences(declarations, declaration).Should().Be(1, "the gateway has one '{0}'", declaration.Trim());
        }

        var codedError = catalog.Types.GetType<ITypeDefinition>("CodedError").Description;
        codedError.Should().NotBeNullOrWhiteSpace();
        Occurrences(sdl, codedError!).Should().Be(1, "the gateway describes the interface once, as both modules do");
    }

    [Fact]
    public async Task Source_schemas_that_spell_their_enums_differently_do_not_compose()
    {
        // Why every source schema needs the same spelling: an enum two schemas declare is one type to the
        // gateway, and the composer wants each schema to define every value of it. The application does not
        // start, and says which values are missing where.
        var starting = GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddShopServices();
            builder.Services.AddCatalog(EnumValueSpelling.LowerSnakeCase);
            builder.Services.AddInventory(EnumValueSpelling.UpperSnakeCase);
        });

        var refused = (await starting.Invoking(async task => await task).Should().ThrowAsync<InvalidOperationException>()).Which;
        Record("Two spellings: " + refused.Message);

        refused.Message.Should().Contain("could not be composed");
        refused.Message.Should().Contain("The enum type 'RefusalKind' in schema 'catalog' must define the value 'NOT_PERMITTED'.");
        refused.Message.Should().Contain("The enum type 'RefusalKind' in schema 'inventory' must define the value 'not_permitted'.");
    }

    [Fact]
    public async Task A_refused_mutation_answers_a_typed_error_through_the_gateway()
    {
        await using var shop = await GatewayHost.StartAsync(builder => builder.AddShop());

        var data = await shop.DataAsync($$"""mutation { productRename(input: { id: 1, name: "" }) { product { id name } {{Errors}} } }""");

        var payload = data.GetProperty("productRename");
        payload.GetProperty("product").ValueKind.Should().Be(JsonValueKind.Null);
        var error = payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("__typename").GetString().Should().Be("RefusalError");
        error.GetProperty("code").GetString().Should().Be(CatalogMutations.NameRequired);
        error.GetProperty("kind").GetString().Should().Be("invalid", "the gateway spells the kind as the source schemas do");
        error.GetProperty("message").GetString().Should().Be("A product needs a name.");
        error.GetProperty("field").GetString().Should().Be("name");
        error.GetProperty("arguments").EnumerateArray().Select(argument => (argument.GetProperty("name").GetString(), argument.GetProperty("value").GetString()))
            .Should().Equal(("Field", "name"), ("MaxLength", "40"));

        // The other module's mutation, through the same gateway.
        var restock = await shop.DataAsync($$"""mutation { productRestock(input: { id: 1, quantity: 0 }) { product { id } {{Errors}} } }""");
        restock.GetProperty("productRestock").GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("inventory.quantity-invalid");

        // And one that goes through answers its result and no errors.
        var renamed = await shop.DataAsync($$"""mutation { productRename(input: { id: 1, name: "Espresso" }) { product { id name } {{Errors}} } }""");
        renamed.GetProperty("productRename").GetProperty("product").GetProperty("name").GetString().Should().Be("Espresso");
        renamed.GetProperty("productRename").GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_refused_query_reaches_the_client_with_its_code_kind_and_arguments()
    {
        await using var shop = await GatewayHost.StartAsync(builder => builder.AddShop());

        var body = await shop.PostAsync("{ priceList }");
        Record("A refused query through the gateway: " + body);

        var error = body.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("message").GetString().Should().Be("The price list is not open to this caller.");
        error.GetProperty("path")[0].GetString().Should().Be("priceList");

        var extensions = error.GetProperty("extensions");
        extensions.GetProperty("code").GetString().Should().Be("catalog.price-list-closed");
        extensions.GetProperty("kind").GetString().Should().Be("not_permitted");
        extensions.GetProperty("arguments").GetProperty("List").GetString().Should().Be("wholesale");
        extensions.GetProperty("arguments").GetProperty("Tier").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task What_is_ambient_at_the_request_is_ambient_in_a_resolver_a_lookup_and_a_data_loader()
    {
        // The gateway carries nothing over to a source schema explicitly. What the host's middleware made
        // ambient before the gateway is ambient in a module's code because the call is awaited in the request's
        // own flow: the caller, and the reader's language.
        await using var shop = await GatewayHost.StartAsync(builder => builder.AddShop(), app => app.UseAmbientCaller());

        var data = await shop.DataAsync(
            """
            {
              resolverSaw { caller culture }
              productById(id: 1) {
                name
                lookupSaw { caller culture }
                loaderSaw { caller culture }
              }
            }
            """,
            request: message =>
            {
                message.Headers.Add(ShopModules.CallerHeader, "tove");
                message.Headers.Add("Accept-Language", "nl-NL");
            });

        Saw(data.GetProperty("resolverSaw")).Should().Be(("tove", "nl-NL"), "a resolver of the module that owns the field");
        Saw(data.GetProperty("productById").GetProperty("lookupSaw")).Should().Be(("tove", "nl-NL"), "a lookup the gateway calls in the other module");
        Saw(data.GetProperty("productById").GetProperty("loaderSaw")).Should().Be(("tove", "nl-NL"), "a data loader behind that lookup");

        // Another request, another caller: nothing was kept.
        var other = await shop.DataAsync("{ resolverSaw { caller culture } productById(id: 2) { loaderSaw { caller culture } } }", request: message =>
        {
            message.Headers.Add(ShopModules.CallerHeader, "rhea");
            message.Headers.Add("Accept-Language", "en-GB");
        });

        Saw(other.GetProperty("resolverSaw")).Should().Be(("rhea", "en-GB"));
        Saw(other.GetProperty("productById").GetProperty("loaderSaw")).Should().Be(("rhea", "en-GB"));

        static (string? Caller, string? Culture) Saw(JsonElement witness)
            => (witness.GetProperty("caller").GetString(), witness.GetProperty("culture").GetString());
    }

    [Fact]
    public async Task A_failure_is_phrased_in_the_readers_language_through_the_gateway()
    {
        // The localizer is the application's, and a module's resolver finds it because a source schema runs on
        // the application's services; the language is the request's, because it is ambient.
        await using var shop = await GatewayHost.StartAsync(
            builder =>
            {
                builder.AddShop();
                builder.Services.AddSingleton<IFailureLocalizer, ByLanguage>();
            },
            app => app.UseAmbientCaller());

        var dutch = await shop.DataAsync(
            $$"""mutation { productRename(input: { id: 1, name: "" }) { {{Errors}} } }""",
            request: message => message.Headers.Add("Accept-Language", "nl-NL"));
        dutch.GetProperty("productRename").GetProperty("errors")[0].GetProperty("message").GetString().Should().Be("nl " + CatalogMutations.NameRequired);

        var english = await shop.DataAsync(
            $$"""mutation { productRename(input: { id: 1, name: "" }) { {{Errors}} } }""",
            request: message => message.Headers.Add("Accept-Language", "en-GB"));
        english.GetProperty("productRename").GetProperty("errors")[0].GetProperty("message").GetString().Should().Be("en " + CatalogMutations.NameRequired);

        // A refused query, whose error the filter phrases, in the same language.
        var query = await shop.PostAsync("{ priceList }", request: message => message.Headers.Add("Accept-Language", "nl-NL"));
        query.GetProperty("errors")[0].GetProperty("message").GetString().Should().Be("nl catalog.price-list-closed");
    }

    [Fact]
    public async Task A_source_schema_gets_a_scope_of_the_application_not_the_requests()
    {
        await using var shop = await GatewayHost.StartAsync(builder => builder.AddShop(), app => app.UseAmbientCaller());

        var first = await ScopesAsync(shop);
        var second = await ScopesAsync(shop);

        // The resolver's scoped service is not the one the HTTP request's own scope holds: the gateway calls a
        // source schema without the request's services, and the source schema makes a scope of the application's
        // container for the call. Nothing scoped is shared between the pipeline and a resolver.
        first.Resolver.Should().NotBe(first.Request);
        second.Resolver.Should().NotBe(second.Request);

        // It is a scope, and a new one for every request.
        second.Resolver.Should().NotBe(first.Resolver);
        second.Request.Should().NotBe(first.Request);

        Record("The scope of a resolver in a source schema is a scope of the application's container, made for the call, and not the HTTP request's.");

        static async Task<(Guid Request, Guid Resolver)> ScopesAsync(GatewayHost shop)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/graphql")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { query = "{ resolverSaw { scope } }" }),
            };

            using var response = await shop.Http.SendAsync(message, Cancellation);
            var body = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<JsonElement>(response.Content, Cancellation);

            return (
                Guid.Parse(response.Headers.GetValues(ShopModules.RequestScopeHeader).Single()),
                body.GetProperty("data").GetProperty("resolverSaw").GetProperty("scope").GetGuid());
        }
    }

    [Fact]
    public async Task Fields_of_one_query_run_side_by_side_each_in_a_scope_of_its_own()
    {
        await using var shop = await GatewayHost.StartAsync(builder => builder.AddShop(meetingOf: 3));

        // Each field waits until all three are in flight. Fields that ran one after the other would never meet.
        var data = await shop.DataAsync("{ a: meeting { together scope } b: meeting { together scope } c: meeting { together scope } }");

        var fields = new[] { data.GetProperty("a"), data.GetProperty("b"), data.GetProperty("c") };
        fields.Select(field => field.GetProperty("together").GetBoolean()).Should().AllBeEquivalentTo(true, "the three fields were in flight at the same time");
        fields.Select(field => field.GetProperty("scope").GetGuid()).Should().OnlyHaveUniqueItems("with a scope per resolver, no two fields share a scoped service");

        // One call to the source schema carried all three.
        shop.Services.GetRequiredService<SourceCalls>().Of(ShopModules.Catalog).Should().Be(1);
    }

    [Fact]
    public async Task Two_mutations_of_one_document_run_one_after_the_other_and_their_scopes_are_recorded()
    {
        await using var shop = await GatewayHost.StartAsync(builder => builder.AddShop());

        var data = await shop.DataAsync(
            """
            mutation {
              first: stepRecord(input: { name: "first" }) { step { name scope started ended } }
              second: stepRecord(input: { name: "second" }) { step { name scope started ended } }
            }
            """);

        var first = data.GetProperty("first").GetProperty("step");
        var second = data.GetProperty("second").GetProperty("step");

        // In the order of the document, the second starting after the first has ended.
        first.GetProperty("started").GetInt32().Should().Be(1);
        first.GetProperty("ended").GetInt32().Should().Be(2);
        second.GetProperty("started").GetInt32().Should().Be(3);
        second.GetProperty("ended").GetInt32().Should().Be(4);

        // The gateway sends each mutation field of a document to the source schema as a call of its own. With the
        // request's scope for mutations, that is a scope per call: each command gets its own scoped services,
        // and its own unit of work, although the client sent one document.
        var calls = shop.Services.GetRequiredService<SourceCalls>().Of(ShopModules.Catalog);
        var oneScope = first.GetProperty("scope").GetGuid() == second.GetProperty("scope").GetGuid();
        Record($"Two mutation fields of one document: {calls} call(s) to the source schema, {(oneScope ? "one scope for both" : "a scope each")}.");

        calls.Should().Be(2, "each mutation field is a call of its own");
        oneScope.Should().BeFalse("and each call has a scope of its own");
    }

    [Fact]
    public async Task A_source_schema_of_internal_types_composes()
    {
        // Two modules whose GraphQL types are all internal: the depot's, which DDDToolkit.HotChocolate.Tests
        // builds by itself, and the yard's, which names a depot by its key.
        await using var yard = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddGraphQLServer(YardSchema.Depots).AddDepotSchema();
            builder.Services.AddGraphQLServer(YardSchema.Yard).AddYardSchema();
        });

        typeof(DepotOutput).IsNotPublic.Should().BeTrue();
        typeof(PalletOutput).IsNotPublic.Should().BeTrue();
        typeof(ReferencedDepot).IsNotPublic.Should().BeTrue();

        // One Depot in the composed schema, with the key both declare and the name its owner has.
        var sdl = await yard.Schemas.PrintGatewayAsync(Cancellation);
        Occurrences(sdl, "type Depot ").Should().Be(1);

        // The yard gives the key, and the depot's lookup the name.
        var data = await yard.DataAsync("{ pallets { id depot { id name } } }");
        var pallets = data.GetProperty("pallets").EnumerateArray().ToArray();
        pallets[0].GetProperty("depot").GetProperty("name").GetString().Should().Be("North");
        pallets[1].GetProperty("depot").GetProperty("name").GetString().Should().Be("South");

        // The depot's own lookup and its mutation, through the gateway.
        var depot = await yard.DataAsync("{ depot(id: 2) { id name } }");
        depot.GetProperty("depot").GetProperty("name").GetString().Should().Be("South");

        var refused = await yard.DataAsync($$"""mutation { depotRename(input: { id: 1, name: "" }) { depot { id } {{Errors}} } }""");
        var error = refused.GetProperty("depotRename").GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("code").GetString().Should().Be(DepotSchema.NameRequired);
        error.GetProperty("field").GetString().Should().Be("name");
    }

    [Fact]
    public async Task A_reference_whose_owner_answers_nothing_is_null_and_the_rest_of_the_answer_stands()
    {
        await using var yard = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddGraphQLServer(YardSchema.Depots).AddDepotSchema();
            builder.Services.AddGraphQLServer(YardSchema.Yard).AddYardSchema();
        });

        // The stray pallet names depot 9, which the depot module's lookup answers nothing for.
        var body = await yard.PostAsync("{ strayPallet { id depot { id name } } pallets { id depot { name } } }");
        Record("A reference whose owner answers nothing: " + body);

        // The reference is nothing, and the rest of the answer stands: the pallet itself, and the other pallets
        // with their depots.
        var data = body.GetProperty("data");
        data.GetProperty("strayPallet").GetProperty("id").GetInt32().Should().Be(3);
        data.GetProperty("strayPallet").GetProperty("depot").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("pallets").EnumerateArray().Select(pallet => pallet.GetProperty("depot").GetProperty("name").GetString())
            .Should().Equal("North", "South");

        // It is not silent, though. The client asked for the depot's name, which is never null, and the gateway
        // has no depot to read it from: it says so in errors, at the field, and nulls the reference. A reference
        // field is therefore nullable, or the null would travel up and take the pallet with it.
        var error = body.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("HC0018");
        error.GetProperty("path").EnumerateArray().Select(segment => segment.ToString()).Should().Equal("strayPallet", "depot", "name");

        // Asked for the key alone, the reference is answered by the module that named it: the owner is not
        // asked, so the same reference is an object with its id, and no error.
        var keys = await yard.DataAsync("{ strayPallet { depot { id } } }");
        keys.GetProperty("strayPallet").GetProperty("depot").GetProperty("id").GetInt32().Should().Be(9);
    }

    [Fact]
    public async Task An_internal_lookup_resolves_a_reference_and_is_no_field_of_the_composed_schema()
    {
        // Couriers' only lookup is there for the gateway: [Lookup] and [Internal]. Routes names a courier by its
        // key, and a client reaches a courier through that reference and through nothing else.
        await using var depot = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddCouriers();
            builder.Services.AddRoutes();
        });

        // The reference resolves: Routes gives the key, and the internal lookup the rest.
        var data = await depot.DataAsync("{ routes { id courier { id name phone } } }");
        var couriers = data.GetProperty("routes").EnumerateArray().Select(route => route.GetProperty("courier")).ToArray();
        couriers.Select(courier => courier.GetProperty("name").GetString()).Should().Equal("Jorik", "Marek");
        couriers[0].GetProperty("phone").GetString().Should().Be("555-0101");
        couriers[1].GetProperty("phone").ValueKind.Should().Be(JsonValueKind.Null);

        // The composed schema has the type and what Couriers offers clients, and no trace of the lookup.
        var gateway = await depot.Schemas.PrintGatewayAsync(Cancellation);
        Occurrences(gateway, "type Courier ").Should().Be(1);
        Block(gateway, "type Courier").Should().Contain("name: String!").And.Contain("phone: String");
        Block(gateway, "type Query").Should().Contain("courierCount: Int!").And.Contain("routes:").And.NotContain("courierById");
        gateway.Should().NotContain("courierById").And.NotContain("@internal");

        // Couriers' own schema says what the lookup is: one the gateway composes by, and keeps to itself.
        var source = await depot.Schemas.PrintSourceAsync(CourierModules.Couriers, Cancellation);
        source.Should().Contain("courierById(id: Int!): Courier @lookup @internal");
    }

    [Fact]
    public async Task A_client_asking_the_gateway_for_an_internal_lookup_is_refused_at_validation()
    {
        await using var depot = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddCouriers();
            builder.Services.AddRoutes();
        });

        var body = await depot.PostAsync("{ courierById(id: 1) { id name } }");
        Record("A client asking the gateway for an internal lookup: " + body);

        // The gateway has no such field, so the document does not validate: an error, and no data.
        var error = body.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("message").GetString().Should().Contain("courierById").And.Contain("Query");
        (body.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null)
            .Should().BeFalse("a document that does not validate is not executed, got {0}", body);

        // And nothing was asked of the module that has the lookup.
        depot.Services.GetRequiredService<SourceCalls>().Of(CourierModules.Couriers).Should().Be(0);

        // The field a client is offered is still answered.
        (await depot.DataAsync("{ courierCount }")).GetProperty("courierCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task A_reference_through_an_internal_lookup_that_answers_nothing_is_null()
    {
        await using var depot = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddCouriers();
            builder.Services.AddRoutes();
        });

        // The stray route names courier 9. The internal lookup answers nothing for it, as it does for a courier
        // that is not there and for one the caller may not read: the gateway cannot tell the two apart.
        var body = await depot.PostAsync("{ strayRoute { id courier { id name phone } } routes { id courier { name } } }");
        Record("A reference through an internal lookup that answers nothing, asked for a field that is never null: " + body);

        // The reference is nothing, and the rest of the answer stands: the route itself, and the other routes
        // with their couriers.
        var data = body.GetProperty("data");
        data.GetProperty("strayRoute").GetProperty("id").GetInt32().Should().Be(3);
        data.GetProperty("strayRoute").GetProperty("courier").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("routes").EnumerateArray().Select(route => route.GetProperty("courier").GetProperty("name").GetString())
            .Should().Equal("Jorik", "Marek");

        // It is null because a field that is never null had nothing to be read from, and the gateway says so:
        // one error, at that field, with nothing of the lookup in it.
        var error = body.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("HC0018");
        error.GetProperty("path").EnumerateArray().Select(segment => segment.ToString()).Should().Equal("strayRoute", "courier", "name");
        error.ToString().Should().NotContain("courierById");

        // Asked only for what may be null, the same reference is an object: the key Routes gave, the owner's
        // fields null, and no error. A client tells "nothing to read" from the nulls, not from the reference.
        var nullable = await depot.PostAsync("{ strayRoute { id courier { id phone } } }");
        Record("The same reference, asked only for fields that may be null: " + nullable);

        nullable.TryGetProperty("errors", out _).Should().BeFalse("got {0}", nullable);
        var courier = nullable.GetProperty("data").GetProperty("strayRoute").GetProperty("courier");
        courier.GetProperty("id").GetInt32().Should().Be(9);
        courier.GetProperty("phone").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_entity_one_module_has_no_part_of_keeps_the_other_modules_part()
    {
        await using var shop = await GatewayHost.StartAsync(builder => builder.AddShop());

        // Catalog has product 4 and Inventory has never heard of it. What only Catalog knows is answered, and a
        // nullable field of Inventory's part is null, without an error.
        var named = await shop.PostAsync("{ productById(id: 4) { id name lookupSaw { caller } } }");
        Record("A product Inventory has no part of, asked for a nullable field of Inventory's: " + named);

        named.TryGetProperty("errors", out _).Should().BeFalse();
        named.GetProperty("data").GetProperty("productById").GetProperty("name").GetString().Should().Be("Chai");
        named.GetProperty("data").GetProperty("productById").GetProperty("lookupSaw").ValueKind.Should().Be(JsonValueKind.Null);

        // A field of Inventory's part that is never null cannot be answered: an error at the field, and the null
        // travels up to the first place that may be null, here the product.
        var stocked = await shop.PostAsync("{ productById(id: 4) { id name onHand } }");
        Record("The same product, asked for a field of Inventory's that is never null: " + stocked);

        stocked.GetProperty("data").GetProperty("productById").ValueKind.Should().Be(JsonValueKind.Null);
        var error = stocked.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("path").EnumerateArray().Select(segment => segment.ToString()).Should().Equal("productById", "onHand");
    }

    [Fact]
    public async Task Lookups_for_many_keys_reach_a_data_loader_as_one_batch()
    {
        // The loader's batch leaves when it holds three keys, and not when HotChocolate's own dispatcher finds it
        // quiet, which on a busy machine is before the third lookup got to it. What is pinned is that the three
        // lookups share one loader and so can be one batch; lookups that each had a loader of their own would
        // never fill one, and would leave as three.
        var whole = new WholeBatches();
        whole.Of(3);
        await using var shop = await GatewayHost.StartAsync(builder =>
        {
            builder.AddShop();
            whole.AddTo(builder.Services);
        });

        // Catalog names three products, and Inventory is asked for the stock of each.
        var data = await shop.DataAsync("{ products { id name onHand } }");
        data.GetProperty("products").EnumerateArray().Select(product => (product.GetProperty("name").GetString(), product.GetProperty("onHand").GetInt32()))
            .Should().Equal(("Coffee", 12), ("Tea", 7), ("Cocoa", 0));

        // The gateway's three lookups arrive in one call to the source schema, and its data loader sees the keys
        // of all three in one batch.
        var batches = shop.Services.GetRequiredService<BatchLog>().Batches.ToArray();
        Record("Batches the data loader was asked for: " + string.Join(" | ", batches.Select(batch => string.Join(",", batch))));

        whole.Sent.Should().Equal([3], "the three lookups waited on one batch, sent whole");
        batches.Should().ContainSingle().Which.Should().BeEquivalentTo([1, 2, 3]);
        shop.Services.GetRequiredService<SourceCalls>().Of(ShopModules.Inventory).Should().Be(1);
    }

    [Fact]
    public async Task Two_lookups_of_one_module_each_for_several_keys_are_both_answered()
    {
        await using var site = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddPeople();
            builder.Services.AddShifts();
        });

        // Three shifts, each naming a worker and a trade. The gateway asks People for the workers and for the
        // trades at the same step, as one batch of two requests, each with three sets of variables. Fusion's own
        // in-memory client reads the second request's variables from where the first one's begin, and fails the
        // batch; the gateway keeps such requests apart.
        var data = await site.DataAsync("{ shifts { id worker { name } trade { title } } }");

        data.GetProperty("shifts").EnumerateArray()
            .Select(shift => (shift.GetProperty("worker").GetProperty("name").GetString(), shift.GetProperty("trade").GetProperty("title").GetString()))
            .Should().Equal(("Jorik", "Diver"), ("Marek", "Welder"), ("Odile", "Rigger"));
    }

    [Fact]
    public async Task A_node_field_of_one_source_schema_answers_through_the_gateway_beside_plain_lookups()
    {
        // Projects has the one node type, and Inspections names a project by its id. Inspections has no node of
        // its own, but it has to write the id as Projects reads it: a schema that names a node turns on node
        // ids, without the Node interface and without a node field.
        await using var site = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddProjects();
            builder.Services.AddInspections(nodeIds: true);
        });

        var sdl = await site.Schemas.PrintGatewayAsync(Cancellation);
        sdl.Should().Contain("node(id: ID!): Node").And.Contain("type Project implements Node");
        (await site.Schemas.PrintSourceAsync(NodeModules.Inspections, Cancellation)).Should().NotContain("interface Node").And.NotContain("node(id");

        // A reference from the schema without nodes resolves through the owner, and carries the node id.
        var inspections = await site.DataAsync("{ inspections { id project { id name } } }");
        var first = inspections.GetProperty("inspections")[0].GetProperty("project");
        first.GetProperty("name").GetString().Should().Be("Bridge");
        var id = first.GetProperty("id").GetString()!;
        id.Should().NotBe("1", "a project's id is a node id wherever a project is named");

        // The gateway serves node(id), and the plain lookup beside it.
        var node = await site.DataAsync("query ($id: ID!) { node(id: $id) { id ... on Project { name } } project(id: $id) { name } }", new { id });
        node.GetProperty("node").GetProperty("name").GetString().Should().Be("Bridge");
        node.GetProperty("node").GetProperty("id").GetString().Should().Be(id);
        node.GetProperty("project").GetProperty("name").GetString().Should().Be("Bridge");

        // A node id of a project that is not there is nothing, from both.
        var missing = await site.DataAsync("query ($id: ID!) { node(id: $id) { id } project(id: $id) { name } }", new { id = NodeIdOf(id, 7) });
        missing.GetProperty("node").ValueKind.Should().Be(JsonValueKind.Null);
        missing.GetProperty("project").ValueKind.Should().Be(JsonValueKind.Null);

        Record("node(id) answers through the gateway with global object identification on the schema that owns the node type; "
            + "a schema that only names the node needs node ids (AddGlobalObjectIdentification(registerNodeInterface: false)).");
    }

    [Fact]
    public async Task A_schema_that_names_a_node_without_node_ids_hands_the_owner_an_id_it_cannot_read()
    {
        // The same two modules, and Inspections left as it is: [ID] then writes the bare key, the gateway hands
        // it to Projects' lookup, and Projects refuses it. The schemas compose; the reference does not resolve.
        await using var site = await GatewayHost.StartAsync(builder =>
        {
            builder.Services.AddProjects();
            builder.Services.AddInspections(nodeIds: false);
        });

        var body = await site.PostAsync("{ inspections { id project { id name } } }");
        Record("A reference to a node from a schema without node ids: " + body);

        body.GetProperty("data").GetProperty("inspections")[0].GetProperty("project").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain("node ID");
    }

    [Fact]
    public async Task A_lookup_must_answer_one_nullable_object_to_give_its_type_a_key()
    {
        // A key is inferred from a lookup that answers one nullable object, and from nothing else: a type that
        // other modules add to declares its key itself, with [EntityKey], and does not leave it to inference.
        // (A lookup that answers a list does not get this far: HotChocolate's analyzer refuses to compile it.)
        (await SourceAsync<NullableLookup>()).Should().Contain("type Thing @key(fields: \"id\")");
        (await SourceAsync<NonNullLookup>()).Should().NotContain("@key");

        static async Task<string> SourceAsync<TQuery>()
            where TQuery : class
        {
            await using var app = GatewayHost.Build(builder => builder.Services
                .AddGraphQLServer("things")
                .AddSourceSchemaDefaults()
                .AddQueryType<TQuery>(query => query.Name(OperationTypeNames.Query)));

            return await app.Services.GetRequiredService<InMemoryFusionSchemas>().PrintSourceAsync("things", Cancellation);
        }
    }

    private static void Record(string finding) => TestContext.Current.TestOutputHelper?.WriteLine(finding);

    /// <summary>The node id of another project: the id the gateway handed out, with its key replaced.</summary>
    private static string NodeIdOf(string id, int key)
    {
        var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(id));
        var separator = text.LastIndexOf(':');
        separator.Should().BeGreaterThan(0, "a node id is the type's name and the key, got '{0}'", text);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text[..(separator + 1)] + key));
    }

    private static int Occurrences(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;

    /// <summary>The body of one type declaration, so an assertion cannot match a different type.</summary>
    private static string Block(string sdl, string header)
    {
        var start = sdl.IndexOf(header + " ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the schema should declare '{0}'", header);

        var open = sdl.IndexOf('{', start);
        var close = sdl.IndexOf('}', open);
        return sdl[open..close];
    }

    private static string[] Values(string sdl, string header)
    {
        var start = sdl.IndexOf(header + " ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the schema should declare '{0}'", header);

        var open = sdl.IndexOf('{', start);
        var close = sdl.IndexOf('}', open);
        return [.. sdl[(open + 1)..close]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('"'))];
    }

    /// <summary>Stands in for a real localizer: it answers the reader's language and the code it was asked for.</summary>
    private sealed class ByLanguage : IFailureLocalizer
    {
        private static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        public string Localize(ValidationError error) => Language + " " + error.Code;

        public string Localize(InvariantViolation violation) => Language + " " + violation.Code;

        public string Localize(RefusalException refusal) => Language + " " + refusal.Code;
    }

    /// <summary>A thing without a key of its own, so whatever key it has was inferred.</summary>
    public sealed record Thing(int Id, string Name);

    public sealed class NullableLookup
    {
        [Lookup]
        public Thing? GetThingById(int id) => new(id, "a thing");
    }

    public sealed class NonNullLookup
    {
#pragma warning disable HC0113 // The point of the probe: a lookup that does not answer a nullable type.
        [Lookup]
        public Thing GetThingById(int id) => new(id, "a thing");
#pragma warning restore HC0113
    }
}
