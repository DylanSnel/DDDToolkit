namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// The name in <c>Add{Module}Converters</c>, <c>Add{Module}IntegrationEvents</c> and
/// <c>Add{Module}GraphQlRuntimeBindings</c>: the module the assembly declares with
/// <c>[assembly: Module]</c>, otherwise <c>DDD_Module</c>, otherwise the assembly name. The attribute
/// always wins, so a <c>DDD_Module</c> set for a whole folder is a default an assembly can still override.
/// <para>
/// Two assemblies of one module therefore name their methods the same, and the second half of these
/// tests is what keeps that from colliding: an assembly's method calls the ones of the module's other
/// assemblies it references, so a context or a schema makes one call for the module.
/// </para>
/// </summary>
public class RegistrationNameTests
{
    private const string Converters = "ConverterExtensions";
    private const string Bindings = "BindingExtensions";

    private const string SalesIds =
        """
        using DDDToolkit.Abstractions.Attributes;

        [assembly: Module("Sales")]

        namespace Sales;

        [EntityId<System.Guid>]
        public readonly partial record struct OrderLineId;
        """;

    private const string SalesContracts =
        """
        using DDDToolkit.Abstractions.Attributes;

        [assembly: Module("Sales")]

        namespace Sales.Contracts;

        [EntityId<System.Guid>]
        public readonly partial record struct OrderId;
        """;

    private static GeneratorRunOutcome RunEntityFramework(GeneratorTestHost host)
        => host.RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

    // ------------------------------------------------------------------ where the name comes from

    [Fact]
    public void The_module_names_the_converters_without_any_property()
    {
        var result = RunEntityFramework(GeneratorTestHost.Create(SalesIds).WithAssemblyName("Acme.Sales.Domain").WithEntityFramework());

        result.ShouldCompile();
        result.ShouldContain(Converters, "namespace Acme.Sales.Domain.Converters;", "the namespace is still the assembly's");
        result.ShouldContain(Converters, " AddSalesConverters(");
    }

    [Fact]
    public void The_module_wins_over_DDD_Module()
    {
        // DDD_Module can come from a Directory.Build.props that covers many projects. The attribute is what
        // one assembly says about itself, so it is the one that counts.
        var result = RunEntityFramework(GeneratorTestHost.Create(SalesIds).WithEntityFramework().WithModule("Shop"));

        result.ShouldCompile();
        result.ShouldContain(Converters, " AddSalesConverters(");
        result.ShouldNotContain(Converters, "AddShopConverters");
    }

    [Fact]
    public void Without_a_module_DDD_Module_names_them()
    {
        var result = RunEntityFramework(GeneratorTestHost.Create(
                """
                using DDDToolkit.Abstractions.Attributes;

                namespace Shop;

                [EntityId<System.Guid>]
                public readonly partial record struct ProductId;
                """)
            .WithEntityFramework()
            .WithModule("Shop"));

        result.ShouldCompile();
        result.ShouldContain(Converters, " AddShopConverters(");
    }

    [Theory]
    [InlineData("order-management", "AddOrderManagementConverters")]
    [InlineData("sales", "AddSalesConverters")]
    [InlineData("3d printing", "Add_3dPrintingConverters")]
    public void A_module_name_that_is_not_an_identifier_is_spelled_as_one(string module, string method)
    {
        var result = RunEntityFramework(GeneratorTestHost.Create(SalesIds.Replace("Module(\"Sales\")", $"Module(\"{module}\")")).WithEntityFramework());

        result.ShouldCompile();
        result.ShouldContain(Converters, " " + method + "(", "the same spelling {Module}EventNames uses");
    }

    [Fact]
    public void The_module_names_the_GraphQL_bindings_and_wins_over_DDD_Module()
    {
        var result = GeneratorTestHost.Create(SalesIds)
            .WithHotChocolate()
            .WithModule("Shop")
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain(Bindings, " AddSalesGraphQlRuntimeBindings(");
        result.ShouldNotContain(Bindings, "AddShopGraphQlRuntimeBindings");
    }

    [Fact]
    public void The_module_names_the_integration_event_registration_and_wins_over_DDD_Module()
    {
        var result = RunEntityFramework(GeneratorTestHost.Create(
                """
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.BaseTypes;

                [assembly: Module("Sales")]

                namespace Sales;

                public sealed record OrderPlaced(string OrderId) : DomainEvent;
                """)
            .WithEntityFrameworkRuntime()
            .WithModule("Shop"));

        result.ShouldCompile();
        result.ShouldContain("IntegrationEventExtensions", " AddSalesIntegrationEvents(");
        result.ShouldNotContain("IntegrationEventExtensions", "AddShopIntegrationEvents");
    }

    // ------------------------------------------------------------------ two assemblies, one module

    [Fact]
    public void An_assemblys_converters_call_those_of_the_modules_other_assemblies_it_references()
    {
        var result = RunEntityFramework(GeneratorTestHost.Create(SalesIds)
            .WithAssemblyName("Sales")
            .WithEntityFramework()
            .WithReferencedAssembly(SalesContracts, "Sales.Contracts", GeneratorTestHost.EntityFrameworkGenerators()));

        result.ShouldCompile();
        result.ShouldContain(
            Converters,
            "global::Sales.Contracts.Converters.ConverterExtensions.AddSalesConverters(modelConfigurationBuilder);",
            "both assemblies are Sales, so both methods are AddSalesConverters, and the context makes one call");
        result.ShouldContain(Converters, "modelConfigurationBuilder.Properties<global::Sales.OrderLineId>()", "its own converters are still registered");
    }

    [Fact]
    public void One_call_then_compiles_where_importing_both_classes_would_be_ambiguous()
    {
        var result = RunEntityFramework(GeneratorTestHost.Create(SalesIds)
            .WithSource(
                """
                using Microsoft.EntityFrameworkCore;
                using Sales.Converters;

                namespace Sales.Persistence;

                public sealed class SalesContext : DbContext
                {
                    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                        => configurationBuilder.AddSalesConverters();
                }
                """,
                "SalesContext.cs")
            .WithAssemblyName("Sales")
            .WithEntityFramework()
            .WithReferencedAssembly(SalesContracts, "Sales.Contracts", GeneratorTestHost.EntityFrameworkGenerators()));

        result.ShouldCompile();
    }

    [Fact]
    public void An_assemblys_GraphQL_bindings_call_those_of_the_modules_other_assemblies_it_references()
    {
        var result = GeneratorTestHost.Create(SalesIds)
            .WithAssemblyName("Sales")
            .WithHotChocolate()
            .WithReferencedAssembly(SalesContracts, "Sales.Contracts", GeneratorTestHost.HotChocolateGenerators())
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        result.ShouldCompile();
        result.ShouldContain(Bindings, "global::Sales.Contracts.GraphQl.HotChocolateExtensions.AddSalesGraphQlRuntimeBindings(builder);");
    }

    [Fact]
    public void Another_modules_assembly_is_not_called()
    {
        // Shipping stores Sales' OrderId, so its context calls Sales' registration itself, by its own name.
        var result = RunEntityFramework(GeneratorTestHost.Create(SalesIds.Replace("Module(\"Sales\")", "Module(\"Shipping\")"))
            .WithAssemblyName("Shipping")
            .WithEntityFramework()
            .WithReferencedAssembly(SalesContracts, "Sales.Contracts", GeneratorTestHost.EntityFrameworkGenerators()));

        result.ShouldCompile();
        result.ShouldContain(Converters, " AddShippingConverters(");
        result.ShouldNotContain(Converters, "Sales.Contracts.Converters", "another module's converters are that module's to offer, not this one's to register unasked");
    }

    [Fact]
    public void An_assembly_that_is_no_module_calls_nobody()
    {
        var result = RunEntityFramework(GeneratorTestHost.Create(
                """
                using DDDToolkit.Abstractions.Attributes;

                namespace Host;

                [EntityId<System.Guid>]
                public readonly partial record struct TenantId;
                """)
            .WithAssemblyName("Host")
            .WithEntityFramework()
            .WithReferencedAssembly(SalesContracts, "Sales.Contracts", GeneratorTestHost.EntityFrameworkGenerators()));

        result.ShouldCompile();
        result.ShouldNotContain(Converters, "Sales.Contracts.Converters");
    }

    [Fact]
    public void A_referenced_registration_is_found_by_its_shape_whatever_it_is_called()
    {
        // An assembly of the same module built by an earlier version named its method after DDD_Module.
        const string builtEarlier =
            """
            using DDDToolkit.Abstractions.Attributes;
            using Microsoft.EntityFrameworkCore;

            [assembly: Module("Sales")]

            namespace Sales.Contracts.Converters;

            public static class ConverterExtensions
            {
                public static ModelConfigurationBuilder AddSalesContractsConverters(this ModelConfigurationBuilder builder) => builder;

                public static string Describe(this ModelConfigurationBuilder builder) => "not a registration";
            }
            """;

        var result = RunEntityFramework(GeneratorTestHost.Create(SalesIds)
            .WithAssemblyName("Sales")
            .WithEntityFramework()
            .WithReferencedAssembly(builtEarlier, "Sales.Contracts"));

        result.ShouldCompile();
        result.ShouldContain(Converters, "global::Sales.Contracts.Converters.ConverterExtensions.AddSalesContractsConverters(modelConfigurationBuilder);");
        result.ShouldNotContain(Converters, ".Describe(");
    }
}
