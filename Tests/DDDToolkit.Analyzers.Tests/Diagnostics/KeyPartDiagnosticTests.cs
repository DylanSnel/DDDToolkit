using DDDToolkit.Interfaces;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// <c>[KeyPart]</c>: what the generator emits for it, and DDD00028 (only on an entity or aggregate
/// root), DDD00029 (no public setter) and DDD00030 (all key parts in one file).
/// </summary>
public class KeyPartDiagnosticTests
{
    private const string Preamble =
        """
        using DDDToolkit.Abstractions.Attributes;

        namespace Sample;

        [EntityId<System.Guid>]
        public readonly partial record struct ThingId;

        [EntityId<System.Guid>]
        public readonly partial record struct RegionId;


        """;

    // ------------------------------------------------------------------ generation

    [Fact]
    public void Key_parts_are_named_in_declaration_order_through_IHasKeyParts()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public partial class Basket
            {
                public Basket(ThingId id, RegionId region, int period) : base(id) { Region = region; Period = period; }

                [KeyPart]
                public int Period { get; }

                public string Name { get; private set; } = "";

                [KeyPart]
                public RegionId Region { get; }
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint.Of("Sample.Basket"), ", global::DDDToolkit.Interfaces.IHasKeyParts");
        result.ShouldContain(Hint.Of("Sample.Basket"), "new string[] { nameof(Period), nameof(Region) }");

        var type = result.Emit().Type("Sample.Basket");
        typeof(IHasKeyParts).IsAssignableFrom(type).Should().BeTrue();
        ReadKeyParts(type).Should().Equal("Period", "Region");
    }

    [Fact]
    public void A_type_without_key_parts_gets_exactly_what_it_got_before()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public partial class Basket
            {
                public Basket(ThingId id) : base(id) { }

                public RegionId Region { get; private set; }
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldNotContain(Hint.Of("Sample.Basket"), "IHasKeyParts");
        result.ShouldContain(Hint.Of("Sample.Basket"), "partial class Basket : global::DDDToolkit.BaseTypes.AggregateRoot<global::Sample.ThingId>\n");
    }

    [Fact]
    public void A_child_entity_can_declare_key_parts()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [Entity<ThingId>]
            public partial class Line
            {
                public Line(ThingId id, RegionId region) : base(id) => Region = region;

                [KeyPart]
                public RegionId Region { get; private set; }
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        ReadKeyParts(result.Emit().Type("Sample.Line")).Should().Equal("Region");
    }

    // ------------------------------------------------------------------ DDD00028

    [Theory]
    [InlineData("public partial class Plain")]
    [InlineData("[ValueObject] public partial record Address")]
    public void A_key_part_outside_an_entity_reports_DDD00028(string declaration)
    {
        var result = GeneratorTestHost.Create(Preamble +
            declaration + """

            {
                [KeyPart]
                public RegionId Region { get; protected init; }
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00028", at: "Region");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        result.Count("DDD00028").Should().Be(1);
    }

    // ------------------------------------------------------------------ DDD00029

    [Fact]
    public void A_key_part_with_a_public_setter_reports_DDD00029_and_still_generates()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public partial class Basket
            {
                public Basket(ThingId id) : base(id) { }

                [KeyPart]
                public RegionId Region { get; set; }
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00029", at: "Region");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        result.ShouldContain(Hint.Of("Sample.Basket"), "nameof(Region)");
    }

    [Theory]
    [InlineData("{ get; }")]
    [InlineData("{ get; private set; }")]
    [InlineData("{ get; protected set; }")]
    [InlineData("{ get; init; }")]
    public void A_key_part_that_cannot_be_reassigned_from_outside_reports_nothing(string accessors)
    {
        var result = GeneratorTestHost.Create(Preamble +
            $$"""
            [AggregateRoot<ThingId>]
            public partial class Basket
            {
                public Basket(ThingId id) : base(id) { }

                [KeyPart]
                public RegionId Region {{accessors}}
            }
            """).RunCore();

        result.ShouldHaveExactlyDiagnostics();
    }

    // ------------------------------------------------------------------ DDD00030

    [Fact]
    public void Key_parts_spread_over_two_files_report_DDD00030_and_generate_nothing()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public partial class Basket
            {
                public Basket(ThingId id) : base(id) { }

                [KeyPart]
                public RegionId Region { get; }
            }
            """, path: "Basket.cs")
            .WithSource(
            """
            using DDDToolkit.Abstractions.Attributes;

            namespace Sample;

            public partial class Basket
            {
                [KeyPart]
                public int Period { get; }
            }
            """, path: "Basket.Period.cs").RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00030", at: "Basket");
        diagnostic.GetMessage().Should().Contain("Basket.cs").And.Contain("Basket.Period.cs");
        result.ShouldNotHaveGeneratedFor("Basket");
    }

    [Fact]
    public void Key_parts_in_one_file_of_a_partial_class_are_fine()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public partial class Basket
            {
                public Basket(ThingId id) : base(id) { }
            }
            """, path: "Basket.cs")
            .WithSource(
            """
            using DDDToolkit.Abstractions.Attributes;

            namespace Sample;

            public partial class Basket
            {
                [KeyPart]
                public RegionId Region { get; }

                [KeyPart]
                public int Period { get; }
            }
            """, path: "Basket.Keys.cs").RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        ReadKeyParts(result.Emit().Type("Sample.Basket")).Should().Equal("Region", "Period");
    }

    private static IReadOnlyList<string> ReadKeyParts(Type type)
        => (IReadOnlyList<string>)typeof(KeyPartDiagnosticTests)
            .GetMethod(nameof(ReadKeyPartsOf), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(type)
            .Invoke(null, null)!;

    private static IReadOnlyList<string> ReadKeyPartsOf<T>() where T : IHasKeyParts => T.KeyParts;
}
