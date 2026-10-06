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
            Hint.Of("Sample.ProductId", ".Converter"),
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
            Hint.Of("Sample.OrderId", ".Converter"),
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
        result.ShouldContain(Hint.Of("Sample.UserId", ".Converter"), "public sealed class UserIdConverter :");
        result.ShouldContain(Hint.Of("Sample.UserId", ".Converter"), "public sealed class ValidUserIdConverter :");

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
        result.ShouldContain(Hint.Of("Sample.EmailAddress", ".Converter"), "public sealed class EmailAddressConverter :");
        result.ShouldContain(Hint.Of("Sample.EmailAddress", ".Converter"), "public sealed class ValidEmailAddressConverter :");

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
        result.ShouldContain(Hint.Of("Sample.Order", ".EntityFramework"), "[global::Microsoft.EntityFrameworkCore.Owned]");

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
        result.GeneratedSources.Should().NotContain(source => source.HintName == Hint.Of("Sample.Basket", ".EntityFramework"));
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
        result.ShouldContain(Hint.Of("Sample.Money", ".EntityFramework"), "[global::System.ComponentModel.DataAnnotations.Schema.ComplexType]");
        result.ShouldContain(Hint.Of("Sample.Money", ".EntityFramework"), "partial record ValidMoney");

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

    // ------------------------------------------------------------------ a module in layers

    private const string Converter = "global::DDDToolkit.EntityFramework.Storage.SingleValueConverter";

    /// <summary>A module's domain project, without Entity Framework: an id of each kind and a single value object.</summary>
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

        [SingleValueObject<string>(ColumnLength: 80)]
        public partial record EmailAddress;

        [AggregateRoot<string>("INV", ColumnLength: 16)]
        public partial class Invoice
        {
            public Invoice(InvoiceId id) : base(id) { }
        }
        """;

    /// <summary>
    /// A project of the module that holds the context: it references Entity Framework, and declares no ids. It sets
    /// a <c>DDD_Module</c> of its own as well, which names its registration only when it declares no module.
    /// </summary>
    private static GeneratorTestHost Infrastructure(string? module = "Sales", string source = "namespace Sales.Infrastructure;\n\npublic static class Nothing;")
    {
        var host = GeneratorTestHost.Create(source, "Infrastructure.cs").WithAssemblyName("Sales.Infrastructure").WithModule("SalesInfrastructure");
        return module is null ? host : host.WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"" + module + "\")]", "Module.cs");
    }

    private static string Registered(string type, string value, string maxLength = "")
        => "modelConfigurationBuilder.Properties<" + type + ">().HaveConversion<" + Converter + "<" + type + ", " + value + ">>()" + maxLength + ";";

    [Fact]
    public void Ids_of_the_modules_project_without_Entity_Framework_are_registered_with_the_single_value_converter()
    {
        var result = Infrastructure()
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .WithEntityFrameworkRuntime()
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        // That it compiles is the strong half: HaveConversion<SingleValueConverter<T, TValue>> type-checks against
        // EF Core only when T really implements ISingleValue<T, TValue>.
        result.ShouldCompile();
        result.ShouldContain(
            "ConverterExtensions",
            "public static global::Microsoft.EntityFrameworkCore.ModelConfigurationBuilder AddSalesConverters(",
            "the module names the registration, whatever DDD_Module the project sets");
        result.ShouldContain("ConverterExtensions", Registered("global::Sales.Domain.ProductId", "global::System.Guid"));
        result.ShouldContain(
            "ConverterExtensions",
            "modelConfigurationBuilder.DefaultTypeMapping<global::Sales.Domain.ProductId>().HasConversion<" + Converter + "<global::Sales.Domain.ProductId, global::System.Guid>>();");
        result.ShouldContain("ConverterExtensions", Registered("global::Sales.Domain.CustomerId", "global::System.Guid"));
        result.ShouldContain("ConverterExtensions", Registered("global::Sales.Domain.ValidCustomerId", "global::System.Guid"), "a twin is stored as its value too");
        result.ShouldContain("ConverterExtensions", Registered("global::Sales.Domain.EmailAddress", "string", ".HaveMaxLength(80)"), "the column length comes from the attribute");
        result.ShouldContain("ConverterExtensions", Registered("global::Sales.Domain.ValidEmailAddress", "string", ".HaveMaxLength(80)"), "a twin's from its parent's");
        result.ShouldContain("ConverterExtensions", Registered("global::Sales.Domain.InvoiceId", "string", ".HaveMaxLength(16)"), "an implicit id's from its entity's");
    }

    [Fact]
    public void A_project_with_no_ids_of_its_own_still_registers_its_modules()
    {
        var own = Infrastructure()
            .WithEntityFrameworkRuntime()
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());
        own.GeneratedSources.Should().NotContain(source => source.HintName.Contains("ConverterExtensions", StringComparison.Ordinal), "nothing to register is nothing to write");

        var withDomain = Infrastructure()
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .WithEntityFrameworkRuntime()
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());
        withDomain.ShouldCompile();
        withDomain.ShouldHaveGenerated("ConverterExtensions");
    }

    [Fact]
    public void A_published_id_of_another_module_is_registered_and_an_unpublished_one_is_not()
    {
        var result = Infrastructure(source:
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                namespace Sales.Infrastructure;

                [EntityId<Guid>]
                public readonly partial record struct OrderId;
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
            .WithEntityFrameworkRuntime()
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain("ConverterExtensions", "Properties<global::Sales.Infrastructure.OrderId>().HaveConversion<global::Sales.Infrastructure.OrderId.OrderIdConverter>()", "its own ids keep their nested converters");
        result.ShouldContain("ConverterExtensions", Registered("global::Billing.Contracts.InvoiceNumber", "global::System.Guid"));
        result.ShouldContain("ConverterExtensions", Registered("global::Billing.Contracts.PayerId", "global::System.Guid"));
        result.ShouldContain("ConverterExtensions", Registered("global::Billing.Contracts.ValidPayerId", "global::System.Guid"), "the twin of a published id is the published id, validated");
        result.ShouldNotContain("ConverterExtensions", "LedgerLineId", "another module's own ids are its business (DDD00022)");
    }

    [Theory]
    [InlineData("its source")]
    [InlineData("its build")]
    public void The_ids_of_another_modules_contracts_project_are_registered_without_a_ModuleContract_each(string declaredBy)
    {
        // The contracts project says once that every public type of it is published, and keeps an internal id to itself.
        var result = Infrastructure()
            .WithEntityFrameworkRuntime()
            .WithReferencedProject(
                "Billing.Contracts",
                project =>
                {
                    project = project
                        .WithModuleFromTheBuild("Billing")
                        .WithSource(
                            """
                            using System;
                            using DDDToolkit.Abstractions.Attributes;

                            namespace Billing.Contracts;

                            [EntityId<Guid>]
                            public readonly partial record struct InvoiceNumber;

                            [EntityId<Guid>]
                            public partial record PayerId;

                            [EntityId<Guid>]
                            internal readonly partial record struct LedgerLineId;
                            """);

                    return declaredBy == "its source"
                        ? project.WithSource("[assembly: DDDToolkit.Abstractions.Attributes.ModuleContracts]", "AssemblyInfo.cs")
                        : project.WithBuildProperty("DDD_ModuleContracts", "true");
                })
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain("ConverterExtensions", Registered("global::Billing.Contracts.InvoiceNumber", "global::System.Guid"));
        result.ShouldContain("ConverterExtensions", Registered("global::Billing.Contracts.PayerId", "global::System.Guid"));
        result.ShouldContain("ConverterExtensions", Registered("global::Billing.Contracts.ValidPayerId", "global::System.Guid"), "the twin of a published id is the published id, validated");
        result.ShouldNotContain("ConverterExtensions", "LedgerLineId", "an internal id is the contracts project's own");
    }

    [Fact]
    public void The_ids_of_a_project_named_Contracts_that_says_nothing_are_not_registered()
    {
        var result = Infrastructure()
            .WithEntityFrameworkRuntime()
            .WithReferencedProject(
                "Billing.Contracts",
                project => project
                    .WithModuleFromTheBuild("Billing")
                    .WithSource(
                        """
                        using System;
                        using DDDToolkit.Abstractions.Attributes;

                        namespace Billing.Contracts;

                        [EntityId<Guid>]
                        public readonly partial record struct InvoiceNumber;
                        """))
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldNotContain("ConverterExtensions", "InvoiceNumber", "a project is never its module's contracts because of its name");
    }

    [Fact]
    public void An_id_whose_assembly_has_its_own_converter_is_left_to_it()
    {
        // The contracts project references Entity Framework, as it used to have to: it registers its ids itself.
        var result = Infrastructure()
            .WithEntityFrameworkRuntime()
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
                        """),
                GeneratorTestHost.EntityFrameworkGenerators())
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain("ConverterExtensions", Registered("global::Sales.Domain.ProductId", "global::System.Guid"));
        result.ShouldNotContain("ConverterExtensions", "CustomerNumber", "the contracts' own AddSalesConverters() registers it with its nested converter");
        result.ShouldContain(
            "ConverterExtensions",
            "global::Sales.Contracts.Converters.ConverterExtensions.AddSalesConverters(modelConfigurationBuilder);",
            "and this project's registration calls that one, so the context makes one call for the module");
    }

    [Fact]
    public void A_published_id_of_another_module_whose_project_references_entity_framework_is_registered_with_its_own_converter()
    {
        // The natural small layout: the other module declares its ids in a project that holds its context as well,
        // so they have converters of their own. This module stores one of them, and its one call registers it.
        var result = Infrastructure()
            .WithEntityFrameworkRuntime()
            .WithReferencedProject(
                "Billing",
                project => project
                    .WithModule("Billing")
                    .WithSource(
                        """
                        using System;
                        using DDDToolkit.Abstractions.Attributes;

                        [assembly: Module("Billing")]

                        namespace Billing;

                        [ModuleContract]
                        [EntityId<Guid>]
                        public readonly partial record struct InvoiceNumber;

                        [ModuleContract]
                        [EntityId<Guid>]
                        public partial record PayerId;

                        [EntityId<Guid>]
                        public readonly partial record struct LedgerLineId;
                        """),
                GeneratorTestHost.EntityFrameworkGenerators())
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "ConverterExtensions",
            "modelConfigurationBuilder.Properties<global::Billing.InvoiceNumber>().HaveConversion<global::Billing.InvoiceNumber.InvoiceNumberConverter>();");
        result.ShouldContain(
            "ConverterExtensions",
            "modelConfigurationBuilder.DefaultTypeMapping<global::Billing.InvoiceNumber>().HasConversion<global::Billing.InvoiceNumber.InvoiceNumberConverter>();");
        result.ShouldContain("ConverterExtensions", "Properties<global::Billing.PayerId>().HaveConversion<global::Billing.PayerId.PayerIdConverter>()");
        result.ShouldContain("ConverterExtensions", "Properties<global::Billing.ValidPayerId>().HaveConversion<global::Billing.ValidPayerId.ValidPayerIdConverter>()", "the twin of a published id is published too");
        result.ShouldNotContain("ConverterExtensions", "LedgerLineId", "another module's own ids are its business (DDD00022)");
        result.ShouldNotContain("ConverterExtensions", "AddBillingConverters", "another module's registration is not this one's to call");
        result.ShouldContain("ConverterExtensions", Registered("global::Sales.Domain.ProductId", "global::System.Guid"));
    }

    [Fact]
    public void A_published_id_whose_class_of_that_name_is_no_value_converter_is_not_registered_with_it()
    {
        // A class the application nested in its id under the name the generated converter has, for binding a route
        // value say, is no converter a registration could name: the id is left alone, as an id that has a converter
        // of its own the registration cannot name is, and the registration compiles.
        var result = Infrastructure()
            .WithEntityFrameworkRuntime()
            .WithReferencedProject(
                "Billing",
                project => project
                    .WithModule("Billing")
                    .WithSource(
                        """
                        using System;
                        using DDDToolkit.Abstractions.Attributes;

                        [assembly: Module("Billing")]

                        namespace Billing;

                        [ModuleContract]
                        [EntityId<Guid>]
                        public readonly partial record struct InvoiceNumber
                        {
                            public sealed class InvoiceNumberConverter : System.ComponentModel.TypeConverter;
                        }

                        [ModuleContract]
                        [EntityId<Guid>]
                        public readonly partial record struct PayerNumber;
                        """))
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldNotContain("ConverterExtensions", "InvoiceNumber");
        result.ShouldContain("ConverterExtensions", Registered("global::Billing.PayerNumber", "global::System.Guid"));
    }

    /// <summary>
    /// A second project of the module with Entity Framework, above the one that holds the context: the migrations
    /// for another database, say. It references the infrastructure project and, through it, the domain project.
    /// </summary>
    private static GeneratorTestHost AboveInfrastructure(string source)
        => GeneratorTestHost.Create(source, "Postgres.cs")
            .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Sales\")]", "Module.cs")
            .WithAssemblyName("Sales.Postgres")
            .WithEntityFrameworkRuntime()
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .WithReferencedProject(
                "Sales.Infrastructure",
                project => project
                    .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Sales\")]", "Module.cs")
                    .WithSource(
                        """
                        using System;
                        using DDDToolkit.Abstractions.Attributes;

                        namespace Sales.Infrastructure;

                        [EntityId<Guid>]
                        public readonly partial record struct OutboxCursorId;
                        """,
                        "Infrastructure.cs"),
                GeneratorTestHost.EntityFrameworkGenerators());

    [Fact]
    public void What_a_registration_this_one_calls_has_registered_is_not_registered_again()
    {
        // Both projects reference the domain project, both are Sales and both run the generator. The infrastructure
        // project registered the domain's ids; this one calls it, so it leaves them to it.
        var result = AboveInfrastructure(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                namespace Sales.Postgres;

                [EntityId<Guid>]
                public readonly partial record struct MigrationRunId;
                """)
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "ConverterExtensions",
            "global::Sales.Infrastructure.Converters.ConverterExtensions.AddSalesConverters(modelConfigurationBuilder);");
        result.ShouldContain("ConverterExtensions", "Properties<global::Sales.Postgres.MigrationRunId>()", "its own id is its own to register");
        result.ShouldNotContain("ConverterExtensions", "Sales.Domain", "the registration it calls has every id of the domain project");
        result.ShouldNotContain("ConverterExtensions", Converter);
    }

    [Fact]
    public void A_project_whose_module_is_registered_by_the_one_it_references_gets_no_registration_of_its_own()
    {
        // Nothing is left for it to register, so nothing is written: its context calls the infrastructure
        // project's AddSalesConverters(), the only one there is to import.
        var result = AboveInfrastructure(
                """
                using Microsoft.EntityFrameworkCore;
                using Sales.Infrastructure.Converters;

                namespace Sales.Postgres;

                public sealed class SalesContext : DbContext
                {
                    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                        => configurationBuilder.AddSalesConverters();
                }
                """)
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("ConverterExtensions", StringComparison.Ordinal));
    }

    [Fact]
    public void What_the_called_registration_could_not_see_is_still_registered_here()
    {
        // The contracts project is referenced by this project and not by the infrastructure project, so the
        // registration this one calls knows nothing of its ids.
        var result = AboveInfrastructure("namespace Sales.Postgres;\n\npublic static class Nothing;")
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
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain("ConverterExtensions", Registered("global::Sales.Contracts.CustomerNumber", "global::System.Guid"));
        result.ShouldContain(
            "ConverterExtensions",
            "global::Sales.Infrastructure.Converters.ConverterExtensions.AddSalesConverters(modelConfigurationBuilder);");
        result.ShouldNotContain("ConverterExtensions", "Sales.Domain", "those the called registration has");
    }

    [Fact]
    public void A_referenced_package_without_a_module_is_ignored()
    {
        // The Tenancy package declares TenantSlug, a single value object it maps itself; a shared kernel without a
        // module is no module's to register.
        var result = Infrastructure(source:
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                namespace Sales.Infrastructure;

                [EntityId<Guid>]
                public readonly partial record struct OrderId;
                """)
            .WithTenancy()
            .WithReferencedAssembly(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                namespace Shared.Kernel;

                [EntityId<Guid>]
                public readonly partial record struct CorrelationId;
                """,
                "Shared.Kernel")
            .WithEntityFrameworkRuntime()
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain("ConverterExtensions", "Sales.Infrastructure.OrderId");
        result.ShouldNotContain("ConverterExtensions", "TenantSlug");
        result.ShouldNotContain("ConverterExtensions", "CorrelationId");
        result.ShouldNotContain("ConverterExtensions", Converter);
    }

    [Fact]
    public void A_project_without_a_module_registers_only_its_own()
    {
        var result = Infrastructure(module: null, source:
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;

                namespace Sales.Host;

                [EntityId<Guid>]
                public readonly partial record struct RequestId;
                """)
            .WithReferencedAssembly(SalesDomain, "Sales.Domain")
            .WithEntityFrameworkRuntime()
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain("ConverterExtensions", "Sales.Host.RequestId");
        result.ShouldNotContain("ConverterExtensions", "Sales.Domain", "a project without a module is no module's");
    }
}
