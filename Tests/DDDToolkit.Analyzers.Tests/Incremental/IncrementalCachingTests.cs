using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.Tests.Incremental;

/// <summary>
/// An incremental generator earns its name by <em>not</em> re-running when nothing it cares about changed.
/// Every edit in an IDE re-runs the pipeline, so a generator whose model does not compare equal across
/// runs regenerates every type on every keystroke.
/// <para>
/// Each test here runs a generator, applies an edit that has nothing to do with the DDDToolkit types, runs
/// the same driver again and checks that every source-output step reports
/// <see cref="IncrementalStepRunReason.Cached"/> or <see cref="IncrementalStepRunReason.Unchanged"/>. The
/// last test is the control: a change that <em>does</em> matter must show up as New or Modified, otherwise
/// the others would pass on a pipeline that reports nothing at all.
/// </para>
/// </summary>
public class IncrementalCachingTests
{
    /// <summary>
    /// One of everything, including an aggregate whose id is generated from its own declaration: that
    /// id reaches the generators through a provider that collects the whole compilation, which is the
    /// shape most likely to lose its caching.
    /// </summary>
    private const string Source =
        """
        using DDDToolkit.Abstractions.Attributes;
        using System;
        using System.Collections.Generic;

        namespace Sample;

        [EntityId<Guid>("PRD")]
        public readonly partial record struct ProductId;

        [EntityId<Guid>("USR")]
        public partial record UserId;

        [SingleValueObject<string>(ColumnLength: 255)]
        public partial record EmailAddress;

        [ValueObject]
        public partial record PersonName
        {
            public string FirstName { get; protected init; } = "";

            [DontCompare]
            public string? MiddleNames { get; protected init; }
        }

        [Entity<ProductId>]
        public partial class Line
        {
            public Line(ProductId id) : base(id) { }
        }

        [AggregateRoot<UserId>]
        public partial class Basket
        {
            public Basket(UserId id) : base(id) { }

            public partial IReadOnlyList<Line> Lines { get; }
        }

        [AggregateRoot<Guid>("INV")]
        public partial class Invoice
        {
            public Invoice(InvoiceId id) : base(id) { }
        }
        """;

    // ------------------------------------------------------------------ one test per generator family

    [Fact]
    public void The_core_generators_cache_their_output_across_an_unrelated_comment()
    {
        var first = Host().RunCore();
        first.ShouldCompile();

        var second = first.RunAgain(AppendComment);

        AssertNothingRegenerated(first, second);
    }

    [Fact]
    public void The_core_generators_cache_their_output_across_an_unrelated_new_file()
    {
        var first = Host().RunCore();
        first.ShouldCompile();

        var second = first.RunAgain(AddUnrelatedFile);

        AssertNothingRegenerated(first, second);
    }

    [Fact]
    public void The_entity_framework_generators_cache_their_output()
    {
        var first = Host().WithEntityFramework().WithModule("Sales")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());
        first.ShouldCompile();

        var second = first.RunAgain(AppendComment);

        AssertNothingRegenerated(first, second);
    }

    [Fact]
    public void The_fluent_validation_generator_caches_its_output()
    {
        var first = Host().WithFluentValidation()
            .RunCoreAnd(GeneratorTestHost.FluentValidationGenerators());
        first.ShouldCompile();

        var second = first.RunAgain(AppendComment);

        AssertNothingRegenerated(first, second);
    }

    [Fact]
    public void The_hotchocolate_generator_caches_its_output()
    {
        var first = Host().WithHotChocolate().WithModule("Sales")
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());
        first.ShouldCompile();

        var second = first.RunAgain(AppendComment);

        AssertNothingRegenerated(first, second);
    }

    [Fact]
    public void Diagnostics_are_cached_too_so_a_broken_type_is_not_re_reported_from_scratch()
    {
        // Diagnostics are carried through the pipeline as plain data rather than reported from the
        // transform, which is what lets a run with errors stay cacheable as well.
        var first = GeneratorTestHost.Create(
            """
            using DDDToolkit.Abstractions.Attributes;

            namespace Sample;

            [ValueObject]
            public partial class NotARecord
            {
            }
            """).RunCore();

        first.ShouldHaveDiagnostic("DDD00001", at: "NotARecord");

        var second = first.RunAgain(AppendComment);

        second.ShouldHaveDiagnostic("DDD00001", at: "NotARecord");
        AssertNothingRegenerated(first, second);
    }

    // ------------------------------------------------------------------ the control

    [Fact]
    public void A_change_that_does_matter_is_not_reported_as_cached()
    {
        // Without this the tests above could pass over a pipeline that never reports anything.
        var first = Host().RunCore();
        first.ShouldCompile();

        var second = first.RunAgain(static (compilation, parseOptions) => compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(
                SourceText.From(
                    """
                    using DDDToolkit.Abstractions.Attributes;
                    using System;

                    namespace Sample;

                    [EntityId<Guid>("NEW")]
                    public readonly partial record struct BrandNewId;
                    """,
                    Encoding.UTF8),
                parseOptions,
                "BrandNewId.cs")));

        second.ShouldCompile();
        second.OutputStepReasons().Select(step => step.Reason)
            .Should().Contain(IncrementalStepRunReason.New, "a new DDDToolkit type is new work");
        second.GeneratedSources.Select(source => source.HintName).Should().Contain(Hint.Of("Sample.BrandNewId"));
    }

    [Fact]
    public void Editing_a_type_regenerates_only_that_type()
    {
        var first = Host().RunCore();
        first.ShouldCompile();

        var second = first.RunAgain(static (compilation, parseOptions) =>
        {
            var tree = compilation.SyntaxTrees.First();
            var edited = tree.GetText().ToString().Replace("""[EntityId<Guid>("PRD")]""", """[EntityId<Guid>("PRODUCT")]""", StringComparison.Ordinal);
            return compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(SourceText.From(edited, Encoding.UTF8), parseOptions, tree.FilePath));
        });

        second.ShouldCompile();
        second.ShouldContain(Hint.Of("Sample.ProductId"), "IdPrefix = \"PRODUCT\"");

        // Every other generated file is byte-for-byte what it was.
        foreach (var source in second.GeneratedSources.Where(source => source.HintName != Hint.Of("Sample.ProductId")))
        {
            source.SourceText.ToString().Should().Be(
                first.GeneratedSources.Single(other => other.HintName == source.HintName).SourceText.ToString(),
                "nothing but ProductId changed");
        }
    }

    // ------------------------------------------------------------------ helpers

    private static GeneratorTestHost Host() => GeneratorTestHost.Create(Source);

    private static void AssertNothingRegenerated(GeneratorRunOutcome first, GeneratorRunOutcome second)
    {
        var reasons = second.OutputStepReasons();

        reasons.Should().NotBeEmpty("the driver must be tracking steps, or this assertion means nothing");
        reasons.Should().OnlyContain(
            step => step.Reason == IncrementalStepRunReason.Cached || step.Reason == IncrementalStepRunReason.Unchanged,
            "an unrelated edit must not make the generators redo their work");

        second.GeneratedSources.Select(source => source.HintName)
            .Should().BeEquivalentTo(first.GeneratedSources.Select(source => source.HintName));
    }

    /// <summary>An edit at the very end of the file: no DDDToolkit type moves, so nothing about them changed.</summary>
    private static Compilation AppendComment(Compilation compilation, CSharpParseOptions parseOptions)
    {
        var tree = compilation.SyntaxTrees.First();
        var text = tree.GetText().ToString() + "\n\n// A comment that concerns nobody.\n";
        return compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), parseOptions, tree.FilePath));
    }

    /// <summary>A whole new file holding a type no DDDToolkit generator cares about.</summary>
    private static Compilation AddUnrelatedFile(Compilation compilation, CSharpParseOptions parseOptions)
        => compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            SourceText.From(
                """
                namespace Unrelated;

                public sealed class Bystander
                {
                    public int Value { get; set; }
                }
                """,
                Encoding.UTF8),
            parseOptions,
            "Bystander.cs"));
}
