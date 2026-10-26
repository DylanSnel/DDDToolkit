using System.Text.Json;
using DDDToolkit.HotChocolate.Tests.Harbor.Api;
using DDDToolkit.HotChocolate.Tests.Harbor.Api.GraphQl;
using DDDToolkit.HotChocolate.Tests.Harbor.Domain;
using DDDToolkit.HotChocolate.Types;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// A module split into projects by layer declares its ids in a domain or contracts project that does not reference
/// HotChocolate, so they get no nested <c>ChangeTypeProvider</c> and no nested <c>NodeIdValueSerializer</c>. The
/// module's API project, which declares no id itself, binds them: its generated
/// <c>Add{Module}GraphQlRuntimeBindings()</c> registers the package's generic provider and serializer for each.
/// <para>
/// These run against two real projects built that way, <c>DDDToolkit.HotChocolate.Tests.Harbor.Domain</c> and
/// <c>DDDToolkit.HotChocolate.Tests.Harbor.Api</c>, so what is proven is what a build gives an application: the
/// ids cross the schema exactly as ids that bind themselves do.
/// </para>
/// </summary>
public class IdsOfOtherProjectsTests
{
    [Fact]
    public async Task A_single_value_id_from_another_assembly_round_trips_as_a_scalar()
    {
        var guid = HarborData.VesselGuid.ToString();

        // Out.
        (await DataAsync("{ vesselId }")).GetProperty("vesselId").GetString().Should().Be(guid);

        // In, as a literal. Only a real VesselId prints its prefix, so the resolver was handed the id and not a Guid.
        var literal = await DataAsync($$"""{ echoVesselId(id: "{{guid}}") describeVesselId(id: "{{guid}}") }""");
        literal.GetProperty("echoVesselId").GetString().Should().Be(guid);
        literal.GetProperty("describeVesselId").GetString().Should().Be("VSL_" + guid);

        // In, through a variable.
        var variable = await DataAsync(
            "query Echo($id: UUID!) { echoVesselId(id: $id) describeVesselId(id: $id) }",
            new Dictionary<string, object?> { ["id"] = guid });
        variable.GetProperty("echoVesselId").GetString().Should().Be(guid);
        variable.GetProperty("describeVesselId").GetString().Should().Be("VSL_" + guid);
    }

    [Fact]
    public async Task Each_id_is_printed_as_the_scalar_of_its_value()
    {
        var sdl = (await ExecutorAsync()).Schema.ToString();

        sdl.Should().Contain("vesselId: UUID!");
        sdl.Should().Contain("missingVesselId: UUID");
        sdl.Should().Contain("vesselIds: [UUID!]!");
        sdl.Should().Contain("skipper: UUID!");
        sdl.Should().Contain("validSkipper: UUID!");
        sdl.Should().Contain("berth: Int!");
        sdl.Should().Contain("quay: String!");
        sdl.Should().Contain("voyage: Long!");
        sdl.Should().Contain("mooring: Short!");
        sdl.Should().Contain("callSign: String!");
        sdl.Should().Contain("echoVesselId(id: UUID!): UUID!");
        sdl.Should().Contain("echoCallSign(callSign: String!): String!");
    }

    [Fact]
    public async Task An_id_over_each_value_is_serialized_as_that_value()
    {
        var data = await DataAsync("{ berth quay voyage mooring callSign missingVesselId vesselIds }");

        data.GetProperty("berth").GetInt32().Should().Be(HarborData.Berth);
        data.GetProperty("quay").GetString().Should().Be(HarborData.Quay);
        data.GetProperty("voyage").GetInt64().Should().Be(HarborData.Voyage);
        data.GetProperty("mooring").GetInt16().Should().Be(HarborData.Mooring);
        data.GetProperty("callSign").GetString().Should().Be(HarborData.Sign);
        data.GetProperty("missingVesselId").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("vesselIds").EnumerateArray().Select(element => element.GetString())
            .Should().Equal(HarborData.VesselGuid.ToString(), HarborData.OtherVesselGuid.ToString());
    }

    [Fact]
    public async Task A_class_id_and_its_twin_from_another_assembly_serialize_as_the_same_value()
    {
        var data = await DataAsync("{ skipper validSkipper checkedSkipper }");

        data.GetProperty("skipper").GetString().Should().Be(HarborData.SkipperGuid.ToString());
        data.GetProperty("validSkipper").GetString().Should().Be(HarborData.SkipperGuid.ToString());

        // A field declared as the id that answers a twin: the twin's own provider converts what is really there.
        data.GetProperty("checkedSkipper").GetString().Should().Be(HarborData.SkipperGuid.ToString());
    }

    [Fact]
    public async Task A_twin_from_another_assembly_is_validated_when_it_is_read()
    {
        var accepted = await DataAsync($$"""{ echoCallSign(callSign: "{{HarborData.Sign}}") }""");
        accepted.GetProperty("echoCallSign").GetString().Should().Be(HarborData.Sign);

        // The twin is built through its public constructor, which validates: a call sign that breaks the rule
        // never reaches the resolver.
        var rejected = await RawAsync("""{ echoCallSign(callSign: "no") }""");
        rejected.GetProperty("errors").GetArrayLength().Should().BePositive();
        (!rejected.TryGetProperty("data", out var nothing) || nothing.ValueKind == JsonValueKind.Null).Should().BeTrue("the resolver was never called");
    }

    [Fact]
    public async Task An_input_object_reads_the_ids_of_another_assembly()
    {
        var expected = $"SKP_{HarborData.SkipperGuid}/{HarborData.Berth}/{HarborData.Sign}";

        var literal = await DataAsync(
            $$"""{ describeDocking(docking: { skipper: "{{HarborData.SkipperGuid}}", berth: {{HarborData.Berth}}, callSign: "{{HarborData.Sign}}" }) }""");
        literal.GetProperty("describeDocking").GetString().Should().Be(expected);

        var variable = await DataAsync(
            "query Describe($docking: DockingInput!) { describeDocking(docking: $docking) }",
            new Dictionary<string, object?>
            {
                ["docking"] = new Dictionary<string, object?>
                {
                    ["skipper"] = HarborData.SkipperGuid.ToString(),
                    ["berth"] = HarborData.Berth,
                    ["callSign"] = HarborData.Sign,
                },
            });
        variable.GetProperty("describeDocking").GetString().Should().Be(expected);

        var sdl = (await ExecutorAsync()).Schema.ToString();
        sdl.Should().Contain("input DockingInput");
        sdl.Should().Contain("skipper: UUID!");
        sdl.Should().Contain("berth: Int!");
    }

    // ------------------------------------------------------------------ Relay node ids

    [Fact]
    public async Task A_referenced_id_reads_back_from_the_node_id_it_was_written_into()
    {
        var id = (await DataAsync("{ vessel { id } }")).GetProperty("vessel").GetProperty("id").GetString()!;

        // node(id:) hands the resolver the VesselId inside the node id, and so does an [ID] argument.
        var data = await DataAsync($$"""{ node(id: "{{id}}") { id ... on Vessel { callSign berth skipper } } describeVessel(id: "{{id}}") }""");

        var node = data.GetProperty("node");
        node.GetProperty("id").GetString().Should().Be(id);
        node.GetProperty("callSign").GetString().Should().Be(HarborData.Sign);
        node.GetProperty("berth").GetInt32().Should().Be(HarborData.Berth);
        node.GetProperty("skipper").GetString().Should().Be(HarborData.SkipperGuid.ToString());
        data.GetProperty("describeVessel").GetString().Should().Be("VSL_" + HarborData.VesselGuid);
    }

    [Fact]
    public async Task The_node_id_of_a_referenced_id_is_the_one_HotChocolate_writes_for_the_bare_value()
    {
        // The same vessel, in a schema where its id is the bare Guid HotChocolate serializes by itself: any server
        // or Fusion gateway that reads the one reads the other.
        var expected = await BareNodeIdAsync("vessel");

        var id = (await DataAsync("{ vessel { id } }")).GetProperty("vessel").GetProperty("id").GetString();

        id.Should().Be(expected);
    }

    [Fact]
    public async Task A_node_id_written_elsewhere_is_read_as_the_id_it_names()
    {
        // A node id this schema never wrote, of a vessel it does not know: it is read as that vessel's id, which
        // the node resolver then finds nothing for, rather than as an empty id or as the vessel it does know.
        var other = await BareNodeIdAsync("otherVessel");

        var data = await DataAsync($$"""{ node(id: "{{other}}") { id } describeVessel(id: "{{other}}") }""");

        data.GetProperty("node").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("describeVessel").GetString().Should().Be("VSL_" + HarborData.OtherVesselGuid);
    }

    // ------------------------------------------------------------------ bound more than once

    [Fact]
    public async Task Ids_two_projects_bind_in_one_schema_cross_it_as_ids_bound_once_do()
    {
        // Two modules that both name a published id each bind it, and one schema over both then hears every
        // registration twice: the same scalar, the same provider and the same node id serializer.
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddHarborSchema()
            .AddHarborGraphQlRuntimeBindings()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var once = await ExecutorAsync();

        executor.Schema.ToString().Should().Be(once.Schema.ToString());

        var guid = HarborData.VesselGuid.ToString();
        var result = await executor.ExecuteAsync(
            $$"""{ vessel { id callSign } echoVesselId(id: "{{guid}}") describeVesselId(id: "{{guid}}") echoCallSign(callSign: "{{HarborData.Sign}}") }""",
            TestContext.Current.CancellationToken);
        var answer = Parse(result.ToJson());
        answer.TryGetProperty("errors", out _).Should().BeFalse();

        var data = answer.GetProperty("data");
        data.GetProperty("echoVesselId").GetString().Should().Be(guid);
        data.GetProperty("describeVesselId").GetString().Should().Be("VSL_" + guid);
        data.GetProperty("echoCallSign").GetString().Should().Be(HarborData.Sign);

        var id = data.GetProperty("vessel").GetProperty("id").GetString()!;
        id.Should().Be((await DataAsync("{ vessel { id } }")).GetProperty("vessel").GetProperty("id").GetString());

        var node = await executor.ExecuteAsync($$"""{ node(id: "{{id}}") { id } describeVessel(id: "{{id}}") }""", TestContext.Current.CancellationToken);
        var read = Parse(node.ToJson());
        read.TryGetProperty("errors", out _).Should().BeFalse();
        read.GetProperty("data").GetProperty("node").GetProperty("id").GetString().Should().Be(id);
        read.GetProperty("data").GetProperty("describeVessel").GetString().Should().Be("VSL_" + guid);
    }

    // ------------------------------------------------------------------ by hand

    [Fact]
    public async Task A_type_no_generated_method_binds_is_bound_by_hand_with_the_same_classes()
    {
        // What an application writes for a type of an assembly without a module, such as a shared kernel's: this
        // schema calls no generated method at all.
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .BindRuntimeType<QuayCode, StringType>()
            .AddTypeConverter<SingleValueChangeTypeProvider<QuayCode, string>>()
            .BindRuntimeType<ValidCallSign, StringType>()
            .AddTypeConverter<SingleValueChangeTypeProvider<ValidCallSign, string>>()
            .AddQueryType<ByHandQuery>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        executor.Schema.ToString().Should().Contain("echo(quay: String!, callSign: String!): String!");

        var result = await executor.ExecuteAsync(
            $$"""{ quay echo(quay: "{{HarborData.Quay}}", callSign: "{{HarborData.Sign}}") }""",
            TestContext.Current.CancellationToken);
        var data = Parse(result.ToJson()).GetProperty("data");

        data.GetProperty("quay").GetString().Should().Be(HarborData.Quay);
        data.GetProperty("echo").GetString().Should().Be(HarborData.Quay + "/" + HarborData.Sign);
    }

    // ------------------------------------------------------------------ the schema

    private static ValueTask<IRequestExecutor> ExecutorAsync()
        => new ServiceCollection()
            .AddGraphQL()
            .AddHarborSchema()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

    private static async Task<JsonElement> DataAsync(string query, IReadOnlyDictionary<string, object?>? variables = null)
    {
        var raw = await RawAsync(query, variables);
        if (raw.TryGetProperty("errors", out var failure))
        {
            Assert.Fail("The GraphQL request failed: " + failure);
        }

        return raw.GetProperty("data");
    }

    private static async Task<JsonElement> RawAsync(string query, IReadOnlyDictionary<string, object?>? variables = null)
    {
        var executor = await ExecutorAsync();
        var result = variables is null
            ? await executor.ExecuteAsync(query, TestContext.Current.CancellationToken)
            : await executor.ExecuteAsync(query, variables, TestContext.Current.CancellationToken);
        return Parse(result.ToJson());
    }

    /// <summary>The node id HotChocolate writes for a vessel whose id is a bare <see cref="Guid"/>, with no toolkit type in the schema.</summary>
    private static async Task<string> BareNodeIdAsync(string field)
    {
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddGlobalObjectIdentification()
            .AddType<RawVesselType>()
            .AddQueryType<RawQuery>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var result = await executor.ExecuteAsync("{ " + field + " { id } }", TestContext.Current.CancellationToken);

        return Parse(result.ToJson()).GetProperty("data").GetProperty(field).GetProperty("id").GetString()!;
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public sealed class ByHandQuery
    {
        public QuayCode GetQuay() => QuayCode.Create(HarborData.Quay);

        public string Echo(QuayCode quay, ValidCallSign callSign) => quay.Value + "/" + callSign.Value;
    }

    public sealed record RawVessel(Guid Id);

    public sealed class RawQuery
    {
        public RawVessel GetVessel() => new(HarborData.VesselGuid);

        public RawVessel GetOtherVessel() => new(HarborData.OtherVesselGuid);
    }

    public sealed class RawVesselType : ObjectType<RawVessel>
    {
        protected override void Configure(IObjectTypeDescriptor<RawVessel> descriptor)
            => descriptor.Name("Vessel").ImplementsNode().IdField(vessel => vessel.Id)
                .ResolveNode((_, id) => Task.FromResult<RawVessel?>(new RawVessel(id)));
    }
}
