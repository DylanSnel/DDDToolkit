namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00006: a DDDToolkit type cannot have type parameters.
/// <para>
/// The generators redeclare the type by name and never repeat its type parameters, so
/// <c>[ValueObject] partial record Box&lt;T&gt;</c> used to produce a <em>second</em>, unrelated,
/// non-generic <c>Box</c> that happened to compile — while <c>Box&lt;T&gt;</c> itself got nothing.
/// The author saw a pile of errors about members that "do not exist" and no mention of the real cause.
/// </para>
/// </summary>
public class GenericTypeDiagnosticTests
{
    private const string Usings = "using DDDToolkit.Abstractions.Attributes;\nusing System;\n\nnamespace Sample;\n\n";

    [Fact]
    public void A_generic_value_object_reports_DDD00006_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [ValueObject]
            public partial record Box<T>
            {
                public T Item { get; protected init; } = default!;
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00006", at: "Box");
        diagnostic.GetMessage().Should().Contain("type parameters");
        result.ShouldNotHaveGeneratedFor("Box");
        result.CompilationErrors.Should().BeEmpty(
            "the one diagnostic replaces the cascade of errors about members of a phantom non-generic Box");
    }

    [Fact]
    public void A_generic_single_value_object_reports_DDD00006_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [SingleValueObject<string>]
            public partial record Wrapper<T>;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00006", at: "Wrapper");
        result.ShouldNotHaveGeneratedFor("Wrapper");
    }

    [Fact]
    public void A_generic_entity_id_reports_DDD00006_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>]
            public readonly partial record struct Key<T>;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00006", at: "Key");
        result.ShouldNotHaveGeneratedFor("Key");
    }

    [Fact]
    public void A_generic_aggregate_root_reports_DDD00006_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>]
            public readonly partial record struct ThingId;

            [AggregateRoot<ThingId>]
            public partial class Box<T>
            {
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00006", at: "Box");
        diagnostic.GetMessage().Should().Contain("AggregateRoot");
        result.ShouldNotHaveGeneratedFor("Box");
    }

    [Fact]
    public void A_generic_entity_reports_DDD00006_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>]
            public readonly partial record struct ThingId;

            [Entity<ThingId>]
            public partial class Box<T>
            {
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00006", at: "Box");
        result.ShouldNotHaveGeneratedFor("Box");
    }

    [Fact]
    public void A_type_nested_inside_a_generic_type_reports_DDD00006_too()
    {
        // A type parameter of the containing type is just as unusable: the generated
        // [JsonConverter(typeof(InnerId.SystemTextJsonConverter))] would be an attribute argument
        // naming Outer<T>.InnerId (CS0416), and the generated Add{Module}Converters, which lives
        // outside Outer<T>, could not name the type at all.
        var result = GeneratorTestHost.Create(Usings +
            """
            public partial class Outer<T>
            {
                [EntityId<Guid>]
                public readonly partial record struct InnerId;
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00006", at: "InnerId");
        result.ShouldNotHaveGeneratedFor("InnerId");
        result.CompilationErrors.Should().BeEmpty();
    }

    [Fact]
    public void A_type_nested_inside_a_non_generic_type_is_fine()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            public partial class Outer
            {
                [EntityId<Guid>]
                public readonly partial record struct InnerId;
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00006");
        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample.Outer.InnerId"), "partial class Outer");
    }

    [Fact]
    public void A_non_generic_type_is_not_bothered()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [ValueObject]
            public partial record Box
            {
                public string Item { get; protected init; } = "";
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00006");
        result.ShouldCompile();
    }
}
