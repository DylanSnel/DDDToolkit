namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// DDDToolkit.EntityFramework.Analyzers: a <c>ValueConverter</c> for every id and single value object, one
/// <c>Add{Module}Converters</c> that registers them all, <c>[Owned]</c> on child entities and
/// <c>[ComplexType]</c> on value objects. These run alongside the core generators, because they build on
/// the base types the core generators emit.
/// </summary>
public class EntityFrameworkGeneratorTests
{
    private const string Preamble = "using DDDToolkit.Abstractions.Attributes;\nusing System;\n\nnamespace Sample;\n\n";

    private static GeneratorRunOutcome Run(string source, string module = "Sales")
        => GeneratorTestHost.Create(Preamble + source)
            .WithEntityFramework()
            .WithModule(module)
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

    [Fact]
    public void A_struct_id_gets_a_nested_value_converter()
    {
        var result = Run(
            """
            [EntityId<Guid>("PRD")]
            public readonly partial record struct ProductId;
            """);

        result.ShouldCompile();
        result.ShouldContain(
            "Sample.ProductId.Converter.g.cs",
            "public sealed class ProductIdConverter : global::Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<global::Sample.ProductId, global::System.Guid>");

        // It really is a usable converter: construct it and run both directions.
        var emitted = result.Emit();
        var converter = (Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)emitted.New("Sample.ProductId+ProductIdConverter");
        var guid = Guid.NewGuid();

        converter.ConvertToProvider(emitted.New("Sample.ProductId", guid)).Should().Be(guid);
        converter.ConvertFromProvider(guid).Should().Be(emitted.New("Sample.ProductId", guid));
    }

    [Fact]
    public void An_id_generated_from_the_aggregate_gets_the_same_converter_and_registration()
    {
        // The id has no [EntityId] attribute of its own - it does not exist until a generator writes
        // it - so this only works because every generator reads ids from one shared provider.
        var result = Run(
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }
            }
            """);

        result.ShouldCompile();
        result.ShouldContain(
            "Sample.OrderId.Converter.g.cs",
            "public sealed class OrderIdConverter : global::Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<global::Sample.OrderId, global::System.Guid>");
        result.ShouldContain("ConverterExtensions", "Properties<global::Sample.OrderId>().HaveConversion<global::Sample.OrderId.OrderIdConverter>();");
        result.ShouldContain("ConverterExtensions", "DefaultTypeMapping<global::Sample.OrderId>().HasConversion<global::Sample.OrderId.OrderIdConverter>();");

        var emitted = result.Emit();
        var converter = (Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)emitted.New("Sample.OrderId+OrderIdConverter");
        var guid = Guid.NewGuid();

        converter.ConvertToProvider(emitted.New("Sample.OrderId", guid)).Should().Be(guid);
        converter.ConvertFromProvider(guid).Should().Be(emitted.New("Sample.OrderId", guid));
    }

    [Fact]
    public void A_record_id_gets_a_converter_for_itself_and_for_its_always_valid_twin()
    {
        var result = Run(
            """
            [EntityId<Guid>("USR")]
            public partial record UserId;
            """);

        result.ShouldCompile();
        result.ShouldContain("Sample.UserId.Converter.g.cs", "public sealed class UserIdConverter :");
        result.ShouldContain("Sample.UserId.Converter.g.cs", "public sealed class ValidUserIdConverter :");

        var emitted = result.Emit();
        var converter = (Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)emitted.New("Sample.ValidUserId+ValidUserIdConverter");
        var guid = Guid.NewGuid();

        converter.ConvertToProvider(emitted.New("Sample.ValidUserId", guid)).Should().Be(guid);
        converter.ConvertFromProvider(guid).Should().Be(emitted.New("Sample.ValidUserId", guid));
    }

    [Fact]
    public void A_single_value_object_gets_a_converter_for_itself_and_its_twin()
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
        result.ShouldContain("Sample.EmailAddress.Converter.g.cs", "public sealed class EmailAddressConverter :");
        result.ShouldContain("Sample.EmailAddress.Converter.g.cs", "public sealed class ValidEmailAddressConverter :");

        var emitted = result.Emit();
        var converter = (Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)emitted.New("Sample.EmailAddress+EmailAddressConverter");

        converter.ConvertToProvider(emitted.CallStatic("Sample.EmailAddress", "Create", "ada@example.com")).Should().Be("ada@example.com");
        converter.ConvertFromProvider("ada@example.com").Should().Be(emitted.CallStatic("Sample.EmailAddress", "Create", "ada@example.com"));
    }

    [Fact]
    public void The_registration_method_is_named_after_the_module_and_registers_everything()
    {
        var result = Run(
            """
            [EntityId<Guid>]
            public readonly partial record struct ProductId;

            [EntityId<Guid>]
            public partial record UserId;

            [SingleValueObject<string>]
            public partial record EmailAddress;
            """);

        result.ShouldCompile();
        result.ShouldContain("ConverterExtensions", "public static class ConverterExtensions");
        result.ShouldContain("ConverterExtensions", "AddSalesConverters(this global::Microsoft.EntityFrameworkCore.ModelConfigurationBuilder");
        result.ShouldContain("ConverterExtensions", "Properties<global::Sample.ProductId>().HaveConversion<global::Sample.ProductId.ProductIdConverter>()");
        result.ShouldContain("ConverterExtensions", "Properties<global::Sample.UserId>().HaveConversion<global::Sample.UserId.UserIdConverter>()");
        result.ShouldContain("ConverterExtensions", "Properties<global::Sample.ValidUserId>().HaveConversion<global::Sample.ValidUserId.ValidUserIdConverter>()");
        result.ShouldContain("ConverterExtensions", "Properties<global::Sample.EmailAddress>().HaveConversion<global::Sample.EmailAddress.EmailAddressConverter>()");
        result.ShouldContain("ConverterExtensions", "Properties<global::Sample.ValidEmailAddress>()");
    }

    [Fact]
    public void The_registration_method_is_a_public_static_extension_on_ModelConfigurationBuilder()
    {
        // That it compiles at all is the strong assertion: HaveConversion<TConverter> is constrained to
        // ValueConverter, so a converter the generator got wrong would not type-check against real EF Core.
        var emitted = Run(
            """
            [EntityId<Guid>]
            public readonly partial record struct ProductId;
            """).Emit();

        // The registration class lives in "{assembly name}.Converters", not in the types' own namespace.
        var method = emitted.Type(GeneratorTestHost.DefaultAssemblyName + ".Converters.ConverterExtensions")
            .GetMethod("AddSalesConverters")!;

        method.IsStatic.Should().BeTrue();
        method.IsPublic.Should().BeTrue();
        method.GetParameters().Single().ParameterType.Should().Be<Microsoft.EntityFrameworkCore.ModelConfigurationBuilder>();
        method.ReturnType.Should().Be<Microsoft.EntityFrameworkCore.ModelConfigurationBuilder>();
        method.GetCustomAttributes(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false).Should().NotBeEmpty();
    }

    [Fact]
    public void ColumnLength_becomes_HaveMaxLength()
    {
        var result = Run(
            """
            [SingleValueObject<string>(ColumnLength: 255)]
            public partial record EmailAddress;

            [EntityId<string>(Prefix: "SKU", ColumnLength: 32)]
            public readonly partial record struct Sku;
            """);

        result.ShouldCompile();
        result.ShouldContain("ConverterExtensions", "Properties<global::Sample.EmailAddress>().HaveConversion<global::Sample.EmailAddress.EmailAddressConverter>().HaveMaxLength(255);");
        result.ShouldContain("ConverterExtensions", "Properties<global::Sample.Sku>().HaveConversion<global::Sample.Sku.SkuConverter>().HaveMaxLength(32);");
    }

    [Fact]
    public void Without_ColumnLength_no_maximum_is_imposed()
    {
        var result = Run(
            """
            [SingleValueObject<string>]
            public partial record EmailAddress;
            """);

        result.ShouldCompile();
        result.ShouldNotContain("ConverterExtensions", "HaveMaxLength");
    }

    [Fact]
    public void Nothing_is_registered_when_the_assembly_has_no_ids_or_value_objects()
    {
        var result = Run("public sealed class Nothing;");

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("ConverterExtensions", StringComparison.Ordinal));
    }

    [Fact]
    public void A_child_entity_is_marked_as_an_owned_type()
    {
        var result = Run(
            """
            [EntityId<Guid>]
            public readonly partial record struct OrderId;

            [Entity<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }
            }
            """);

        result.ShouldCompile();
        result.ShouldContain("Sample.Order.EntityFramework.g.cs", "[global::Microsoft.EntityFrameworkCore.Owned]");

        result.Emit().Type("Sample.Order")
            .GetCustomAttributes(typeof(Microsoft.EntityFrameworkCore.OwnedAttribute), inherit: false)
            .Should().HaveCount(1);
    }

    [Fact]
    public void An_aggregate_root_is_not_marked_as_an_owned_type()
    {
        // A root is its own boundary; EF must map it as an entity type of its own.
        var result = Run(
            """
            [EntityId<Guid>]
            public readonly partial record struct BasketId;

            [AggregateRoot<BasketId>]
            public partial class Basket
            {
                public Basket(BasketId id) : base(id) { }
            }
            """);

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName == "Sample.Basket.EntityFramework.g.cs");
        result.Emit().Type("Sample.Basket")
            .GetCustomAttributes(typeof(Microsoft.EntityFrameworkCore.OwnedAttribute), inherit: false)
            .Should().BeEmpty();
    }

    [Fact]
    public void A_value_object_and_its_twin_are_marked_as_complex_types()
    {
        var result = Run(
            """
            [ValueObject]
            public partial record Money
            {
                public decimal Amount { get; protected init; }
            }
            """);

        result.ShouldCompile();
        result.ShouldContain("Sample.Money.EntityFramework.g.cs", "[global::System.ComponentModel.DataAnnotations.Schema.ComplexType]");
        result.ShouldContain("Sample.Money.EntityFramework.g.cs", "partial record ValidMoney");

        var emitted = result.Emit();
        emitted.Type("Sample.Money").GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ComplexTypeAttribute), false).Should().HaveCount(1);
        emitted.Type("Sample.ValidMoney").GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ComplexTypeAttribute), false).Should().HaveCount(1);
    }

    [Fact]
    public void A_type_the_core_generator_refused_gets_no_EF_code_either()
    {
        // The EF generators filter on CanGenerate, so a misapplied attribute does not produce an
        // avalanche of follow-on errors about a converter over a type that was never generated.
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
}
