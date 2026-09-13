namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00009: a class carrying both [Entity] and [AggregateRoot].
/// <para>
/// Before this diagnostic existed, both attribute providers produced a definition for the same class and
/// both output steps added a source with the same hint name. That throws inside the generator, so the
/// author saw only CS8785 about a crashed generator and nothing at all about the two attributes.
/// </para>
/// </summary>
public class ConflictingEntityAttributeTests
{
    private const string Preamble =
        """
        using DDDToolkit.Abstractions.Attributes;

        namespace Sample;

        [EntityId<System.Guid>]
        public readonly partial record struct ThingId;


        """;

    [Fact]
    public void Both_attributes_on_one_class_report_DDD00009_and_generate_nothing()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [Entity<ThingId>]
            [AggregateRoot<ThingId>]
            public partial class Widget;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00009", at: "Widget");
        result.ShouldNotHaveGeneratedFor("Widget");
    }

    [Fact]
    public void The_clash_is_reported_once_even_though_two_providers_see_the_class()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [Entity<ThingId>]
            [AggregateRoot<ThingId>]
            public partial class Widget;
            """).RunCore();

        result.GeneratorDiagnostics
            .Count(diagnostic => diagnostic.Id == "DDD00009")
            .Should().Be(1, "the entity path refuses silently and only the aggregate-root path reports");
    }

    [Fact]
    public void The_generator_does_not_crash()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [Entity<ThingId>]
            [AggregateRoot<ThingId>]
            public partial class Widget;
            """).RunCore();

        result.GeneratorDiagnostics
            .Should().NotContain(diagnostic => diagnostic.Id == "CS8785", "a crashed generator tells the author nothing");
    }

    [Fact]
    public void Either_attribute_on_its_own_is_still_fine()
    {
        var entity = GeneratorTestHost.Create(Preamble +
            """
            [Entity<ThingId>]
            public partial class Line;
            """).RunCore();

        entity.ShouldNotHaveDiagnostic("DDD00009");
        entity.ShouldCompile();

        var root = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public partial class Basket;
            """).RunCore();

        root.ShouldNotHaveDiagnostic("DDD00009");
        root.ShouldCompile();
    }
}
