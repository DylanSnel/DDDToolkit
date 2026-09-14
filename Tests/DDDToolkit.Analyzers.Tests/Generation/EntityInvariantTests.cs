using DDDToolkit.Exceptions;
using DDDToolkit.Interfaces;
using DDDToolkit.Invariants;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// What an entity's own invariants turn into: an array of the <c>IInvariant&lt;T&gt;</c> rules nested
/// inside it, one routine that collects what is broken, and the stages built on that routine -
/// <c>GetInvariantViolations()</c>, which answers, and <c>EnsureInvariants()</c>, which insists.
/// <para>
/// The tests run the generated code rather than only reading it, because the claim worth pinning is
/// behavioural: the same set of rules decides both stages, the seam's exception survives as the inner
/// exception of the one thrown, and an entity that states nothing pays for nothing.
/// </para>
/// <para>
/// The entities here hold no child entities, so their aggregate answer and their own answer are the
/// same one. What happens when they differ is in <see cref="EntityChildInvariantTests"/>.
/// </para>
/// </summary>
public class EntityInvariantTests
{
    private const string Preamble =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Invariants;
        using System.Collections.Generic;

        namespace Sample;

        [EntityId<System.Guid>]
        public readonly partial record struct ThingId;


        """;

    private static GeneratorTestHost Basket(string body, string attribute = "AggregateRoot<ThingId>")
        => GeneratorTestHost.Create(Preamble +
            $$"""
            [{{attribute}}]
            public partial class Basket
            {
                public Basket(ThingId id) : base(id) { }

                private int _count;

                public void Fill(int count) => _count = count;

            {{body}}
            }
            """);

    /// <summary>A rule that reads the entity's private field, which is what nesting is for.</summary>
    private const string MustNotBeEmpty =
        """
            public sealed class MustNotBeEmpty : IInvariant<Basket>
            {
                public string Code => "basket.empty";

                public string? Check(Basket entity) => entity._count > 0 ? null : "A basket must hold something.";
            }
        """;

    private const string MustNotBeHuge =
        """
            public sealed class MustNotBeHuge : IInvariant<Basket>
            {
                public string Code => "basket.huge";

                public string? Check(Basket entity) => entity._count <= 10 ? null : "A basket may hold at most ten things.";
            }
        """;

    /// <summary>An entity with its rules, ready to be asked and to be told.</summary>
    private static IHasInvariants Fill(GeneratorRunOutcome result, int count)
    {
        var emitted = result.Emit();
        var basket = emitted.New("Sample.Basket", emitted.CallStatic("Sample.ThingId", "CreateUnique")!);
        emitted.Call(basket, "Fill", count);
        return (IHasInvariants)basket;
    }

    // ------------------------------------------------------------------ one rule

    [Fact]
    public void One_nested_invariant_becomes_one_entry_in_the_array()
    {
        var result = Basket(MustNotBeEmpty).RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            "Sample.Basket.g.cs",
            "private static readonly global::DDDToolkit.Invariants.IInvariant<global::Sample.Basket>[] __invariants =");
        result.ShouldContain("Sample.Basket.g.cs", "new global::Sample.Basket.MustNotBeEmpty(),");
    }

    [Fact]
    public void A_broken_rule_is_returned_by_the_stage_that_asks()
    {
        var result = Basket(MustNotBeEmpty).RunCore();

        Fill(result, 1).GetInvariantViolations().Should().BeEmpty("the basket holds something");

        var violation = Fill(result, 0).GetInvariantViolations().Should().ContainSingle().Which;

        violation.Code.Should().Be("basket.empty");
        violation.Message.Should().Be("A basket must hold something.");
        violation.EntityType!.FullName.Should().Be("Sample.Basket", "a violation names who reported it");
        violation.EntityId.Should().NotBeNull("and which one of them it was");
    }

    [Fact]
    public void A_broken_rule_is_thrown_by_the_stage_that_insists()
    {
        var result = Basket(MustNotBeEmpty).RunCore();

        Fill(result, 1).Invoking(basket => basket.EnsureInvariants()).Should().NotThrow();

        var exception = Fill(result, 0).Invoking(basket => basket.EnsureInvariants())
            .Should().Throw<InvariantViolationException>().Which;

        exception.AggregateType!.FullName.Should().Be("Sample.Basket");
        exception.AggregateId.Should().NotBeNull("the exception names which basket it was");
        exception.Violations.Should().ContainSingle().Which.Should().Be("A basket must hold something.");
        exception.InnerException.Should().BeNull("no seam threw, so there is nothing to keep");
    }

    [Fact]
    public void The_rule_instance_is_created_once_and_reused()
    {
        var result = Basket(MustNotBeEmpty).RunCore();
        var emitted = result.Emit();

        var field = emitted.Type("Sample.Basket").GetField(
            "__invariants",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        field.IsInitOnly.Should().BeTrue();
        ((Array)field.GetValue(null)!).Length.Should().Be(1);
    }

    // ------------------------------------------------------------------ several rules

    [Fact]
    public void Several_rules_all_run_and_all_report()
    {
        var result = Basket(MustNotBeEmpty + "\n" + MustNotBeHuge).RunCore();

        result.ShouldCompile();
        result.ShouldContain("Sample.Basket.g.cs", "new global::Sample.Basket.MustNotBeEmpty(),");
        result.ShouldContain("Sample.Basket.g.cs", "new global::Sample.Basket.MustNotBeHuge(),");

        Fill(result, 5).GetInvariantViolations().Should().BeEmpty();
        Fill(result, 0).GetInvariantViolations().Should().ContainSingle()
            .Which.Code.Should().Be("basket.empty");
        Fill(result, 99).GetInvariantViolations().Should().ContainSingle()
            .Which.Code.Should().Be("basket.huge");
    }

    [Fact]
    public void One_check_reports_every_rule_that_is_broken_at_once()
    {
        // Both rules are about the same field, and both are broken by the same value: nothing stops
        // after the first failure.
        var result = Basket(
            """
                public sealed class MustBePositive : IInvariant<Basket>
                {
                    public string Code => "basket.positive";

                    public string? Check(Basket entity) => entity._count > 0 ? null : "A basket cannot hold less than nothing.";
                }

                public sealed class MustBeStarted : IInvariant<Basket>
                {
                    public string Code => "basket.started";

                    public string? Check(Basket entity) => entity._count != 0 ? null : "A basket must be started before it is saved.";
                }
            """).RunCore();

        Fill(result, 0).GetInvariantViolations().Select(violation => violation.Code)
            .Should().Equal("basket.positive", "basket.started");

        var exception = Fill(result, 0).Invoking(basket => basket.EnsureInvariants())
            .Should().Throw<InvariantViolationException>().Which;

        exception.Violations.Should().HaveCount(2);
        exception.Message.Should().Contain("broke 2 invariants");
    }

    // ------------------------------------------------------------------ a rule and a seam together

    [Fact]
    public void A_rule_and_a_seam_are_reported_by_one_call()
    {
        var result = Basket(
            MustNotBeEmpty +
            """

                partial void CheckInvariants()
                {
                    if (_count < 2) throw InvariantViolation("A basket must hold at least two things.");
                }
            """).RunCore();

        result.ShouldCompile();

        Fill(result, 2).GetInvariantViolations().Should().BeEmpty("the basket is neither empty nor short");

        var violations = Fill(result, 0).GetInvariantViolations();

        violations.Should().HaveCount(2, "the rules and the seam are one question with one answer");
        violations[0].Code.Should().Be("basket.empty");
        violations[1].Code.Should().Be(InvariantViolation.SeamCode, "the seam has nowhere to put a code of its own");
        violations[1].Message.Should().Contain("at least two");
    }

    [Fact]
    public void What_the_seam_threw_is_kept_as_the_inner_exception()
    {
        var result = Basket(
            MustNotBeEmpty +
            """

                partial void CheckInvariants()
                {
                    throw InvariantViolation("A basket is never good enough.");
                }
            """).RunCore();

        var exception = Fill(result, 1).Invoking(basket => basket.EnsureInvariants())
            .Should().Throw<InvariantViolationException>().Which;

        exception.Violations.Should().ContainSingle().Which.Should().Contain("never good enough");
        exception.InnerException.Should().BeOfType<InvariantViolationException>("no stack trace is lost")
            .Which.Message.Should().Contain("never good enough");
    }

    [Fact]
    public void The_two_stages_agree_about_a_seam_that_reports_several_rules_at_once()
    {
        var result = Basket(
            MustNotBeEmpty +
            """

                partial void CheckInvariants()
                {
                    throw InvariantViolation(new[] { "The first thing is wrong.", "So is the second." });
                }
            """).RunCore();

        var violations = Fill(result, 1).GetInvariantViolations();

        violations.Should().HaveCount(2, "the seam's own list is folded in, not flattened into one message");
        violations.Should().OnlyContain(violation => violation.Code == InvariantViolation.SeamCode);

        Fill(result, 1).Invoking(basket => basket.EnsureInvariants())
            .Should().Throw<InvariantViolationException>()
            .Which.Violations.Should().HaveCount(2);
    }

    [Fact]
    public void A_seam_that_throws_a_message_of_its_own_still_becomes_a_violation()
    {
        // Thrown directly rather than through the InvariantViolation(...) helper, so the exception
        // carries no list of rules and the message is all there is.
        var result = Basket(
            MustNotBeEmpty +
            """

                partial void CheckInvariants()
                {
                    throw new DDDToolkit.Exceptions.InvariantViolationException("Something is off about this basket.");
                }
            """).RunCore();

        var violation = Fill(result, 1).GetInvariantViolations().Should().ContainSingle().Which;

        violation.Code.Should().Be(InvariantViolation.SeamCode);
        violation.Message.Should().Be("Something is off about this basket.");
        violation.EntityType!.FullName.Should().Be("Sample.Basket", "what the seam reports is reported by the entity");
    }

    // ------------------------------------------------------------------ neither

    [Fact]
    public void An_entity_with_no_rules_keeps_the_seam_only_shape()
    {
        var result = Basket("").RunCore();

        result.ShouldCompile();
        result.ShouldNotContain("Sample.Basket.g.cs", "__invariants", "an empty array is still an array to allocate");
        result.ShouldNotContain(
            "Sample.Basket.g.cs",
            "ThrowInvariantViolations",
            "with nothing to collect there is nothing to throw, and both Ensure methods stay the bare call the compiler can erase");
        result.ShouldNotContain(
            "Sample.Basket.g.cs",
            "CollectChildInvariantViolations",
            "this basket holds no child entities, so there is no walk to write");
        result.ShouldContain("Sample.Basket.g.cs", "public override void EnsureInvariants()");
        result.ShouldContain("Sample.Basket.g.cs", "public override void EnsureOwnInvariants()");
    }

    [Fact]
    public void An_entity_with_no_rules_and_no_seam_is_consistent_and_says_so()
    {
        var result = Basket("").RunCore();

        var basket = Fill(result, 0);

        basket.GetInvariantViolations().Should().BeEmpty();
        basket.Invoking(subject => subject.EnsureInvariants()).Should().NotThrow();
    }

    [Fact]
    public void An_entity_with_only_a_seam_reports_it_from_both_stages()
    {
        // The stage that asks must never pass what the stage that saves refuses, rules or no rules.
        var result = Basket(
            """
                partial void CheckInvariants()
                {
                    if (_count == 0) throw InvariantViolation("A basket must hold something.");
                }
            """).RunCore();

        Fill(result, 1).GetInvariantViolations().Should().BeEmpty();

        Fill(result, 0).GetInvariantViolations().Should().ContainSingle()
            .Which.Code.Should().Be(InvariantViolation.SeamCode);

        Fill(result, 0).Invoking(basket => basket.EnsureInvariants())
            .Should().Throw<InvariantViolationException>();
    }

    // ------------------------------------------------------------------ child entities

    [Fact]
    public void A_child_entity_runs_its_own_rules()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [Entity<ThingId>]
            public partial class Line
            {
                public Line(ThingId id, decimal price) : base(id) => _price = price;

                private readonly decimal _price;

                public sealed class MustCostSomething : IInvariant<Line>
                {
                    public string Code => "line.price";

                    public string? Check(Line entity) => entity._price >= 0m ? null : "A line cannot cost less than nothing.";
                }
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldContain("Sample.Line.g.cs", "new global::Sample.Line.MustCostSomething(),");

        var emitted = result.Emit();
        var id = emitted.CallStatic("Sample.ThingId", "CreateUnique")!;

        ((IHasInvariants)emitted.New("Sample.Line", id, 1m)).GetInvariantViolations().Should().BeEmpty();
        ((IHasInvariants)emitted.New("Sample.Line", id, -1m)).GetInvariantViolations()
            .Should().ContainSingle().Which.Code.Should().Be("line.price");
    }

    [Fact]
    public void A_rule_may_be_private_and_still_be_found()
    {
        // Nesting is what makes the rule able to read _count; there is no reason for it to be visible
        // outside the entity, and the generated code is written inside the entity too.
        var result = Basket(
            """
                private sealed class MustNotBeEmpty : IInvariant<Basket>
                {
                    public string Code => "basket.empty";

                    public string? Check(Basket entity) => entity._count > 0 ? null : "A basket must hold something.";
                }
            """).RunCore();

        result.ShouldCompile();
        Fill(result, 0).GetInvariantViolations().Should().ContainSingle();
    }

    [Fact]
    public void A_rule_on_an_aggregate_and_a_rule_on_its_child_stay_apart()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public partial class Order
            {
                public Order(ThingId id) : base(id) { }

                public sealed class MustHaveLines : IInvariant<Order>
                {
                    public string Code => "order.lines";

                    public string? Check(Order entity) => "An order must have at least one line.";
                }
            }

            [Entity<ThingId>]
            public partial class Line
            {
                public Line(ThingId id) : base(id) { }

                public sealed class MustCostSomething : IInvariant<Line>
                {
                    public string Code => "line.price";

                    public string? Check(Line entity) => "A line cannot cost less than nothing.";
                }
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldContain("Sample.Order.g.cs", "new global::Sample.Order.MustHaveLines(),");
        result.ShouldNotContain("Sample.Order.g.cs", "MustCostSomething");
        result.ShouldContain("Sample.Line.g.cs", "new global::Sample.Line.MustCostSomething(),");
        result.ShouldNotContain("Sample.Line.g.cs", "MustHaveLines");
    }

    // ------------------------------------------------------------------ the shape of the generated code

    [Fact]
    public void Both_stages_are_built_on_the_one_routine_that_collects()
    {
        var result = Basket(MustNotBeEmpty).RunCore();

        result.ShouldContain("Sample.Basket.g.cs", "CollectInvariantViolations(ref violations, out _);");
        result.ShouldContain("Sample.Basket.g.cs", "CollectInvariantViolations(ref violations, out var seamFailure);");
    }

    [Fact]
    public void An_entity_that_holds_no_children_answers_the_two_questions_with_one_method()
    {
        // Nothing inside it means the aggregate's answer and this object's own answer cannot differ,
        // and one method having the answer is what stops them drifting apart.
        var result = Basket(MustNotBeEmpty).RunCore();

        result.ShouldContain("Sample.Basket.g.cs", "GetInvariantViolations() => GetOwnInvariantViolations();");
        result.ShouldContain("Sample.Basket.g.cs", "public override void EnsureInvariants() => EnsureOwnInvariants();");
    }

    [Fact]
    public void The_generated_code_names_everything_it_uses_in_full()
    {
        // Generated code carries no using directives, so anything it names unqualified would only
        // compile in a file that happened to have the right ones.
        var result = Basket(MustNotBeEmpty).RunCore();

        result.ShouldCompile();
        result.ShouldNotContain("Sample.Basket.g.cs", "using ");
        result.ShouldNotContain("Sample.Basket.g.cs", "System.Linq.Enumerable");
    }

    [Fact]
    public void The_happy_path_allocates_no_list()
    {
        // Not observable through the public API, so it is read off the generated source: the list is
        // created where the first failure is added, and nowhere else.
        var result = Basket(MustNotBeEmpty).RunCore();
        var source = result.Source("Sample.Basket.g.cs");

        source.Should().Contain("? violations = null;");
        source.Should().Contain("violations ??= new global::System.Collections.Generic.List<global::DDDToolkit.Invariants.InvariantViolation>();");
        source.Should().Contain("return global::System.Array.Empty<global::DDDToolkit.Invariants.InvariantViolation>();");
    }
}
