using HotChocolate.Utilities;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// DDDToolkit.HotChocolate.Analyzers gives every id and single value object a nested
/// <c>ChangeTypeProvider</c> so the GraphQL runtime can move between the strongly typed object and the
/// scalar it wraps, plus one <c>Add{Module}GraphQlRuntimeBindings</c> that binds each type to a scalar and
/// registers the converters.
/// </summary>
public class HotChocolateGeneratorTests
{
    private const string Preamble = "using DDDToolkit.Abstractions.Attributes;\nusing System;\n\nnamespace Sample;\n\n";

    private static GeneratorRunOutcome Run(string source, string module = "Sales")
        => GeneratorTestHost.Create(Preamble + source)
            .WithHotChocolate()
            .WithModule(module)
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

    [Fact]
    public void A_struct_id_gets_a_change_type_provider_that_converts_both_ways()
    {
        var result = Run(
            """
            [EntityId<Guid>("PRD")]
            public readonly partial record struct ProductId;
            """);

        result.ShouldCompile();
        result.ShouldContain("Sample.ProductId.HotChocolate.g.cs", "public sealed class ChangeTypeProvider : global::HotChocolate.Utilities.IChangeTypeProvider");

        var emitted = result.Emit();
        var provider = (IChangeTypeProvider)emitted.New("Sample.ProductId+ChangeTypeProvider");
        var idType = emitted.Type("Sample.ProductId");
        var guid = Guid.NewGuid();

        provider.TryCreateConverter(idType, typeof(Guid), NoRoot, out var toValue).Should().BeTrue();
        toValue!(emitted.New("Sample.ProductId", guid)).Should().Be(guid);

        provider.TryCreateConverter(typeof(Guid), idType, NoRoot, out var fromValue).Should().BeTrue();
        fromValue!(guid).Should().Be(emitted.New("Sample.ProductId", guid));
    }

    [Fact]
    public void An_id_generated_from_the_aggregate_is_bound_and_converted_like_any_other()
    {
        // The GraphQL generator never sees an [EntityId] attribute for this id; it reads the id from
        // the same provider the core generator emits it from.
        var result = Run(
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }
            }
            """);

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", "builder.BindRuntimeType<global::Sample.OrderId, global::HotChocolate.Types.UuidType>();");
        result.ShouldContain("BindingExtensions", "builder.AddTypeConverter<global::Sample.OrderId.ChangeTypeProvider>();");

        var emitted = result.Emit();
        var provider = (IChangeTypeProvider)emitted.New("Sample.OrderId+ChangeTypeProvider");
        var guid = Guid.NewGuid();

        provider.TryCreateConverter(emitted.Type("Sample.OrderId"), typeof(Guid), NoRoot, out var toValue).Should().BeTrue();
        toValue!(emitted.New("Sample.OrderId", guid)).Should().Be(guid);
    }

    [Fact]
    public void A_change_type_provider_refuses_conversions_it_knows_nothing_about()
    {
        var emitted = Run(
            """
            [EntityId<Guid>]
            public readonly partial record struct ProductId;
            """).Emit();

        var provider = (IChangeTypeProvider)emitted.New("Sample.ProductId+ChangeTypeProvider");

        provider.TryCreateConverter(typeof(string), typeof(int), NoRoot, out var converter).Should().BeFalse();
        converter.Should().BeNull();
    }

    [Fact]
    public void A_record_id_converts_its_always_valid_twin_as_well()
    {
        var result = Run(
            """
            [EntityId<Guid>("USR")]
            public partial record UserId
            {
                public static UserId Create(Guid value) => new(value);
            }
            """);

        result.ShouldCompile();

        var emitted = result.Emit();
        var provider = (IChangeTypeProvider)emitted.New("Sample.UserId+ChangeTypeProvider");
        var guid = Guid.NewGuid();

        provider.TryCreateConverter(emitted.Type("Sample.ValidUserId"), typeof(Guid), NoRoot, out var toValue).Should().BeTrue();
        toValue!(emitted.New("Sample.ValidUserId", guid)).Should().Be(guid);

        provider.TryCreateConverter(typeof(Guid), emitted.Type("Sample.ValidUserId"), NoRoot, out var fromValue).Should().BeTrue();
        fromValue!(guid).Should().Be(emitted.New("Sample.ValidUserId", guid));
    }

    [Fact]
    public void A_single_value_object_gets_a_change_type_provider()
    {
        var result = Run(
            """
            [SingleValueObject<string>]
            public partial record EmailAddress
            {
                public static EmailAddress Create(string value) => new(value);
            }
            """);

        result.ShouldCompile();

        var emitted = result.Emit();
        var provider = (IChangeTypeProvider)emitted.New("Sample.EmailAddress+ChangeTypeProvider");

        provider.TryCreateConverter(emitted.Type("Sample.EmailAddress"), typeof(string), NoRoot, out var toValue).Should().BeTrue();
        toValue!(emitted.CallStatic("Sample.EmailAddress", "Create", "ada@example.com")).Should().Be("ada@example.com");
    }

    [Fact]
    public void The_bindings_extension_is_named_after_the_module_and_binds_every_type()
    {
        var result = Run(
            """
            [EntityId<Guid>]
            public readonly partial record struct ProductId;

            [EntityId<int>]
            public readonly partial record struct LineNumber;

            [SingleValueObject<string>]
            public partial record EmailAddress;
            """);

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", "public static class HotChocolateExtensions");
        result.ShouldContain("BindingExtensions", "AddSalesGraphQlRuntimeBindings(this global::HotChocolate.Execution.Configuration.IRequestExecutorBuilder builder)");
        result.ShouldContain("BindingExtensions", "builder.BindRuntimeType<global::Sample.ProductId, global::HotChocolate.Types.UuidType>();");
        result.ShouldContain("BindingExtensions", "builder.BindRuntimeType<global::Sample.LineNumber, global::HotChocolate.Types.IntType>();");
        result.ShouldContain("BindingExtensions", "builder.BindRuntimeType<global::Sample.EmailAddress, global::HotChocolate.Types.StringType>();");
        result.ShouldContain("BindingExtensions", "builder.BindRuntimeType<global::Sample.ValidEmailAddress, global::HotChocolate.Types.StringType>();");
        result.ShouldContain("BindingExtensions", "builder.AddTypeConverter<global::Sample.ProductId.ChangeTypeProvider>();");
    }

    [Fact]
    public void The_bindings_extension_is_a_public_static_extension_on_IRequestExecutorBuilder()
    {
        var emitted = Run(
            """
            [EntityId<Guid>]
            public readonly partial record struct ProductId;
            """).Emit();

        var method = emitted.Type(GeneratorTestHost.DefaultAssemblyName + ".GraphQl.HotChocolateExtensions")
            .GetMethod("AddSalesGraphQlRuntimeBindings")!;

        method.IsStatic.Should().BeTrue();
        method.IsPublic.Should().BeTrue();
        method.GetParameters().Single().ParameterType.Name.Should().Be("IRequestExecutorBuilder");
    }

    [Fact]
    public void GraphQLType_overrides_the_default_scalar_mapping()
    {
        var result = GeneratorTestHost.Create(
            """
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.HotChocolate.Attributes;

            namespace Sample;

            [GraphQLType<HotChocolate.Types.UrlType>]
            [SingleValueObject<string>]
            public partial record Website;
            """)
            .WithHotChocolate()
            .WithModule("Sales")
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", "builder.BindRuntimeType<global::Sample.Website, global::HotChocolate.Types.UrlType>();");
        result.ShouldNotContain("BindingExtensions", "StringType");
    }

    [Fact]
    public void A_value_type_with_no_default_scalar_is_still_given_a_converter()
    {
        // No scalar mapping exists for a custom struct, so nothing is bound - but the converter is
        // still registered, which is what lets the author bind the scalar themselves.
        var result = Run(
            """
            public readonly record struct Ticket(int Number);

            [EntityId<Ticket>]
            public readonly partial record struct TicketId;
            """);

        result.ShouldCompile();
        result.ShouldNotContain("BindingExtensions", "BindRuntimeType");
        result.ShouldContain("BindingExtensions", "builder.AddTypeConverter<global::Sample.TicketId.ChangeTypeProvider>();");
    }

    [Fact]
    public void Nothing_is_generated_for_an_assembly_with_no_ids_or_value_objects()
    {
        var result = Run("public sealed class Nothing;");

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("BindingExtensions", StringComparison.Ordinal));
    }

    [Fact]
    public void A_type_the_core_generator_refused_gets_no_GraphQL_code_either()
    {
        var result = Run(
            """
            [EntityId<Guid>]
            public partial class NotARecord
            {
            }
            """);

        result.ShouldHaveDiagnostic("DDD00003", at: "NotARecord");
        result.ShouldNotHaveGeneratedFor("NotARecord");
    }

    /// <summary>A root provider that converts nothing, so only what the generated provider knows can succeed.</summary>
    private static bool NoRoot(
        Type source,
        Type target,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ChangeType? converter)
    {
        converter = null;
        return false;
    }
}
