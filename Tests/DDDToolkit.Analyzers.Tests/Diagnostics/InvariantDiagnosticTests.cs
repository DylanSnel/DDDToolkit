namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// The four rules about <c>IInvariant&lt;T&gt;</c> implementations: DDD00024 (declared outside an entity,
/// so nothing will ever run it), DDD00025 (nested inside a type it is not about), DDD00026 (two rules of
/// one entity answering to one code) and DDD00027 (nothing the generator can create).
/// <para>
/// Every rule here exists because the alternative is silence: a rule that compiles, reads well, passes
/// its own unit tests and never runs. The near misses matter as much as the probes - a rule that fires
/// on a correct invariant would teach authors to stop writing them.
/// </para>
/// </summary>
public class InvariantDiagnosticTests
{
    private const string Preamble =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Invariants;

        namespace Sample;

        [EntityId<System.Guid>]
        public readonly partial record struct ThingId;


        """;

    private static GeneratorRunOutcome Run(string source)
        => GeneratorTestHost.Create(Preamble + source)
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .RunCore();

    /// <summary>An aggregate to hang rules off, with the body the test supplies.</summary>
    private static string Basket(string body) =>
        $$"""
        [AggregateRoot<ThingId>]
        public partial class Basket
        {
            public Basket(ThingId id) : base(id) { }

        {{body}}
        }
        """;

    // ------------------------------------------------------------------ DDD00024

    [Fact]
    public void A_rule_declared_outside_an_entity_reports_DDD00024()
    {
        var result = Run(
            Basket("") +
            """


            public sealed class MustNotBeEmpty : IInvariant<Basket>
            {
                public string Code => "basket.empty";

                public InvariantFailure? Check(Basket entity) => null;
            }
            """);

        var diagnostic = result.ShouldHaveDiagnostic("DDD00024", at: "MustNotBeEmpty");
        diagnostic.GetMessage().Should().Contain("Basket", "the message names where the rule belongs");
        result.ShouldNotContain(Hint.Of("Sample.Basket"), "MustNotBeEmpty", "a rule outside the entity is not discovered");
    }

    [Fact]
    public void A_rule_nested_in_a_type_that_is_not_an_entity_reports_DDD00024()
    {
        var result = Run(
            Basket("") +
            """


            public static class BasketRules
            {
                public sealed class MustNotBeEmpty : IInvariant<Basket>
                {
                    public string Code => "basket.empty";

                    public InvariantFailure? Check(Basket entity) => null;
                }
            }
            """);

        result.ShouldHaveDiagnostic("DDD00024", at: "MustNotBeEmpty");
    }

    [Fact]
    public void A_rule_nested_in_the_entity_it_is_about_reports_nothing()
    {
        var result = Run(Basket(
            """
                public sealed class MustNotBeEmpty : IInvariant<Basket>
                {
                    public string Code => "basket.empty";

                    public InvariantFailure? Check(Basket entity) => null;
                }
            """));

        result.ShouldNotHaveDiagnostic("DDD00024");
        result.ShouldCompile();
    }

    [Fact]
    public void An_abstract_base_for_rules_reports_nothing_wherever_it_lives()
    {
        // A shared base is not a rule that was meant to run on its own, so complaining about where it
        // lives would be an analyzer arguing with a base class.
        var result = Run(
            Basket(
                """
                    public sealed class MustNotBeEmpty : BasketRule
                    {
                        public override string Code => "basket.empty";

                        public override InvariantFailure? Check(Basket entity) => null;
                    }
                """) +
            """


            public abstract class BasketRule : IInvariant<Basket>
            {
                public abstract string Code { get; }

                public abstract InvariantFailure? Check(Basket entity);
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00024");
        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample.Basket"), "new global::Sample.Basket.MustNotBeEmpty(),", "the rule itself is still found");
    }

    // ------------------------------------------------------------------ DDD00025

    [Fact]
    public void A_rule_about_another_type_reports_DDD00025()
    {
        var result = Run(
            Basket(
                """
                    public sealed class MustCostSomething : IInvariant<Line>
                    {
                        public string Code => "line.price";

                        public InvariantFailure? Check(Line entity) => null;
                    }
                """) +
            """


            [Entity<ThingId>]
            public partial class Line
            {
                public Line(ThingId id) : base(id) { }
            }
            """);

        var diagnostic = result.ShouldHaveDiagnostic("DDD00025", at: "MustCostSomething");
        diagnostic.GetMessage().Should().Contain("Basket").And.Contain("Line");

        result.ShouldNotHaveDiagnostic("DDD00024");
        result.ShouldNotContain(Hint.Of("Sample.Basket"), "MustCostSomething", "the basket would never have run it");
        result.ShouldNotContain(Hint.Of("Sample.Line"), "MustCostSomething", "and the line cannot see it either");
    }

    [Fact]
    public void A_rule_about_the_entity_that_holds_it_reports_nothing()
    {
        var result = Run(Basket(
            """
                public sealed class MustNotBeEmpty : IInvariant<Basket>
                {
                    public string Code => "basket.empty";

                    public InvariantFailure? Check(Basket entity) => null;
                }
            """));

        result.ShouldNotHaveDiagnostic("DDD00025");
        result.ShouldCompile();
    }

    // ------------------------------------------------------------------ DDD00026

    [Fact]
    public void Two_rules_of_one_entity_with_one_code_report_DDD00026()
    {
        var result = Run(Basket(
            """
                public sealed class MustNotBeEmpty : IInvariant<Basket>
                {
                    public string Code => "basket.broken";

                    public InvariantFailure? Check(Basket entity) => null;
                }

                public sealed class MustNotBeHuge : IInvariant<Basket>
                {
                    public string Code => "basket.broken";

                    public InvariantFailure? Check(Basket entity) => null;
                }
            """));

        var diagnostic = result.ShouldHaveDiagnostic("DDD00026", at: "MustNotBeHuge");
        diagnostic.GetMessage().Should().Contain("basket.broken").And.Contain("MustNotBeEmpty");

        result.Count("DDD00026").Should().Be(1, "the rule that claimed the code first is not at fault");
        result.ShouldCompile();
    }

    [Theory]
    [InlineData("""public string Code => "basket.broken";""")]
    [InlineData("""public string Code { get; } = "basket.broken";""")]
    [InlineData("""public string Code => Shared;""")]
    public void The_code_is_read_from_every_shape_a_constant_can_take(string code)
    {
        var result = Run(Basket(
            $$"""
                private const string Shared = "basket.broken";

                public sealed class MustNotBeEmpty : IInvariant<Basket>
                {
                    {{code}}

                    public InvariantFailure? Check(Basket entity) => null;
                }

                public sealed class MustNotBeHuge : IInvariant<Basket>
                {
                    public string Code => "basket.broken";

                    public InvariantFailure? Check(Basket entity) => null;
                }
            """));

        result.ShouldHaveDiagnostic("DDD00026", at: "MustNotBeHuge");
    }

    [Fact]
    public void Two_rules_with_codes_of_their_own_report_nothing()
    {
        var result = Run(Basket(
            """
                public sealed class MustNotBeEmpty : IInvariant<Basket>
                {
                    public string Code => "basket.empty";

                    public InvariantFailure? Check(Basket entity) => null;
                }

                public sealed class MustNotBeHuge : IInvariant<Basket>
                {
                    public string Code => "basket.huge";

                    public InvariantFailure? Check(Basket entity) => null;
                }
            """));

        result.ShouldNotHaveDiagnostic("DDD00026");
        result.ShouldCompile();
    }

    [Fact]
    public void A_code_that_is_not_a_constant_is_left_alone()
    {
        // Built at run time, so the analyzer cannot know whether the two are the same. Guessing would
        // report two rules as sharing a code they may well not share.
        var result = Run(Basket(
            """
                public sealed class MustNotBeEmpty : IInvariant<Basket>
                {
                    public string Code => Build();

                    public InvariantFailure? Check(Basket entity) => null;

                    private static string Build() => "basket.broken";
                }

                public sealed class MustNotBeHuge : IInvariant<Basket>
                {
                    public string Code => "basket.broken";

                    public InvariantFailure? Check(Basket entity) => null;
                }
            """));

        result.ShouldNotHaveDiagnostic("DDD00026");
        result.ShouldCompile();
    }

    [Fact]
    public void One_code_on_two_different_entities_is_not_a_clash()
    {
        var result = Run(
            Basket(
                """
                    public sealed class MustNotBeEmpty : IInvariant<Basket>
                    {
                        public string Code => "empty";

                        public InvariantFailure? Check(Basket entity) => null;
                    }
                """) +
            """


            [Entity<ThingId>]
            public partial class Line
            {
                public Line(ThingId id) : base(id) { }

                public sealed class MustNotBeEmpty : IInvariant<Line>
                {
                    public string Code => "empty";

                    public InvariantFailure? Check(Line entity) => null;
                }
            }
            """);

        // A code is unique within the entity that states it, not across the solution.
        result.ShouldNotHaveDiagnostic("DDD00026");
        result.ShouldCompile();
    }

    // ------------------------------------------------------------------ DDD00027

    [Fact]
    public void A_rule_the_generator_cannot_create_reports_DDD00027_and_is_left_out()
    {
        var result = Run(Basket(
            """
                public sealed class MustNotBeEmpty : IInvariant<Basket>
                {
                    public MustNotBeEmpty(int limit) => Limit = limit;

                    public int Limit { get; }

                    public string Code => "basket.empty";

                    public InvariantFailure? Check(Basket entity) => null;
                }
            """));

        var diagnostic = result.ShouldHaveDiagnostic("DDD00027", at: "MustNotBeEmpty");
        diagnostic.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, "the generated code would not compile");
        diagnostic.GetMessage().Should().Contain("Basket");

        // Left out rather than emitted as code that cannot compile: the entity itself is still
        // generated, so the author sees this one error instead of a page of them.
        result.ShouldNotContain(Hint.Of("Sample.Basket"), "__invariants");
        result.ShouldHaveGenerated(Hint.Of("Sample.Basket"));
        result.CompilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void A_rule_whose_constructor_the_entity_cannot_reach_reports_DDD00027()
    {
        var result = Run(Basket(
            """
                public sealed class MustNotBeEmpty : IInvariant<Basket>
                {
                    private MustNotBeEmpty()
                    {
                    }

                    public string Code => "basket.empty";

                    public InvariantFailure? Check(Basket entity) => null;
                }
            """));

        result.ShouldHaveDiagnostic("DDD00027", at: "MustNotBeEmpty");
    }

    [Fact]
    public void A_rule_with_a_constructor_it_can_reach_reports_nothing()
    {
        var result = Run(Basket(
            """
                public sealed class MustNotBeEmpty : IInvariant<Basket>
                {
                    public MustNotBeEmpty()
                    {
                    }

                    public string Code => "basket.empty";

                    public InvariantFailure? Check(Basket entity) => null;
                }

                private sealed class MustNotBeHuge : IInvariant<Basket>
                {
                    public string Code => "basket.huge";

                    public InvariantFailure? Check(Basket entity) => null;
                }
            """));

        result.ShouldNotHaveDiagnostic("DDD00027");
        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample.Basket"), "new global::Sample.Basket.MustNotBeEmpty(),");
        result.ShouldContain(Hint.Of("Sample.Basket"), "new global::Sample.Basket.MustNotBeHuge(),");
    }

    // ------------------------------------------------------------------ nothing fires on code without rules

    [Fact]
    public void An_entity_with_no_rules_at_all_reports_none_of_the_four()
    {
        var result = Run(Basket(""));

        result.ShouldNotHaveDiagnostic("DDD00024");
        result.ShouldNotHaveDiagnostic("DDD00025");
        result.ShouldNotHaveDiagnostic("DDD00026");
        result.ShouldNotHaveDiagnostic("DDD00027");
        result.ShouldCompile();
    }
}
