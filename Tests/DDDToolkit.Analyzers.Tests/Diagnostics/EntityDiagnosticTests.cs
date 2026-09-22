namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00002 (entities and aggregate roots must be classes), DDD00005 (must be partial) and DDD00020
/// (generated collection properties must be get-only).
/// </summary>
public class EntityDiagnosticTests
{
    private const string Preamble =
        """
        using DDDToolkit.Abstractions.Attributes;

        namespace Sample;

        [EntityId<System.Guid>]
        public readonly partial record struct ThingId;


        """;

    [Fact]
    public void Entity_on_a_record_reports_DDD00002_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [Entity<ThingId>]
            public partial record Widget;
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00002", at: "Widget");
        diagnostic.GetMessage().Should().Contain("Entity");
        result.ShouldNotHaveGeneratedFor("Widget");
    }

    [Fact]
    public void Entity_on_a_struct_reports_DDD00002_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [Entity<ThingId>]
            public partial struct Widget
            {
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00002", at: "Widget");
        result.ShouldNotHaveGeneratedFor("Widget");
    }

    [Fact]
    public void AggregateRoot_on_a_record_reports_DDD00002_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public partial record Basket;
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00002", at: "Basket");
        diagnostic.GetMessage().Should().Contain("AggregateRoot");
        result.ShouldNotHaveGeneratedFor("Basket");
    }

    [Fact]
    public void AggregateRoot_on_a_record_struct_reports_DDD00002_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public readonly partial record struct Basket;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00002", at: "Basket");
        result.ShouldNotHaveGeneratedFor("Basket");
    }

    [Fact]
    public void A_non_partial_entity_reports_DDD00005_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [Entity<ThingId>]
            public class Widget
            {
            }
            """).RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00005");
        result.ShouldHaveDiagnostic("DDD00005", at: "Widget");
        result.ShouldNotHaveGeneratedFor("Widget");
    }

    [Fact]
    public void A_non_partial_aggregate_root_reports_DDD00005_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public class Basket
            {
            }
            """).RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00005");
        result.ShouldNotHaveGeneratedFor("Basket");
    }

    [Fact]
    public void A_non_partial_record_entity_reports_both_DDD00002_and_DDD00005()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [Entity<ThingId>]
            public record Widget;
            """).RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00002", "DDD00005");
        result.ShouldNotHaveGeneratedFor("Widget");
    }

    [Fact]
    public void A_settable_partial_collection_property_reports_DDD00020_and_is_not_implemented()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public partial class Basket
            {
                public partial System.Collections.Generic.IReadOnlyList<int> Lines { get; set; }
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00020", at: "Lines");
        diagnostic.GetMessage().Should().Contain("_lines", "the message names the backing field the author should use instead");

        // The aggregate itself is still generated (the base class is what makes the rest of the file
        // compile) but the property is deliberately left unimplemented.
        result.ShouldHaveGenerated(Hint.Of("Sample.Basket"));
        result.ShouldNotContain(Hint.Of("Sample.Basket"), "Lines");
        result.ShouldNotContain(Hint.Of("Sample.Basket"), "_lines");

        // And the compiler then refuses the unimplemented partial property, so the mistake cannot ship.
        result.CompilationErrors.Should().NotBeEmpty("an unimplemented partial property is a compiler error");
    }

    [Fact]
    public void A_get_only_partial_collection_property_reports_nothing()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public partial class Basket
            {
                public partial System.Collections.Generic.IReadOnlyList<int> Lines { get; }
            }
            """).RunCore();

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
    }

    [Fact]
    public void A_partial_property_over_a_type_the_generator_does_not_back_is_left_alone()
    {
        // Not one of the four supported read-only interfaces: the generator must neither implement it
        // nor complain about it - it is simply none of its business.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public partial class Basket
            {
                public partial System.Collections.Generic.List<int> Lines { get; }
            }

            public partial class Basket
            {
                public partial System.Collections.Generic.List<int> Lines => new();
            }
            """).RunCore();

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldNotContain(Hint.Of("Sample.Basket"), "Lines");
        result.ShouldCompile();
    }

    [Fact]
    public void An_aggregate_that_names_a_base_class_of_its_own_does_not_compile()
    {
        // Pins down what docs/entities-and-aggregates.md tells authors: the generator writes the base
        // class itself and does not look for one the author declared, so the two parts disagree.
        // If the generator ever learns to respect an author's base, this test and that paragraph go.
        var result = GeneratorTestHost.Create(Preamble +
            """
            public abstract class AuditedRoot : DDDToolkit.BaseTypes.AggregateRoot<ThingId>
            {
                protected AuditedRoot() { }
            }

            [AggregateRoot<ThingId>]
            public partial class Basket : AuditedRoot
            {
            }
            """).RunCore();

        result.CompilationErrors.Select(static error => error.Id).Should().Contain("CS0263");
    }
}
