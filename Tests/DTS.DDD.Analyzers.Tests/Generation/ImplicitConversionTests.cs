using System.Reflection;
using DTS.DDD.Exceptions;

namespace DTS.DDD.Analyzers.Tests.Generation;

/// <summary>
/// Unless <c>DDD_ImplicitConversions</c> is <c>false</c>, a <c>[SingleValueObject&lt;T&gt;]</c> converts to its value,
/// and a value converts to the always-valid twin, which validates and throws for an invalid value. The plain record gets
/// no conversion from the value, and a record whose author declared a conversion operator gets none from the generator.
/// </summary>
public class ImplicitConversionTests
{
    private const string Preamble = "using DTS.DDD.Abstractions.Attributes;\nusing System.Collections.Generic;\n\nnamespace Sample;\n\n";

    private const string Language =
        """
        [SingleValueObject<string>]
        public partial record Language
        {
            public static Language Create(string value) => new(value);

            protected override bool Validate() => Value is "nl-NL" or "en-GB";
        }
        """;

    private static GeneratorRunOutcome Run(string source, bool conversions = true)
    {
        var host = GeneratorTestHost.Create(Preamble + source);
        return (conversions ? host : host.WithBuildProperty("DDD_ImplicitConversions", "false")).RunCore();
    }

    private static MethodInfo[] Conversions(Type type)
        => [.. type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(method => method.Name == "op_Implicit")];

    [Fact]
    public void A_project_that_sets_the_property_to_false_gets_no_conversion()
    {
        var result = Run(Language, conversions: false);

        result.ShouldCompile();
        var emitted = result.Emit();
        Conversions(emitted.Type("Sample.Language")).Should().BeEmpty();
        Conversions(emitted.Type("Sample.ValidLanguage")).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    public void A_project_that_leaves_the_property_out_or_sets_it_true_gets_both_conversions(string? setting)
    {
        var host = GeneratorTestHost.Create(Preamble + Language);
        var emitted = (setting is null ? host.WithoutBuildProperties() : host.WithBuildProperty("DDD_ImplicitConversions", setting)).RunCore().Emit();

        Conversions(emitted.Type("Sample.Language")).Should().ContainSingle();
        Conversions(emitted.Type("Sample.ValidLanguage")).Should().ContainSingle();
    }

    [Fact]
    public void A_value_object_converts_to_its_value_and_a_value_converts_to_the_twin()
    {
        var result = Run(Language);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
        var emitted = result.Emit();

        var toValue = Conversions(emitted.Type("Sample.Language")).Should().ContainSingle().Which;
        toValue.ReturnType.Should().Be(typeof(string));
        toValue.Invoke(null, [emitted.CallStatic("Sample.Language", "Create", "nl-NL")]).Should().Be("nl-NL");

        var toTwin = Conversions(emitted.Type("Sample.ValidLanguage")).Should().ContainSingle().Which;
        toTwin.GetParameters().Single().ParameterType.Should().Be(typeof(string));
        var valid = toTwin.Invoke(null, ["en-GB"])!;
        valid.GetType().Name.Should().Be("ValidLanguage");
        emitted.Property(valid, "Value").Should().Be("en-GB");

        // The plain record gets no conversion from the value: it may be invalid, and a silent conversion would hide that.
        Conversions(emitted.Type("Sample.Language")).Should().NotContain(method => method.ReturnType.Name == "Language");
    }

    [Fact]
    public void An_invalid_value_converted_to_the_twin_throws()
    {
        var emitted = Run(Language).Emit();
        var toTwin = Conversions(emitted.Type("Sample.ValidLanguage")).Single();

        FluentActions.Invoking(() => toTwin.Invoke(null, ["fr-FR"]))
            .Should().Throw<TargetInvocationException>().WithInnerException<InvalidValueObjectException>();
    }

    [Fact]
    public void A_collection_of_twins_is_written_as_its_values()
    {
        var result = Run(
            Language +
            """

            public static class Languages
            {
                public static IReadOnlyList<ValidLanguage> Supported { get; } = ["nl-NL", "en-GB"];

                public static string Tag(Language language) => language;
            }
            """);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
        var emitted = result.Emit();
        ((System.Collections.IEnumerable)emitted.StaticProperty("Sample.Languages", "Supported")!).Cast<object>().Should().HaveCount(2);
    }

    [Fact]
    public void A_record_with_a_conversion_of_its_author_gets_none_from_the_generator_and_its_twin_still_does()
    {
        var result = Run(
            """
            [SingleValueObject<string>]
            public partial record Code
            {
                public static Code Create(string value) => new(value);

                public static explicit operator string(Code code) => code.Value;
            }
            """);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
        var emitted = result.Emit();
        Conversions(emitted.Type("Sample.Code")).Should().BeEmpty();
        Conversions(emitted.Type("Sample.ValidCode")).Should().ContainSingle();
    }

    [Fact]
    public void A_value_type_value_converts_both_ways()
    {
        var result = Run(
            """
            [SingleValueObject<int>]
            public partial record Percentage
            {
                public static Percentage Create(int value) => new(value);

                protected override bool Validate() => Value is >= 0 and <= 100;
            }
            """);

        result.ShouldCompile();
        var emitted = result.Emit();
        Conversions(emitted.Type("Sample.Percentage")).Single().Invoke(null, [emitted.CallStatic("Sample.Percentage", "Create", 40)]).Should().Be(40);
        emitted.Property(Conversions(emitted.Type("Sample.ValidPercentage")).Single().Invoke(null, [60])!, "Value").Should().Be(60);
    }
}
