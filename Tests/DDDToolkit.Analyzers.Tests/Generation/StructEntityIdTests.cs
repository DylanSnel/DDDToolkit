using System.Text.Json;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// What <c>[EntityId&lt;T&gt;]</c> on a <c>readonly partial record struct</c> produces, exercised as running
/// code: the snippet is compiled, emitted, loaded and the generated members are invoked. A test that only
/// grepped the generated text would pass on code that does not work.
/// </summary>
public class StructEntityIdTests
{
    private const string Preamble = "using DDDToolkit.Abstractions.Attributes;\nusing System;\n\nnamespace Sample;\n\n";

    private static GeneratorRunOutcome GuidId(string prefix = "PFX")
        => GeneratorTestHost.Create(Preamble +
            $"""
             [EntityId<Guid>("{prefix}")]
             public readonly partial record struct OrderId;
             """).RunCore();

    // ------------------------------------------------------------------ Guid ids

    [Fact]
    public void A_struct_id_over_Guid_exposes_its_value_and_prefix()
    {
        var emitted = GuidId().Emit();
        var guid = Guid.NewGuid();

        var id = emitted.New("Sample.OrderId", guid);

        emitted.Property(id, "Value").Should().Be(guid);
        emitted.StaticProperty("Sample.OrderId", "IdPrefix").Should().Be("PFX");
    }

    [Fact]
    public void ToString_writes_the_prefix_before_the_value()
    {
        var emitted = GuidId().Emit();
        var guid = Guid.NewGuid();

        var id = emitted.New("Sample.OrderId", guid);

        id.ToString().Should().Be("PFX_" + guid.ToString());
    }

    [Fact]
    public void Without_a_prefix_ToString_is_just_the_value()
    {
        var emitted = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<Guid>]
            public readonly partial record struct OrderId;
            """).RunCore().Emit();

        var guid = Guid.NewGuid();

        emitted.New("Sample.OrderId", guid).ToString().Should().Be(guid.ToString());
        emitted.StaticProperty("Sample.OrderId", "IdPrefix").Should().Be("");
    }

    [Fact]
    public void Parse_accepts_the_prefixed_and_the_bare_form()
    {
        var emitted = GuidId().Emit();
        var guid = Guid.NewGuid();

        var fromPrefixed = emitted.CallStatic("Sample.OrderId", "Parse", "PFX_" + guid);
        var fromBare = emitted.CallStatic("Sample.OrderId", "Parse", guid.ToString());

        emitted.Property(fromPrefixed!, "Value").Should().Be(guid);
        emitted.Property(fromBare!, "Value").Should().Be(guid);
        fromPrefixed.Should().Be(fromBare);
    }

    [Fact]
    public void Parse_throws_a_FormatException_naming_the_type()
    {
        var emitted = GuidId().Emit();

        var act = () => emitted.CallStatic("Sample.OrderId", "Parse", "not-an-id");

        act.Should().Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<FormatException>()
            .WithMessage("*OrderId*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("PFX_garbage")]
    [InlineData("PFX_")]
    public void TryParse_refuses_garbage_and_null(string? input)
    {
        var emitted = GuidId().Emit();

        var (success, value) = emitted.TryParse("Sample.OrderId", input);

        success.Should().BeFalse();
        value.Should().Be(emitted.Default("Sample.OrderId"), "a failed TryParse leaves the result at default");
    }

    [Fact]
    public void TryParse_round_trips_whatever_ToString_produced()
    {
        var emitted = GuidId().Emit();
        var original = emitted.CallStatic("Sample.OrderId", "CreateUnique")!;

        var (success, parsed) = emitted.TryParse("Sample.OrderId", original.ToString());

        success.Should().BeTrue();
        parsed.Should().Be(original);
    }

    [Fact]
    public void Empty_is_the_default_and_knows_it()
    {
        var emitted = GuidId().Emit();

        var empty = emitted.StaticProperty("Sample.OrderId", "Empty")!;

        empty.Should().Be(emitted.Default("Sample.OrderId"));
        emitted.Property(empty, "IsEmpty").Should().Be(true);
        emitted.Property(empty, "Value").Should().Be(Guid.Empty);
        emitted.Property(emitted.CallStatic("Sample.OrderId", "CreateUnique")!, "IsEmpty").Should().Be(false);
    }

    [Fact]
    public void CreateUnique_makes_distinct_version_4_ids()
    {
        var emitted = GuidId().Emit();

        var ids = Enumerable.Range(0, 50).Select(_ => emitted.CallStatic("Sample.OrderId", "CreateUnique")!).ToList();

        ids.Distinct().Should().HaveCount(50);
        ids.Select(id => Version((Guid)emitted.Property(id, "Value")!)).Should().AllBeEquivalentTo(4);
    }

    [Fact]
    public void CreateSequential_makes_distinct_time_ordered_version_7_ids()
    {
        var emitted = GuidId().Emit();

        var guids = Enumerable.Range(0, 50)
            .Select(_ => (Guid)emitted.Property(emitted.CallStatic("Sample.OrderId", "CreateSequential")!, "Value")!)
            .ToList();

        guids.Distinct().Should().HaveCount(50);
        guids.Select(Version).Should().AllBeEquivalentTo(7, "CreateSequential must produce UUIDv7");

        // The first 48 bits of a UUIDv7 are the creation time in milliseconds, so they never go backwards.
        var timestamps = guids.Select(Timestamp).ToList();
        timestamps.Should().BeInAscendingOrder();
    }

    [Fact]
    public void CompareTo_orders_by_the_underlying_value()
    {
        var emitted = GuidId().Emit();
        var low = emitted.New("Sample.OrderId", Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var high = emitted.New("Sample.OrderId", Guid.Parse("00000000-0000-0000-0000-000000000002"));

        ((int)emitted.Call(low, "CompareTo", high)!).Should().BeNegative();
        ((int)emitted.Call(high, "CompareTo", low)!).Should().BePositive();
        ((int)emitted.Call(low, "CompareTo", emitted.New("Sample.OrderId", Guid.Parse("00000000-0000-0000-0000-000000000001")))!).Should().Be(0);
    }

    [Fact]
    public void The_explicit_operators_convert_in_both_directions()
    {
        var emitted = GuidId().Emit();
        var idType = emitted.Type("Sample.OrderId");
        var guid = Guid.NewGuid();

        var id = emitted.Convert(typeof(Guid), idType, guid)!;
        var back = emitted.Convert(idType, typeof(Guid), id);

        emitted.Property(id, "Value").Should().Be(guid);
        back.Should().Be(guid);
    }

    [Fact]
    public void Two_ids_over_the_same_value_are_equal()
    {
        var emitted = GuidId().Emit();
        var guid = Guid.NewGuid();

        var left = emitted.New("Sample.OrderId", guid);
        var right = emitted.New("Sample.OrderId", guid);

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
        emitted.New("Sample.OrderId", Guid.NewGuid()).Should().NotBe(left);
    }

    [Fact]
    public void The_id_implements_IEntityId_IComparable_and_IParsable()
    {
        var emitted = GuidId().Emit();
        var idType = emitted.Type("Sample.OrderId");

        typeof(DDDToolkit.Abstractions.Interfaces.IEntityId<Guid>).IsAssignableFrom(idType).Should().BeTrue();
        typeof(IComparable<>).MakeGenericType(idType).IsAssignableFrom(idType).Should().BeTrue();
        typeof(IParsable<>).MakeGenericType(idType).IsAssignableFrom(idType).Should().BeTrue();
    }

    [Fact]
    public void IParsable_is_wired_to_the_generated_Parse()
    {
        var emitted = GuidId().Emit();
        var idType = emitted.Type("Sample.OrderId");
        var guid = Guid.NewGuid();

        // Reached through the interface, which is how generic code such as model binding calls it.
        var parsable = typeof(IParsable<>).MakeGenericType(idType);
        var parse = idType.GetInterfaceMap(parsable).TargetMethods.Single(method => method.Name.EndsWith("Parse", StringComparison.Ordinal) && method.ReturnType == idType);

        var id = parse.Invoke(null, ["PFX_" + guid, null])!;

        emitted.Property(id, "Value").Should().Be(guid);
    }

    [Fact]
    public void The_id_survives_a_System_Text_Json_round_trip_as_its_bare_value()
    {
        var emitted = GuidId().Emit();
        var guid = Guid.NewGuid();
        var id = emitted.New("Sample.OrderId", guid);

        var json = EmittedAssembly.ToJson(id);

        json.Should().Be(JsonSerializer.Serialize(guid), "the id serializes as the value it wraps, not as an object");
        emitted.JsonRoundTrip(id).Should().Be(id);
    }

    [Fact]
    public void The_id_survives_a_System_Text_Json_round_trip_as_a_dictionary_key()
    {
        var emitted = GuidId().Emit();
        var guid = Guid.NewGuid();
        var id = emitted.New("Sample.OrderId", guid);

        var (json, roundTripped) = emitted.DictionaryKeyRoundTrip(id, "one");

        json.Should().Contain("PFX_" + guid, "as a property name the id uses its prefixed textual form");
        roundTripped.Keys.Cast<object>().Single().Should().Be(id);
        roundTripped[id].Should().Be("one");
    }

    [Fact]
    public void The_id_serializes_inside_a_containing_object()
    {
        var emitted = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<Guid>("PFX")]
            public readonly partial record struct OrderId;

            public sealed class Envelope
            {
                public OrderId Id { get; set; }
            }
            """).RunCore().Emit();

        var guid = Guid.NewGuid();
        var envelope = emitted.New("Sample.Envelope");
        emitted.Type("Sample.Envelope").GetProperty("Id")!.SetValue(envelope, emitted.New("Sample.OrderId", guid));

        var json = EmittedAssembly.ToJson(envelope);

        json.Should().Be($$"""{"Id":"{{guid}}"}""");
        var back = emitted.FromJson("Sample.Envelope", json)!;
        emitted.Property(back, "Id").Should().Be(emitted.New("Sample.OrderId", guid));
    }

    // ------------------------------------------------------------------ other underlying types

    [Fact]
    public void A_struct_id_over_string_parses_and_prints()
    {
        var emitted = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<string>("SKU")]
            public readonly partial record struct Sku;
            """).RunCore().Emit();

        var id = emitted.New("Sample.Sku", "abc-1");

        id.ToString().Should().Be("SKU_abc-1");
        emitted.CallStatic("Sample.Sku", "Parse", "SKU_abc-1").Should().Be(id);
        emitted.CallStatic("Sample.Sku", "Parse", "abc-1").Should().Be(id);
        emitted.TryParse("Sample.Sku", null).Success.Should().BeFalse("null is not an id");
        emitted.Property(emitted.Default("Sample.Sku"), "IsEmpty").Should().Be(true, "the default string id holds null");
    }

    [Fact]
    public void A_struct_id_over_string_has_no_Guid_factories()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<string>]
            public readonly partial record struct Sku;
            """).RunCore();

        result.ShouldNotContain("Sample.Sku.g.cs", "CreateUnique");
        result.ShouldNotContain("Sample.Sku.g.cs", "CreateSequential");
    }

    [Theory]
    [InlineData("int", "42")]
    [InlineData("long", "9007199254740993")]
    public void A_struct_id_over_an_integral_type_parses_with_the_invariant_culture(string underlying, string text)
    {
        var emitted = GeneratorTestHost.Create(Preamble +
            $"""
             [EntityId<{underlying}>("N")]
             public readonly partial record struct Number;
             """).RunCore().Emit();

        var parsed = emitted.CallStatic("Sample.Number", "Parse", "N_" + text)!;

        parsed.ToString().Should().Be("N_" + text);
        emitted.CallStatic("Sample.Number", "Parse", text).Should().Be(parsed);
        emitted.TryParse("Sample.Number", "1.5").Success.Should().BeFalse();
        emitted.Property(emitted.Default("Sample.Number"), "IsEmpty").Should().Be(true);
    }

    [Fact]
    public void A_struct_id_over_a_type_without_TryParse_gets_no_parsing_at_all()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            /// <summary>A value type with no static TryParse(string, IFormatProvider, out T).</summary>
            public readonly record struct Ticket(int Number);

            [EntityId<Ticket>]
            public readonly partial record struct TicketId;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldNotContain("Sample.TicketId.g.cs", "static TicketId Parse(");
        result.ShouldNotContain("Sample.TicketId.g.cs", "TryParse");
        result.ShouldNotContain("Sample.TicketId.g.cs", "IParsable");
        result.ShouldContain("Sample.TicketId.g.cs", "/// <summary>Prefix written by ToString().</summary>");

        var emitted = result.Emit();
        var idType = emitted.Type("Sample.TicketId");

        emitted.HasMember("Sample.TicketId", "Parse").Should().BeFalse();
        emitted.HasMember("Sample.TicketId", "TryParse").Should().BeFalse();
        idType.GetInterfaces().Should().NotContain(@interface => @interface.Name.StartsWith("IParsable", StringComparison.Ordinal));

        // Everything that does not need parsing still works.
        var ticket = emitted.New("Sample.Ticket", 7);
        var id = emitted.New("Sample.TicketId", ticket);
        emitted.Property(id, "Value").Should().Be(ticket);
        emitted.Property(id, "IsEmpty").Should().Be(false);
        id.Should().Be(emitted.New("Sample.TicketId", emitted.New("Sample.Ticket", 7)));
    }

    [Fact]
    public void A_struct_id_over_a_type_without_TryParse_still_serializes_to_JSON()
    {
        var emitted = GeneratorTestHost.Create(Preamble +
            """
            public readonly record struct Ticket(int Number);

            [EntityId<Ticket>]
            public readonly partial record struct TicketId;
            """).RunCore().Emit();

        var id = emitted.New("Sample.TicketId", emitted.New("Sample.Ticket", 7));

        EmittedAssembly.ToJson(id).Should().Be("""{"Number":7}""");
        emitted.JsonRoundTrip(id).Should().Be(id);
    }

    // ------------------------------------------------------------------ helpers

    private static int Version(Guid guid) => (guid.ToByteArray(bigEndian: true)[6] & 0xF0) >> 4;

    private static long Timestamp(Guid guid)
    {
        var bytes = guid.ToByteArray(bigEndian: true);
        long timestamp = 0;
        for (var i = 0; i < 6; i++)
        {
            timestamp = (timestamp << 8) | bytes[i];
        }

        return timestamp;
    }
}
