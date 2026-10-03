using HotChocolate.Utilities;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// DDDToolkit.HotChocolate.Analyzers gives every id and single value object a nested
/// <c>ChangeTypeProvider</c> so the GraphQL runtime can move between the strongly typed object and the
/// scalar it wraps, plus one <c>Add{Module}GraphQlRuntimeBindings</c> that binds each type to a scalar and
/// registers the converters. That method also binds the ids of the module's projects without HotChocolate, and
/// the published ids of other modules, with the package's generic provider.
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
        result.ShouldContain(Hint.Of("Sample.ProductId", ".HotChocolate"), "public sealed class ChangeTypeProvider : global::HotChocolate.Utilities.IChangeTypeProvider");

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
    public void GraphQLType_on_the_authors_own_part_of_a_generated_id_still_counts()
    {
        // The generated part cannot carry the attribute, so the author's part is the only place it can
        // go. Ignoring it there would make the override silently do nothing.
        var result = GeneratorTestHost.Create(
            """
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.HotChocolate.Attributes;
            using System;

            namespace Sample;

            [GraphQLType<HotChocolate.Types.StringType>]
            public readonly partial record struct OrderId;

            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }
            }
            """)
            .WithHotChocolate()
            .WithModule("Sales")
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", "builder.BindRuntimeType<global::Sample.OrderId, global::HotChocolate.Types.StringType>();");
        result.ShouldNotContain("BindingExtensions", "UuidType");
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

    // ------------------------------------------------------------------ Relay node ids

    [Theory]
    [InlineData("[EntityId<Guid>(\"PRD\")] public readonly partial record struct ProductId;", "ProductId")]
    [InlineData("[EntityId<Guid>] public partial record CustomerId;", "CustomerId")]
    [InlineData("[EntityId<int>] public readonly partial record struct SeatId;", "SeatId")]
    [InlineData("[EntityId<string>] public readonly partial record struct LoginId;", "LoginId")]
    public void An_id_gets_a_node_id_serializer_that_reads_back_what_it_wrote(string declaration, string name)
    {
        var result = Run(declaration);

        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample." + name, ".HotChocolate"), "public sealed class NodeIdValueSerializer : global::HotChocolate.Types.Relay.CompositeNodeIdValueSerializer<global::Sample." + name + ">");
        result.ShouldContain("BindingExtensions", "builder.AddNodeIdValueSerializer<global::Sample." + name + ".NodeIdValueSerializer>();");

        var emitted = result.Emit();
        var serializer = (global::HotChocolate.Types.Relay.INodeIdValueSerializer)emitted.New("Sample." + name + "+NodeIdValueSerializer");
        var idType = emitted.Type("Sample." + name);
        var raw = name switch
        {
            "SeatId" => (object)12,
            "LoginId" => "ada@example.com",
            _ => Guid.NewGuid(),
        };
        var id = Activator.CreateInstance(idType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, [raw], null);

        serializer.IsSupported(idType).Should().BeTrue();

        Span<byte> buffer = stackalloc byte[128];
        serializer.Format(buffer, id!, out var written).Should().Be(global::HotChocolate.Types.Relay.NodeIdFormatterResult.Success);

        serializer.TryParse(buffer[..written], out var read).Should().BeTrue();
        read.Should().Be(id);
    }

    [Fact]
    public void A_single_value_object_is_not_an_identity_and_gets_no_node_id_serializer()
    {
        var result = Run(
            """
            [SingleValueObject<string>]
            public partial record EmailAddress;
            """);

        result.ShouldCompile();
        result.ShouldNotContain(Hint.Of("Sample.EmailAddress", ".HotChocolate"), "NodeIdValueSerializer");
    }

    [Fact]
    public void Asking_HotChocolates_generator_for_a_toolkit_ids_serializer_reports_DDD00032()
    {
        // HotChocolate's generator declares AddNodeIdValueSerializerFrom<T>() and intercepts the call; this
        // stands in for it, because the analyzer only looks at the call.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<Guid>("PRD")]
            public readonly partial record struct ProductId;

            public static class HotChocolateGenerated
            {
                public static HotChocolate.Execution.Configuration.IRequestExecutorBuilder AddNodeIdValueSerializerFrom<T>(
                    this HotChocolate.Execution.Configuration.IRequestExecutorBuilder builder) => builder;
            }

            public static class Schema
            {
                public static void Configure(HotChocolate.Execution.Configuration.IRequestExecutorBuilder graphql)
                    => graphql.AddNodeIdValueSerializerFrom<ProductId>();
            }
            """)
            .WithHotChocolate()
            .WithModule("Sales")
            .WithAnalyzers(new DDDToolkit.HotChocolate.Analyzers.NodeIdSerializerFromAnalyzer())
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldHaveDiagnostic("DDD00032", at: "graphql.AddNodeIdValueSerializerFrom<ProductId>()");
    }

    // ------------------------------------------------------------------ paging keys

    private const string CursorKeySerializer = "global::DDDToolkit.HotChocolate.Paging.SingleValueCursorKeySerializer";

    private static string PagedBy(string type, string value)
        => CursorKeySerializer + "<" + type + ", " + value + ">.Register();";

    [Fact]
    public void A_struct_id_is_registered_as_a_key_paging_can_order_a_list_by()
    {
        var result = Run(
            """
            [EntityId<Guid>("PRD")]
            public readonly partial record struct ProductId;

            [EntityId<int>]
            public readonly partial record struct LineNumber;

            [EntityId<Guid>]
            public partial record CustomerId;

            [SingleValueObject<string>]
            public partial record EmailAddress;

            public readonly record struct Coordinates(int X, int Y);

            [EntityId<Coordinates>]
            public readonly partial record struct ShelfId;
            """);

        // That it compiles is the strong half: Register() exists only on a serializer closed over an id that
        // implements ISingleValue<T, TValue> and compares to itself.
        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", PagedBy("global::Sample.ProductId", "global::System.Guid"));
        result.ShouldContain("BindingExtensions", PagedBy("global::Sample.LineNumber", "int"));
        result.ShouldNotContain("BindingExtensions", CursorKeySerializer + "<global::Sample.CustomerId", "a class id does not compare to itself, which paging needs of a key");
        result.ShouldNotContain("BindingExtensions", CursorKeySerializer + "<global::Sample.ValidCustomerId", "nor does its twin");
        result.ShouldNotContain("BindingExtensions", CursorKeySerializer + "<global::Sample.EmailAddress", "a single value object is not an identity");
        result.ShouldNotContain("BindingExtensions", CursorKeySerializer + "<global::Sample.ShelfId", "no cursor carries its value, and registering it would fail when the bindings are added");
    }

    [Fact]
    public void A_project_without_struct_ids_registers_no_paging_key()
    {
        var result = Run(
            """
            [EntityId<Guid>]
            public partial record CustomerId;
            """);

        result.ShouldCompile();
        result.ShouldNotContain("BindingExtensions", "SingleValueCursorKeySerializer");
        result.ShouldNotContain("BindingExtensions", "ToPageAsync", "the comment that introduces the registrations is not written either");
    }

    // ------------------------------------------------------------------ a module in layers

    private const string Provider = "global::DDDToolkit.HotChocolate.Types.SingleValueChangeTypeProvider";

    private const string NodeIdSerializer = "global::DDDToolkit.HotChocolate.Types.SingleValueNodeIdSerializer";

    /// <summary>A module's domain project, without HotChocolate: an id of each kind, a single value object and a value no scalar is known for.</summary>
    private const string SalesDomain =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        [assembly: Module("Sales")]

        namespace Sales.Domain;

        [EntityId<Guid>("PRD")]
        public readonly partial record struct ProductId;

        [EntityId<Guid>("CUS")]
        public partial record CustomerId;

        [EntityId<int>]
        public readonly partial record struct LineNumber;

        [EntityId<decimal>]
        public readonly partial record struct LedgerPosition;

        [SingleValueObject<string>]
        public partial record EmailAddress;

        [AggregateRoot<string>("INV")]
        public partial class Invoice
        {
            public Invoice(InvoiceId id) : base(id) { }
        }

        public readonly record struct Coordinates(int X, int Y);

        [EntityId<Coordinates>]
        public readonly partial record struct ShelfId;

        [EntityId<Guid>]
        internal readonly partial record struct DraftId;
        """;

    /// <summary>
    /// A project of the module that builds its schema: it references HotChocolate, and declares no ids. It sets a
    /// <c>DDD_Module</c> of its own as well, which names its bindings only when it declares no module.
    /// </summary>
    private static GeneratorTestHost Api(string? module = "Sales", string source = "namespace Sales.Api;\n\npublic static class Nothing;")
    {
        var host = GeneratorTestHost.Create(source, "Api.cs").WithAssemblyName("Sales.Api").WithModule("SalesApi");
        return module is null ? host : host.WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"" + module + "\")]", "Module.cs");
    }

    private static string Bound(string type, string scalar)
        => "builder.BindRuntimeType<" + type + ", global::HotChocolate.Types." + scalar + ">();";

    private static string Converted(string type, string value)
        => "builder.AddTypeConverter<" + Provider + "<" + type + ", " + value + ">>();";

    private static string WrittenIntoNodeIds(string type, string value)
        => "builder.AddNodeIdValueSerializer<" + NodeIdSerializer + "<" + type + ", " + value + ">>();";

    [Fact]
    public void Ids_of_the_modules_project_without_HotChocolate_are_bound_with_the_single_value_provider()
    {
        var result = Api()
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .WithHotChocolate()
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        // That it compiles is the strong half: AddTypeConverter<SingleValueChangeTypeProvider<T, TValue>> and
        // AddNodeIdValueSerializer<SingleValueNodeIdSerializer<T, TValue>> type-check against HotChocolate only when
        // T really implements ISingleValue<T, TValue>.
        result.ShouldCompile();
        result.ShouldContain(
            "BindingExtensions",
            "AddSalesGraphQlRuntimeBindings(this global::HotChocolate.Execution.Configuration.IRequestExecutorBuilder builder)",
            "the module names the bindings, whatever DDD_Module the project sets");

        result.ShouldContain("BindingExtensions", Bound("global::Sales.Domain.ProductId", "UuidType"));
        result.ShouldContain("BindingExtensions", Converted("global::Sales.Domain.ProductId", "global::System.Guid"));
        result.ShouldContain("BindingExtensions", WrittenIntoNodeIds("global::Sales.Domain.ProductId", "global::System.Guid"));

        result.ShouldContain("BindingExtensions", Bound("global::Sales.Domain.CustomerId", "UuidType"));
        result.ShouldContain("BindingExtensions", WrittenIntoNodeIds("global::Sales.Domain.CustomerId", "global::System.Guid"));
        result.ShouldContain("BindingExtensions", Bound("global::Sales.Domain.ValidCustomerId", "UuidType"), "a twin prints as the same scalar");
        result.ShouldContain("BindingExtensions", Converted("global::Sales.Domain.ValidCustomerId", "global::System.Guid"), "and has a provider of its own: nothing nested in its parent converts it");
        result.ShouldNotContain("BindingExtensions", NodeIdSerializer + "<global::Sales.Domain.ValidCustomerId", "a twin is not the identity, as it is not in the project that declares it");

        result.ShouldContain("BindingExtensions", Bound("global::Sales.Domain.LineNumber", "IntType"));
        result.ShouldContain("BindingExtensions", WrittenIntoNodeIds("global::Sales.Domain.LineNumber", "int"));

        result.ShouldContain("BindingExtensions", Bound("global::Sales.Domain.InvoiceId", "StringType"), "an id an aggregate asked for is an id like any other");
        result.ShouldContain("BindingExtensions", WrittenIntoNodeIds("global::Sales.Domain.InvoiceId", "string"));

        result.ShouldContain("BindingExtensions", Bound("global::Sales.Domain.EmailAddress", "StringType"));
        result.ShouldContain("BindingExtensions", Converted("global::Sales.Domain.EmailAddress", "string"));
        result.ShouldContain("BindingExtensions", Bound("global::Sales.Domain.ValidEmailAddress", "StringType"));
        result.ShouldContain("BindingExtensions", Converted("global::Sales.Domain.ValidEmailAddress", "string"));
        result.ShouldNotContain("BindingExtensions", NodeIdSerializer + "<global::Sales.Domain.EmailAddress", "a single value object is not an identity");

        result.ShouldContain("BindingExtensions", Bound("global::Sales.Domain.LedgerPosition", "DecimalType"));
        result.ShouldNotContain("BindingExtensions", NodeIdSerializer + "<global::Sales.Domain.LedgerPosition", "a node id carries no decimal");

        result.ShouldNotContain("BindingExtensions", "BindRuntimeType<global::Sales.Domain.ShelfId", "no scalar is known for its value");
        result.ShouldContain("BindingExtensions", Converted("global::Sales.Domain.ShelfId", "global::Sales.Domain.Coordinates"), "the converter is still registered, so the application can bind the scalar");

        result.ShouldNotContain("BindingExtensions", "DraftId", "a type this project cannot see is not one it can bind");

        // The struct ids among them are keys paging can order by, registered where they are bound.
        result.ShouldContain("BindingExtensions", PagedBy("global::Sales.Domain.ProductId", "global::System.Guid"));
        result.ShouldContain("BindingExtensions", PagedBy("global::Sales.Domain.LineNumber", "int"));
        result.ShouldContain("BindingExtensions", PagedBy("global::Sales.Domain.InvoiceId", "string"), "an id an aggregate asked for is a struct");
        result.ShouldContain("BindingExtensions", PagedBy("global::Sales.Domain.LedgerPosition", "decimal"), "a cursor carries a decimal, though a node id does not");
        result.ShouldNotContain("BindingExtensions", CursorKeySerializer + "<global::Sales.Domain.CustomerId", "a class id does not compare to itself");
        result.ShouldNotContain("BindingExtensions", CursorKeySerializer + "<global::Sales.Domain.ValidCustomerId");
        result.ShouldNotContain("BindingExtensions", CursorKeySerializer + "<global::Sales.Domain.EmailAddress", "a single value object is not an identity");
        result.ShouldNotContain("BindingExtensions", CursorKeySerializer + "<global::Sales.Domain.ShelfId", "no cursor carries its value");
    }

    [Fact]
    public void A_project_with_no_types_of_its_own_still_gets_its_bindings()
    {
        var own = Api()
            .WithHotChocolate()
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());
        own.GeneratedSources.Should().NotContain(source => source.HintName.Contains("BindingExtensions", StringComparison.Ordinal), "nothing to bind is nothing to write");

        var withDomain = Api()
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .WithHotChocolate()
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());
        withDomain.ShouldCompile();
        withDomain.ShouldHaveGenerated("BindingExtensions");

        var method = withDomain.OutputCompilation.GetTypeByMetadataName("Sales.Api.GraphQl.HotChocolateExtensions")!
            .GetMembers("AddSalesGraphQlRuntimeBindings").Should().ContainSingle().Subject;
        method.DeclaredAccessibility.Should().Be(Microsoft.CodeAnalysis.Accessibility.Public);
        method.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void A_published_id_of_another_module_gets_graphql_bindings_in_the_consumer()
    {
        var result = Api(source:
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                namespace Sales.Api;

                [EntityId<Guid>]
                public readonly partial record struct BasketId;
                """)
            .WithReferencedAssembly(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                [assembly: Module("Billing")]

                namespace Billing.Contracts;

                [ModuleContract]
                [EntityId<Guid>]
                public readonly partial record struct InvoiceNumber;

                [ModuleContract]
                [EntityId<Guid>]
                public partial record PayerId;

                [EntityId<Guid>]
                public readonly partial record struct LedgerLineId;
                """,
                "Billing.Contracts")
            .WithHotChocolate()
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", "builder.AddTypeConverter<global::Sales.Api.BasketId.ChangeTypeProvider>();", "its own ids keep their nested providers");
        result.ShouldContain("BindingExtensions", "builder.AddNodeIdValueSerializer<global::Sales.Api.BasketId.NodeIdValueSerializer>();");

        result.ShouldContain("BindingExtensions", Bound("global::Billing.Contracts.InvoiceNumber", "UuidType"));
        result.ShouldContain("BindingExtensions", Converted("global::Billing.Contracts.InvoiceNumber", "global::System.Guid"));
        result.ShouldContain("BindingExtensions", WrittenIntoNodeIds("global::Billing.Contracts.InvoiceNumber", "global::System.Guid"));
        result.ShouldContain("BindingExtensions", Bound("global::Billing.Contracts.PayerId", "UuidType"));
        result.ShouldContain("BindingExtensions", Converted("global::Billing.Contracts.PayerId", "global::System.Guid"));
        result.ShouldContain("BindingExtensions", Converted("global::Billing.Contracts.ValidPayerId", "global::System.Guid"), "the twin of a published id is the published id, validated");
        result.ShouldNotContain("BindingExtensions", "LedgerLineId", "another module's own ids are its business (DDD00022)");
    }

    [Fact]
    public void An_id_whose_assembly_binds_it_itself_is_not_bound_twice()
    {
        // The contracts project references HotChocolate, as it used to have to: it binds its ids itself.
        var result = Api()
            .WithHotChocolate()
            .WithReferencedProject(
                "Sales.Contracts",
                project => project
                    .WithModule("SalesContracts")
                    .WithSource(
                        """
                        using System;
                        using DDDToolkit.Abstractions.Attributes;

                        [assembly: Module("Sales")]

                        namespace Sales.Contracts;

                        [ModuleContract]
                        [EntityId<Guid>]
                        public readonly partial record struct CustomerNumber;

                        [ModuleContract]
                        [EntityId<Guid>]
                        public partial record AccountId;
                        """),
                GeneratorTestHost.HotChocolateGenerators())
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", Converted("global::Sales.Domain.ProductId", "global::System.Guid"));
        result.ShouldNotContain("BindingExtensions", "CustomerNumber", "the contracts' own AddSalesGraphQlRuntimeBindings() binds it with its nested provider");
        result.ShouldNotContain("BindingExtensions", "AccountId", "and the provider nested in an id converts its twin as well");
        result.ShouldContain(
            "BindingExtensions",
            "global::Sales.Contracts.GraphQl.HotChocolateExtensions.AddSalesGraphQlRuntimeBindings(builder);",
            "and this project's bindings call that one, so the schema makes one call for the module");
    }

    /// <summary>
    /// A second project of the module with HotChocolate, above the one that builds the schema: a gateway's own
    /// types, say. It references the API project and, through it, the domain project.
    /// </summary>
    private static GeneratorTestHost AboveApi(string source)
        => GeneratorTestHost.Create(source, "Gateway.cs")
            .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Sales\")]", "Module.cs")
            .WithAssemblyName("Sales.Gateway")
            .WithHotChocolate()
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .WithReferencedProject(
                "Sales.Api",
                project => project
                    .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Sales\")]", "Module.cs")
                    .WithSource(
                        """
                        using System;
                        using DDDToolkit.Abstractions.Attributes;

                        namespace Sales.Api;

                        [EntityId<Guid>]
                        public readonly partial record struct BasketId;
                        """,
                        "Api.cs"),
                GeneratorTestHost.HotChocolateGenerators());

    [Fact]
    public void What_bindings_this_project_calls_have_bound_is_not_bound_again()
    {
        // Both projects reference the domain project, both are Sales and both run the generator. The API project
        // bound the domain's ids; this one calls it, so it leaves them to it.
        var result = AboveApi(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                namespace Sales.Gateway;

                [EntityId<Guid>]
                public readonly partial record struct SessionId;
                """)
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", "global::Sales.Api.GraphQl.HotChocolateExtensions.AddSalesGraphQlRuntimeBindings(builder);");
        result.ShouldContain("BindingExtensions", "builder.AddTypeConverter<global::Sales.Gateway.SessionId.ChangeTypeProvider>();", "its own id is its own to bind");
        result.ShouldNotContain("BindingExtensions", "Sales.Domain", "the bindings it calls have every id of the domain project");
        result.ShouldNotContain("BindingExtensions", Provider);
    }

    [Fact]
    public void A_project_whose_module_is_bound_by_the_one_it_references_gets_no_bindings_of_its_own()
    {
        // Nothing is left for it to bind, so nothing is written: its schema calls the API project's
        // AddSalesGraphQlRuntimeBindings(), the only one there is to import.
        var result = AboveApi(
                """
                using HotChocolate.Execution.Configuration;
                using Sales.Api.GraphQl;

                namespace Sales.Gateway;

                public static class GatewaySchema
                {
                    public static IRequestExecutorBuilder AddSales(this IRequestExecutorBuilder builder)
                        => builder.AddSalesGraphQlRuntimeBindings();
                }
                """)
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("BindingExtensions", StringComparison.Ordinal));
    }

    [Fact]
    public void What_the_called_bindings_could_not_see_is_still_bound_here()
    {
        // The contracts project is referenced by this project and not by the API project, so the bindings this
        // one calls know nothing of its ids.
        var result = AboveApi("namespace Sales.Gateway;\n\npublic static class Nothing;")
            .WithReferencedAssembly(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                [assembly: Module("Sales")]

                namespace Sales.Contracts;

                [ModuleContract]
                [EntityId<Guid>]
                public readonly partial record struct CustomerNumber;
                """,
                "Sales.Contracts")
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", Converted("global::Sales.Contracts.CustomerNumber", "global::System.Guid"));
        result.ShouldContain("BindingExtensions", "global::Sales.Api.GraphQl.HotChocolateExtensions.AddSalesGraphQlRuntimeBindings(builder);");
        result.ShouldNotContain("BindingExtensions", "Sales.Domain", "those the called bindings have");
    }

    [Fact]
    public void A_scalar_the_called_bindings_could_not_name_is_bound_here()
    {
        // The domain project takes a scalar from an assembly this project references and the API project does
        // not. The API project's bindings have the converter without the scalar, so this one writes both.
        const string measures =
            """
            namespace Sales.Measures;

            public sealed class HandleType : HotChocolate.Types.StringType;
            """;

        var result = GeneratorTestHost.Create("namespace Sales.Gateway;\n\npublic static class Nothing;", "Gateway.cs")
            .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Sales\")]", "Module.cs")
            .WithAssemblyName("Sales.Gateway")
            .WithHotChocolate()
            .WithReferencedProject(
                "Sales.Domain",
                project => project
                    .WithReferencedAssembly(measures, "Sales.Measures")
                    .WithSource(
                        """
                        using System;
                        using DDDToolkit.Abstractions.Attributes;
                        using DDDToolkit.HotChocolate.Attributes;

                        [assembly: Module("Sales")]

                        namespace Sales.Domain;

                        [GraphQLType<Sales.Measures.HandleType>]
                        [SingleValueObject<string>]
                        public partial record Handle;

                        [EntityId<Guid>]
                        public readonly partial record struct ParcelId;
                        """,
                        "Domain.cs"))
            .WithReferencedProject(
                "Sales.Api",
                project => project
                    .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Sales\")]", "Module.cs")
                    .WithSource("namespace Sales.Api;\n\npublic static class Nothing;", "Api.cs"),
                GeneratorTestHost.HotChocolateGenerators())
            .WithReferencedAssembly(measures, "Sales.Measures")
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", "global::Sales.Api.GraphQl.HotChocolateExtensions.AddSalesGraphQlRuntimeBindings(builder);");
        result.ShouldContain(
            "BindingExtensions",
            "builder.BindRuntimeType<global::Sales.Domain.Handle, global::Sales.Measures.HandleType>();",
            "the API project could not write the scalar's name, so nothing it calls has this binding");
        result.ShouldContain("BindingExtensions", Converted("global::Sales.Domain.Handle", "string"), "the converter a second time changes nothing");
        result.ShouldNotContain("BindingExtensions", "ParcelId", "what the called bindings bound whole is left to them");
    }

    [Fact]
    public void A_referenced_package_without_a_module_gets_no_bindings()
    {
        // The Tenancy package declares TenantSlug, a single value object, and no module; a shared kernel without a
        // module is no module's to bind either.
        var result = Api(source:
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                namespace Sales.Api;

                [EntityId<Guid>]
                public readonly partial record struct BasketId;
                """)
            .WithTenancy()
            .WithReferencedAssembly(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                namespace Shared.Kernel;

                [EntityId<Guid>]
                public readonly partial record struct BatchNumber;
                """,
                "Shared.Kernel")
            .WithHotChocolate()
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", "Sales.Api.BasketId");
        result.ShouldNotContain("BindingExtensions", "TenantSlug");
        result.ShouldNotContain("BindingExtensions", "BatchNumber");
        result.ShouldNotContain("BindingExtensions", Provider);
    }

    [Fact]
    public void A_project_without_a_module_binds_only_its_own()
    {
        var result = Api(module: null, source:
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                namespace Sales.Host;

                [EntityId<Guid>]
                public readonly partial record struct RequestId;
                """)
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .WithHotChocolate()
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", "Sales.Host.RequestId");
        result.ShouldNotContain("BindingExtensions", "Sales.Domain", "a project without a module is no module's");
    }

    [Fact]
    public void A_project_that_cannot_name_the_provider_binds_only_its_own()
    {
        // HotChocolate without DDDToolkit.HotChocolate, which is what a project sees whose copy of the package is
        // older than its generator: a binding it cannot compile would be worse than none.
        var result = Api(source:
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                namespace Sales.Api;

                [EntityId<Guid>]
                public readonly partial record struct BasketId;
                """)
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .WithHotChocolateAlone()
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", "builder.AddTypeConverter<global::Sales.Api.BasketId.ChangeTypeProvider>();");
        result.ShouldNotContain("BindingExtensions", "Sales.Domain");
        result.ShouldNotContain("BindingExtensions", "SingleValueCursorKeySerializer", "a serializer it cannot name is not one it can register, for its own id either");
    }

    [Fact]
    public void A_referenced_type_that_names_its_schema_type_is_bound_to_it()
    {
        // A project can reference DDDToolkit.HotChocolate for the attribute and not run its generator: the type
        // then says how it prints and has no provider of its own.
        var result = Api()
            .WithHotChocolate()
            .WithReferencedProject(
                "Sales.Domain",
                project => project.WithSource(
                    """
                    using System;
                    using DDDToolkit.Abstractions.Attributes;
                    using DDDToolkit.HotChocolate.Attributes;

                    [assembly: Module("Sales")]

                    namespace Sales.Domain;

                    [GraphQLType<HotChocolate.Types.UrlType>]
                    [SingleValueObject<string>]
                    public partial record Website;

                    internal sealed class HiddenType : HotChocolate.Types.StringType;

                    [GraphQLType<HiddenType>]
                    [SingleValueObject<string>]
                    public partial record Nickname;
                    """))
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain("BindingExtensions", Bound("global::Sales.Domain.Website", "UrlType"));
        result.ShouldContain("BindingExtensions", Bound("global::Sales.Domain.ValidWebsite", "UrlType"), "a twin prints as its parent does");
        result.ShouldNotContain("BindingExtensions", "BindRuntimeType<global::Sales.Domain.Website, global::HotChocolate.Types.StringType>");

        result.ShouldNotContain("BindingExtensions", "HiddenType", "a schema type this project cannot see cannot be written into it");
        result.ShouldNotContain("BindingExtensions", "BindRuntimeType<global::Sales.Domain.Nickname", "and the default scalar is not what the type asked for");
        result.ShouldContain("BindingExtensions", Converted("global::Sales.Domain.Nickname", "string"));
    }

    [Fact]
    public void A_name_of_an_assembly_this_project_does_not_reference_is_not_written_into_it()
    {
        // The domain project takes a scalar and a value from an assembly the API project does not reference. Their
        // names are all this compilation has of them, and a binding to a name it cannot resolve would not compile.
        var result = Api()
            .WithHotChocolate()
            .WithReferencedProject(
                "Sales.Domain",
                project => project
                    .WithReferencedAssembly(
                        """
                        namespace Sales.Measures;

                        public sealed class HandleType : HotChocolate.Types.StringType;

                        public readonly record struct Tonnage(decimal Tonnes);
                        """,
                        "Sales.Measures")
                    .WithSource(
                        """
                        using System;
                        using DDDToolkit.Abstractions.Attributes;
                        using DDDToolkit.HotChocolate.Attributes;

                        [assembly: Module("Sales")]

                        namespace Sales.Domain;

                        [GraphQLType<Sales.Measures.HandleType>]
                        [SingleValueObject<string>]
                        public partial record Handle;

                        [SingleValueObject<Sales.Measures.Tonnage>]
                        public partial record Cargo;

                        [EntityId<Guid>]
                        public readonly partial record struct ParcelId;
                        """))
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();

        result.ShouldNotContain("BindingExtensions", "HandleType");
        result.ShouldNotContain("BindingExtensions", "BindRuntimeType<global::Sales.Domain.Handle", "neither to the type it named nor to the default one it did not ask for");
        result.ShouldContain("BindingExtensions", Converted("global::Sales.Domain.Handle", "string"), "the converter needs no name but the type's and its value's");

        result.ShouldNotContain("BindingExtensions", "Tonnage");
        result.ShouldNotContain("BindingExtensions", "Sales.Domain.Cargo", "a provider is closed over the value, so a value without a name leaves nothing to write");
        result.ShouldNotContain("BindingExtensions", "Sales.Domain.ValidCargo");

        result.ShouldContain("BindingExtensions", Bound("global::Sales.Domain.ParcelId", "UuidType"), "the rest of the project is bound as ever");
        result.ShouldContain("BindingExtensions", WrittenIntoNodeIds("global::Sales.Domain.ParcelId", "global::System.Guid"));
    }

    [Fact]
    public void The_bindings_of_other_projects_are_written_in_one_order_whatever_the_order_of_the_references()
    {
        const string billing =
            """
            using System;
            using DDDToolkit.Abstractions.Attributes;

            [assembly: Module("Billing")]

            namespace Billing.Contracts;

            [ModuleContract]
            [EntityId<Guid>]
            public readonly partial record struct InvoiceNumber;
            """;

        var domainFirst = Api()
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .WithReferencedAssembly(billing, "Billing.Contracts")
            .WithHotChocolate()
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());
        var billingFirst = Api()
            .WithReferencedAssembly(billing, "Billing.Contracts")
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .WithHotChocolate()
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        domainFirst.ShouldCompile();
        billingFirst.Source("BindingExtensions").Should().Be(domainFirst.Source("BindingExtensions"));
        domainFirst.Source("BindingExtensions").IndexOf("Billing.Contracts.InvoiceNumber", StringComparison.Ordinal)
            .Should().BeLessThan(domainFirst.Source("BindingExtensions").IndexOf("Sales.Domain.CustomerId", StringComparison.Ordinal));
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
