using System.Collections;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// What <c>[Entity&lt;TId&gt;]</c> and <c>[AggregateRoot&lt;TId&gt;]</c> produce: the base class, a protected
/// parameterless constructor for persistence frameworks, and an implementation of every get-only partial
/// collection property — a private backing field plus a read-only view over it. The aggregate can mutate
/// the field; everybody else gets a view that refuses to be changed.
/// </summary>
public class EntityCollectionTests
{
    private const string Preamble =
        """
        using DDDToolkit.Abstractions.Attributes;
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

            {{body}}
            }
            """);

    // ------------------------------------------------------------------ base types

    [Fact]
    public void An_aggregate_root_derives_from_AggregateRoot_and_an_entity_from_Entity()
    {
        var root = Basket("").RunCore();
        var entity = Basket("", attribute: "Entity<ThingId>").RunCore();

        root.ShouldContain("Sample.Basket.g.cs", "partial class Basket : global::DDDToolkit.BaseTypes.AggregateRoot<global::Sample.ThingId>");
        entity.ShouldContain("Sample.Basket.g.cs", "partial class Basket : global::DDDToolkit.BaseTypes.Entity<global::Sample.ThingId>");

        var rootType = root.Emit().Type("Sample.Basket");
        typeof(IAggregateRoot).IsAssignableFrom(rootType).Should().BeTrue();
        typeof(IAggregateRoot).IsAssignableFrom(entity.Emit().Type("Sample.Basket")).Should().BeFalse("a child entity is not a consistency boundary");
    }

    [Fact]
    public void An_aggregate_with_no_collections_still_gets_its_base_and_a_parameterless_constructor()
    {
        var result = Basket("").RunCore();

        result.ShouldCompile();
        result.ShouldContain("Sample.Basket.g.cs", "protected Basket()");

        var constructor = result.Emit().Type("Sample.Basket")
            .GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, System.Type.EmptyTypes);

        constructor.Should().NotBeNull();
        constructor!.IsFamily.Should().BeTrue();
    }

    // ------------------------------------------------------------------ the four supported interfaces

    [Theory]
    [InlineData("IReadOnlyList<int>", "List")]
    [InlineData("IReadOnlyCollection<int>", "List")]
    [InlineData("IEnumerable<int>", "List")]
    public void A_list_backed_collection_property_gets_a_field_and_an_AsReadOnly_view(string declared, string backing)
    {
        var result = Basket($"    public partial {declared} Lines {{ get; }}").RunCore();

        result.ShouldCompile();
        result.ShouldContain("Sample.Basket.g.cs", $"private readonly global::System.Collections.Generic.{backing}<int> _lines = new();");
        result.ShouldContain("Sample.Basket.g.cs", "_lines.AsReadOnly();");
    }

    [Fact]
    public void A_set_property_is_backed_by_a_HashSet_and_viewed_through_ReadOnlySet()
    {
        var result = Basket("    public partial IReadOnlySet<int> Tags { get; }").RunCore();

        result.ShouldCompile();
        result.ShouldContain("Sample.Basket.g.cs", "private readonly global::System.Collections.Generic.HashSet<int> _tags = new();");
        result.ShouldContain("Sample.Basket.g.cs", "new global::System.Collections.ObjectModel.ReadOnlySet<int>(_tags)");
    }

    [Fact]
    public void The_backing_field_name_is_the_property_name_with_a_leading_underscore_and_lower_case()
    {
        var result = Basket(
            """
                public partial IReadOnlyList<int> Lines { get; }

                public partial IReadOnlyList<int> ABC { get; }
            """).RunCore();

        result.ShouldContain("Sample.Basket.g.cs", "_lines");
        result.ShouldContain("Sample.Basket.g.cs", "_aBC", "only the first character is lowered, the rest of the name is the author's");
        result.ShouldCompile();
    }

    // ------------------------------------------------------------------ the view really is read-only

    [Fact]
    public void The_view_reflects_what_the_aggregate_adds_to_the_field()
    {
        var emitted = Basket(
            """
                public partial IReadOnlyList<int> Lines { get; }

                public void Add(int line) => _lines.Add(line);
            """).RunCore().Emit();

        var basket = emitted.New("Sample.Basket", emitted.CallStatic("Sample.ThingId", "CreateUnique")!);
        emitted.Call(basket, "Add", 1);
        emitted.Call(basket, "Add", 2);

        ((IEnumerable<int>)emitted.Property(basket, "Lines")!).Should().Equal(1, 2);
    }

    [Fact]
    public void The_view_refuses_to_be_changed_from_outside()
    {
        var emitted = Basket("    public partial IReadOnlyList<int> Lines { get; }").RunCore().Emit();
        var basket = emitted.New("Sample.Basket", emitted.CallStatic("Sample.ThingId", "CreateUnique")!);

        var view = emitted.Property(basket, "Lines")!;

        view.Should().BeAssignableTo<IList<int>>("ReadOnlyCollection<T> implements IList<T> — and refuses every mutation");
        var act = () => ((IList<int>)view).Add(1);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void The_set_view_refuses_to_be_changed_from_outside()
    {
        var emitted = Basket(
            """
                public partial IReadOnlySet<int> Tags { get; }

                public void Add(int tag) => _tags.Add(tag);
            """).RunCore().Emit();

        var basket = emitted.New("Sample.Basket", emitted.CallStatic("Sample.ThingId", "CreateUnique")!);
        emitted.Call(basket, "Add", 3);

        var view = (IReadOnlySet<int>)emitted.Property(basket, "Tags")!;

        view.Should().Equal(3);
        var act = () => ((ISet<int>)view).Add(4);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void The_backing_field_is_private_and_readonly()
    {
        var emitted = Basket("    public partial IReadOnlyList<int> Lines { get; }").RunCore().Emit();

        var field = emitted.Type("Sample.Basket")
            .GetField("_lines", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        field.IsPrivate.Should().BeTrue();
        field.IsInitOnly.Should().BeTrue();
    }

    // ------------------------------------------------------------------ modifiers are mirrored

    [Theory]
    [InlineData("public")]
    [InlineData("internal")]
    [InlineData("protected")]
    [InlineData("protected internal")]
    [InlineData("private")]
    public void The_accessibility_of_the_property_is_mirrored(string accessibility)
    {
        var result = Basket($"    {accessibility} partial IReadOnlyList<int> Lines {{ get; }}").RunCore();

        result.ShouldCompile();
        result.ShouldContain("Sample.Basket.g.cs", accessibility + " partial global::System.Collections.Generic.IReadOnlyList<int> Lines =>");
    }

    [Fact]
    public void A_virtual_property_stays_virtual_so_lazy_loading_proxies_keep_working()
    {
        var result = Basket("    public virtual partial IReadOnlyList<int> Lines { get; }").RunCore();

        result.ShouldCompile();
        result.ShouldContain("Sample.Basket.g.cs", "public virtual partial global::System.Collections.Generic.IReadOnlyList<int> Lines =>");

        var property = result.Emit().Type("Sample.Basket").GetProperty("Lines")!;
        property.GetMethod!.IsVirtual.Should().BeTrue();
        property.GetMethod.IsFinal.Should().BeFalse();
    }

    // ------------------------------------------------------------------ EF Core's [BackingField]

    [Fact]
    public void BackingField_is_emitted_when_EntityFramework_is_referenced()
    {
        var result = Basket("    public partial IReadOnlyList<int> Lines { get; }")
            .WithEntityFrameworkAbstractions()
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain("Sample.Basket.g.cs", "[global::Microsoft.EntityFrameworkCore.BackingField(nameof(_lines))]");
    }

    [Fact]
    public void BackingField_is_left_out_when_EntityFramework_is_not_referenced()
    {
        // A domain library that does not depend on EF must not get a reference to it through generated code.
        var result = Basket("    public partial IReadOnlyList<int> Lines { get; }").RunCore();

        result.ShouldCompile();
        result.ShouldNotContain("Sample.Basket.g.cs", "BackingField");
        result.ShouldNotContain("Sample.Basket.g.cs", "EntityFrameworkCore");
    }

    // ------------------------------------------------------------------ awkward placements

    [Fact]
    public void A_nested_aggregate_root_compiles()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            public partial class Outer
            {
                [AggregateRoot<ThingId>]
                public partial class Inner
                {
                    public Inner(ThingId id) : base(id) { }

                    public partial IReadOnlyList<int> Lines { get; }

                    public void Add(int line) => _lines.Add(line);
                }
            }
            """).RunCore();

        result.ShouldCompile();

        var emitted = result.Emit();
        var inner = emitted.New("Sample.Outer+Inner", emitted.CallStatic("Sample.ThingId", "CreateUnique")!);
        emitted.Call(inner, "Add", 9);

        ((IEnumerable<int>)emitted.Property(inner, "Lines")!).Should().Equal(9);
    }

    [Fact]
    public void A_nested_type_does_not_collide_with_a_top_level_type_of_the_same_name()
    {
        // Both are called Basket and both live in namespace Sample; the generator must give them
        // different hint names or the compiler refuses the duplicate generated file.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<ThingId>]
            public partial class Basket
            {
                public Basket(ThingId id) : base(id) { }
            }

            public partial class Outer
            {
                [AggregateRoot<ThingId>]
                public partial class Basket
                {
                    public Basket(ThingId id) : base(id) { }
                }
            }
            """).RunCore();

        result.ShouldNotCrash();
        result.ShouldCompile();
        result.GeneratedSources.Select(source => source.HintName).Should().OnlyHaveUniqueItems();
        result.GeneratedSources.Select(source => source.HintName)
            .Should().Contain(["Sample.Basket.g.cs", "Sample.Outer.Basket.g.cs"]);

        var emitted = result.Emit();
        emitted.HasType("Sample.Basket").Should().BeTrue();
        emitted.HasType("Sample.Outer+Basket").Should().BeTrue();
    }

    [Fact]
    public void An_aggregate_root_in_the_global_namespace_compiles()
    {
        var result = GeneratorTestHost.Create(
            """
            using DDDToolkit.Abstractions.Attributes;
            using System.Collections.Generic;

            [EntityId<System.Guid>]
            public readonly partial record struct RootlessId;

            [AggregateRoot<RootlessId>]
            public partial class Rootless
            {
                public Rootless(RootlessId id) : base(id) { }

                public partial IReadOnlyList<int> Lines { get; }

                public void Add(int line) => _lines.Add(line);
            }
            """).RunCore();

        result.ShouldCompile();
        result.GeneratedSources.Select(source => source.HintName).Should().Contain("Rootless.g.cs");
        result.ShouldNotContain("Rootless.g.cs", "namespace ");

        var emitted = result.Emit();
        var rootless = emitted.New("Rootless", emitted.CallStatic("RootlessId", "CreateUnique")!);
        emitted.Call(rootless, "Add", 4);
        ((IEnumerable)emitted.Property(rootless, "Lines")!).Cast<int>().Should().Equal(4);
    }

    [Fact]
    public void Several_collections_on_one_aggregate_each_get_their_own_field()
    {
        var result = Basket(
            """
                public partial IReadOnlyList<int> Lines { get; }

                public partial IReadOnlySet<string> Tags { get; }

                public partial IEnumerable<long> History { get; }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldContain("Sample.Basket.g.cs", "_lines");
        result.ShouldContain("Sample.Basket.g.cs", "_tags");
        result.ShouldContain("Sample.Basket.g.cs", "_history");
    }

    [Fact]
    public void A_collection_of_generated_ids_works_like_any_other()
    {
        var emitted = Basket(
            """
                public partial IReadOnlyList<ThingId> Children { get; }

                public void Add(ThingId child) => _children.Add(child);
            """).RunCore().Emit();

        var basket = emitted.New("Sample.Basket", emitted.CallStatic("Sample.ThingId", "CreateUnique")!);
        var child = emitted.CallStatic("Sample.ThingId", "CreateUnique")!;
        emitted.Call(basket, "Add", child);

        ((IEnumerable)emitted.Property(basket, "Children")!).Cast<object>().Should().Equal(child);
    }
}
