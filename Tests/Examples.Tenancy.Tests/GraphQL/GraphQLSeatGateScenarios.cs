using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.HotChocolate.Fusion.InMemory;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.GraphQL;

/// <summary>
/// Who may ask the GraphQL schema: a signed-in caller, with a seat in the tenant the request names. The token is
/// checked in front of the gateway, and the seat in front of every root field of every module's schema but the
/// caller's own seats and accepting an invitation, with the refusals the routes answer. A field of the
/// application's own staff asks for an operator instead.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class GraphQLSeatGateScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Projects = "{ projects { nodes { id } } }";

    private const string Open =
        $$"""
        mutation($unit: UUID!) {
          projectOpenAtUnit(input: { number: "P-100", name: "Harbor wall", unitId: $unit }) { project { id } {{SampleGraphQLCalls.Errors}} }
        }
        """;

    /// <summary>The root fields a caller without a seat may ask, as the host names them to the gate.</summary>
    private static readonly string[] OpenFields = ["seatsOfMine", "invitationAccept"];

    /// <summary>The root fields of the application's own staff, as the host names them to the gate.</summary>
    private static readonly string[] OperatorFields = ["tenants", "tenantAccessHistory", "tenantProjects", "tenantProjectInspections"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task Graphql_without_a_token_is_401()
    {
        // No token is not "no seat": the caller is challenged, as on every route, before the gateway reads anything.
        using var anonymous = (await sample.SharedAsync()).Client(token: null, tenant: Harbor.Slug);

        using var asked = await anonymous.PostAsJsonAsync("/graphql", new { query = Projects }, Cancellation);
        using var schema = await anonymous.GetAsync("/graphql?sdl", Cancellation);

        asked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        schema.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the schema is not handed out either");
    }

    [Fact]
    public async Task Without_a_tenant_a_query_is_a_coded_error_and_a_mutation_a_refusal_in_its_payload()
    {
        using var nowhere = await sample.ClientAsync("rhea", tenant: null);

        // A refused query answers a top-level error with the refusal's code.
        (await nowhere.GraphQLAsync(Projects)).SingleError().Code().Should().Be(TenancyRefusals.TenantRequired);

        // A refused mutation answers it where every refusal of a mutation is: in the payload, typed.
        var payload = (await nowhere.GraphQLDataAsync(Open, new { unit = Harbor.UnitNamed("North Coast").Value })).GetProperty("projectOpenAtUnit");
        payload.GetProperty("project").ValueKind.Should().Be(JsonValueKind.Null);
        var error = payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
        error.GetProperty("__typename").GetString().Should().Be("RefusalError");
        error.GetProperty("code").GetString().Should().Be(TenancyRefusals.TenantRequired);
    }

    [Fact]
    public async Task Without_a_seat_no_root_field_reaches_a_use_case()
    {
        // A host of its own, which notes every command and query that is sent: a use case would refuse these
        // callers too, so that the gate did is shown by nothing having been sent at all.
        var sent = new SentRequests();
        await using var host = await sample.StartAsync(services =>
        {
            services.AddSingleton(sent);

            // The first step of all, before the module's own check: steps run in the order they are registered.
            services.Insert(0, ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(Noting<,>)));
        });
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var pier = await ada.ProjectNodeIdAsync(Harbor.ProjectNamed("Pier 7"));

        // With a seat a field sends its query, and it is noted: so one sent without a seat would be.
        sent.Names.Should().Contain(nameof(VisibleProjects));
        var before = sent.Names.Count;

        // Rhea has no seat in meadow, and Seth's seat in harbor is suspended. A query, the node field and a mutation
        // are each refused at the door, with what a route answers.
        using var outsider = await host.ClientAsync("rhea", DemoData.Meadow.Slug);
        var query = (await outsider.GraphQLAsync(Projects)).SingleError();
        query.Code().Should().Be(TenancyRefusals.NotSeated);
        query.Kind().Should().Be("not_permitted", "a refusal's kind is spelled as the schema spells its enum values");

        (await outsider.GraphQLAsync("query($id: ID!) { node(id: $id) { id } }", new { id = pier })).SingleError().Code().Should().Be(TenancyRefusals.NotSeated);

        var opened = (await outsider.GraphQLDataAsync(Open, new { unit = Harbor.UnitNamed("North Coast").Value })).GetProperty("projectOpenAtUnit");
        opened.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which.GetProperty("code").GetString().Should().Be(TenancyRefusals.NotSeated);

        using var seth = await host.ClientAsync("seth", Harbor.Slug);
        (await seth.GraphQLAsync("{ seats { id } }")).SingleError().Code().Should().Be(TenancyRefusals.SeatSuspended);

        sent.Names.Should().HaveCount(before, "none of the four sent a command or a query: the gate answered");
    }

    /// <summary>
    /// The scenario above tries a few fields. This one tries them all, from the schemas themselves: every field of
    /// the query type and of the mutation type of every schema the host registers, the modules' source schemas with
    /// the lookups only the gateway asks, and the administration's it serves apart, each asked of its own schema by
    /// a caller without a seat. A field added tomorrow is tried the day it is added, at either endpoint, and one the
    /// gate does not stand in front of answers something else than the gate's refusal.
    /// </summary>
    [Fact]
    public async Task Every_root_field_of_every_module_is_behind_the_gate_but_the_ones_the_host_names()
    {
        var sent = new SentRequests();
        await using var host = await sample.StartAsync(services =>
        {
            services.AddSingleton(sent);
            services.Insert(0, ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(Noting<,>)));
        });
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var pier = await ada.ProjectNodeIdAsync(Harbor.ProjectNamed("Pier 7"));

        var executors = host.Services.GetRequiredService<IRequestExecutorProvider>();
        var open = new List<string>();
        var operators = new List<string>();
        var seated = new List<string>();

        // Every schema, not only those the gateway composes: the administration's is served on its own.
        var schemas = executors.SchemaNames;
        schemas.Should().BeEquivalentTo([.. host.Services.GetRequiredService<InMemoryFusionSchemas>().SourceSchemaNames, TenantsModule.AdministrationSchema]);

        foreach (var schema in schemas)
        {
            var executor = await executors.GetExecutorAsync(schema, Cancellation);
            foreach (var (operation, root) in new[] { ("query", executor.Schema.QueryType), ("mutation", executor.Schema.MutationType) })
            {
                foreach (var field in root?.Fields.Where(field => !field.IsIntrospectionField) ?? [])
                {
                    var typed = operation == "mutation" && field.Type.NamedType() is IObjectTypeDefinition payload && payload.Fields.ContainsName("errors");
                    var document = $"{operation} {{ {field.Name}{Arguments(field, pier)} {(typed ? "{ errors { ... on CodedError { code } } }" : Selection(field.Type))} }}";
                    var before = sent.Names.Count;

                    var answer = await AskWithoutASeatAsync(host, executor, document);

                    // A refused mutation answers in its payload, typed; anything else as an error of the answer.
                    var codes = typed && answer.GetProperty("data").ValueKind == JsonValueKind.Object
                        ? [.. answer.GetProperty("data").GetProperty(field.Name).GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("code").GetString())]
                        : answer.TryGetProperty("errors", out var errors)
                            ? errors.EnumerateArray().Select(error => error.TryGetProperty("extensions", out var extensions) && extensions.TryGetProperty("code", out var code) ? code.GetString() : null).ToList()
                            : [];
                    var asked = $"{schema}: {document} answered {answer.GetRawText()}";

                    if (OpenFields.Contains(field.Name))
                    {
                        codes.Should().NotContain([TenancyRefusals.NotSeated, TenancyRefusals.OperatorsOnly], "the gate leaves this field open; {0}", asked);
                        sent.Names.Count.Should().BeGreaterThan(before, "an open field reaches its use case; {0}", asked);
                        open.Add(field.Name);
                    }
                    else
                    {
                        var operatorsOnly = OperatorFields.Contains(field.Name);
                        codes.Should().Equal([operatorsOnly ? TenancyRefusals.OperatorsOnly : TenancyRefusals.NotSeated], "the gate stands in front of this field; {0}", asked);
                        sent.Names.Should().HaveCount(before, "the gate answered, so nothing was sent; {0}", asked);
                        (operatorsOnly ? operators : seated).Add(field.Name);
                    }
                }
            }
        }

        // The names the host gives the gate are fields that exist: a field renamed without its name here would be
        // a seat's field from then on, and no longer the open one or the operators' one it was meant to be.
        // The administration's schema has Tenancy's fields too, so a name may come twice.
        open.Distinct().Should().BeEquivalentTo(OpenFields);
        operators.Distinct().Should().BeEquivalentTo(OperatorFields);
        seated.Should().HaveCountGreaterThan(40, "every other root field of the three modules asks for a seat").And.Contain(["projects", "node", "projectById", "inspectionRecord", "roleGrant", "seatGrants"]);
    }

    [Fact]
    public async Task Seats_of_mine_needs_a_token_and_neither_a_tenant_nor_a_seat()
    {
        // Picking a tenant comes before being in one: the caller's own seats are the one field the gate leaves open.
        using var tove = await sample.ClientAsync("tove", tenant: null);

        var mine = (await tove.GraphQLDataAsync("{ seatsOfMine { tenant { slug name } seat { displayName status } } }")).GetProperty("seatsOfMine");

        mine.EnumerateArray().Select(seat => seat.GetProperty("tenant").GetProperty("slug").GetString())
            .Should().BeEquivalentTo([Harbor.Slug, DemoData.Meadow.Slug]);
    }

    [Fact]
    public async Task Accepting_an_invitation_passes_without_a_seat_and_the_operators_field_only_for_an_operator()
    {
        var sent = new SentRequests();
        await using var host = await sample.StartAsync(services =>
        {
            services.AddSingleton(sent);
            services.Insert(0, ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(Noting<,>)));
        });

        // Rhea names no tenant, and her token is nobody's invitation: the refusal is the use case's own, so the
        // gate let the mutation through. Listing the invitations is inside a tenant, and stops at the gate.
        using var nowhere = await host.ClientAsync("rhea", tenant: null);
        var before = sent.Names.Count;
        var accepted = (await nowhere.GraphQLDataAsync(
            $$"""mutation($token: String!) { invitationAccept(input: { token: $token }) { seatId {{SampleGraphQLCalls.Errors}} } }""",
            new { token = "not-a-token" })).GetProperty("invitationAccept");
        accepted.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which.GetProperty("code").GetString().Should().Be(TenancyRefusals.InvitationNotFound);
        (await nowhere.GraphQLAsync("{ openInvitations { id } }")).SingleError().Code().Should().Be(TenancyRefusals.TenantRequired);
        sent.Names.Skip(before).Should().Equal(nameof(AcceptInvitation));

        // Ada holds every key of harbor and is no operator: each of the operators' fields, of whichever module,
        // refuses her before anything is sent.
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var pier = await ada.ProjectNodeIdAsync(Harbor.ProjectNamed("Pier 7"));
        var asked = sent.Names.Count;
        string[] operators =
        [
            "{ tenants { next } }",
            $$"""{ tenantAccessHistory(tenant: "{{Harbor.Id.Value}}") { nodes { id } } }""",
            $$"""{ tenantProjects(tenant: "{{Harbor.Id.Value}}") { number } }""",
            $$"""{ tenantProjectInspections(tenant: "{{Harbor.Id.Value}}", project: "{{pier}}") { title } }""",
        ];

        foreach (var document in operators)
        {
            (await ada.GraphQLAsync(document)).SingleError().Code().Should().Be(TenancyRefusals.OperatorsOnly);
        }

        // Each of those queries refuses her itself, with the same code. That none was sent says the gate did.
        sent.Names.Should().HaveCount(asked, "none of the four sent a query: the gate answered");

        // Orla is one, with no seat anywhere: her field sends its query.
        using var orla = await host.ClientAsync("orla", tenant: null);
        (await orla.GraphQLDataAsync("{ tenants { items { slug } } }")).GetProperty("tenants").GetProperty("items").GetArrayLength().Should().Be(2);
        sent.Names.Skip(asked).Should().Equal(nameof(AllTenants));
    }

    /// <summary>
    /// Asks a module's own schema, as the gateway does once tenant selection made the request's caller current:
    /// here a signed-in person who has no seat in the tenant her request names.
    /// </summary>
    private static async Task<JsonElement> AskWithoutASeatAsync(SampleFactory host, IRequestExecutor executor, string document)
    {
        using (Callers.Begin(Caller.User(DemoPeople.Rhea.Id)))
        using (TenancyCallers.Begin(TenancyCaller<TenantId, SeatId>.Nobody(TenancyRefusals.NotSeated)))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var answer = await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument(document).SetServices(scope.ServiceProvider).Build(), Cancellation);
            return JsonDocument.Parse(answer.ToJson()).RootElement.Clone();
        }
    }

    /// <summary>The arguments a field cannot be asked without, each with a value of its type.</summary>
    private static string Arguments(IOutputFieldDefinition field, string project)
    {
        var required = field.Arguments.Where(argument => argument.Type.IsNonNullType() && argument.DefaultValue is null).ToList();
        return required.Count == 0 ? "" : $"({string.Join(", ", required.Select(argument => $"{argument.Name}: {Value(argument.Type, project)}"))})";
    }

    /// <summary>
    /// A value a field's argument takes: nothing the use case would accept, only what the schema reads as a value
    /// of the type, so the request gets as far as the field. An ID is a project's: every ID of these schemas is.
    /// </summary>
    private static string Value(IType type, string project) => type switch
    {
        NonNullType required => Value(required.NullableType, project),
        ListType list => $"[{Value(list.ElementType, project)}]",
        IEnumTypeDefinition values => values.Values.First().Name,
        IInputObjectTypeDefinition input => $"{{ {string.Join(", ", input.Fields.Where(member => member.Type.IsNonNullType() && member.DefaultValue is null).Select(member => $"{member.Name}: {Value(member.Type, project)}"))} }}",
        ITypeDefinition { Name: "ID" } => $"\"{project}\"",
        ITypeDefinition { Name: "UUID" } => $"\"{Guid.Empty}\"",
        ITypeDefinition { Name: "String" } => "\"x\"",
        ITypeDefinition { Name: "Int" or "Long" } => "1",
        ITypeDefinition { Name: "Boolean" } => "true",
        ITypeDefinition { Name: "LocalDate" } => "\"2026-01-01\"",
        ITypeDefinition { Name: "DateTime" } => "\"2026-01-01T00:00:00Z\"",
        _ => throw new NotSupportedException($"No value is known here for an argument of type {type}: add one."),
    };

    /// <summary>What is selected of a field: nothing of a scalar, and of anything else only what it is.</summary>
    private static string Selection(IType type) => type.NamedType().Kind is TypeKind.Object or TypeKind.Interface or TypeKind.Union ? "{ __typename }" : "";

    /// <summary>The names of the commands and queries a host was sent, in the order they were sent.</summary>
    private sealed class SentRequests
    {
        private readonly ConcurrentQueue<string> _names = new();

        public IReadOnlyCollection<string> Names => _names;

        public void Note(Type request) => _names.Enqueue(request.Name);
    }

    /// <summary>A step every request passes on its way to its handler, which notes it and changes nothing.</summary>
    private sealed class Noting<TMessage, TResponse>(SentRequests sent) : IPipelineBehavior<TMessage, TResponse>
        where TMessage : notnull, IMessage
    {
        public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
        {
            sent.Note(typeof(TMessage));
            return next(message, cancellationToken);
        }
    }
}
