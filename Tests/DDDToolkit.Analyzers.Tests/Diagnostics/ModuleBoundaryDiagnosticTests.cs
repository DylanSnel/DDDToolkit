namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// The module boundary analyzer: DDD00022 (naming a type another module does not publish) and DDD00023
/// (an entity of this module storing an entity of another one).
/// <para>
/// Every test compiles a second assembly for the other module, because that is the only shape the rule
/// supports: one assembly is one module, and the analyzer reads the other module's declaration out of
/// metadata. The negative cases matter at least as much as the positive ones here — a boundary rule that
/// fires on correct code gets switched off on the first afternoon.
/// </para>
/// </summary>
public class ModuleBoundaryDiagnosticTests
{
    /// <summary>The other module: one published id, one published integration event, and two types it keeps to itself.</summary>
    private const string CrmModule = """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        [assembly: Module("Crm")]

        namespace Crm;

        [ModuleContract]
        [EntityId<Guid>]
        public readonly partial record struct CustomerId;

        [ModuleContract]
        public sealed class CustomerSummary
        {
            public string Name { get; init; } = string.Empty;
        }

        [IntegrationEvent("crm.customer-registered")]
        public sealed record CustomerRegistered(Guid CustomerId, string Name);

        [AggregateRoot<CustomerId>]
        public partial class Customer
        {
            public string Name { get; private set; } = string.Empty;

            public static Customer Create() => new();
        }

        [Entity<Guid>]
        public partial class CustomerAddress
        {
            public string City { get; private set; } = string.Empty;
        }

        public sealed class CustomerLookup
        {
            public static Customer? Find(CustomerId id) => null;
        }

        [AttributeUsage(AttributeTargets.Class)]
        public sealed class AuditedAttribute : Attribute;
        """;

    /// <summary>The same types again, in an assembly that never says it is a module.</summary>
    private const string PlainLibrary = """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        namespace Crm;

        [EntityId<Guid>]
        public readonly partial record struct CustomerId;

        [AggregateRoot<CustomerId>]
        public partial class Customer
        {
            public string Name { get; private set; } = string.Empty;
        }

        public sealed class CustomerLookup
        {
            public static Customer? Find(CustomerId id) => null;
        }
        """;

    // ------------------------------------------------------------------ DDD00022

    [Fact]
    public void Naming_a_type_the_other_module_does_not_publish_reports()
    {
        var result = Sales("""
            public sealed class OrderReport
            {
                public string Describe(Customer customer) => customer.Name;
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveDiagnostic("DDD00022", at: "Customer");
    }

    [Fact]
    public void The_message_names_both_modules_and_the_way_out()
    {
        var result = Sales("""
            public sealed class OrderReport
            {
                public string Describe(Customer customer) => customer.Name;
            }
            """);

        var diagnostic = result.ShouldHaveDiagnostic("DDD00022", at: "Customer");

        diagnostic.GetMessage().Should().Contain("Crm.Customer").And.Contain("'Crm'").And.Contain("'Sales'");
        diagnostic.GetMessage().Should().Contain("[ModuleContract]");
        diagnostic.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
    }

    [Fact]
    public void A_published_type_does_not_report()
    {
        var result = Sales("""
            public sealed class OrderReport
            {
                public string Describe(CustomerSummary summary) => summary.Name;
            }
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
    }

    [Fact]
    public void An_integration_event_is_published_without_a_second_attribute()
    {
        var result = Sales("""
            public sealed class OrderReactions
            {
                public void On(CustomerRegistered registered) { }
            }
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
    }

    [Fact]
    public void A_published_identifier_can_be_held()
    {
        var result = Sales("""
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public CustomerId Buyer { get; private set; }
            }
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
        result.ShouldNotHaveDiagnostic("DDD00023");
    }

    [Fact]
    public void A_static_call_on_an_unpublished_type_reports_at_the_type_name()
    {
        var result = Sales("""
            public sealed class OrderReport
            {
                public object? Find(CustomerId id) => CustomerLookup.Find(id);
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveDiagnostic("DDD00022", at: "CustomerLookup");
        result.Count("DDD00022").Should().Be(1, "the method name is not a second violation");
    }

    [Fact]
    public void Creating_an_unpublished_type_reports()
    {
        var result = Sales("""
            public sealed class OrderReport
            {
                public object Make() => Customer.Create();
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveDiagnostic("DDD00022", at: "Customer");
    }

    [Fact]
    public void An_unpublished_type_inside_a_generic_argument_reports()
    {
        var result = Sales("""
            using System.Collections.Generic;

            public sealed class OrderReport
            {
                public List<Customer> Buyers { get; } = [];
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveDiagnostic("DDD00022", at: "Customer");
    }

    [Fact]
    public void An_unpublished_attribute_reports()
    {
        var result = Sales("""
            [Audited]
            public sealed class OrderReport;
            """);

        result.ShouldCompile();
        result.ShouldHaveDiagnostic("DDD00022", at: "Audited");
    }

    [Fact]
    public void A_using_alias_for_an_unpublished_type_reports()
    {
        var result = Sales(
            """
            using DDDToolkit.Abstractions.Attributes;
            using Crm;
            using Buyer = Crm.Customer;

            [assembly: Module("Sales")]

            namespace Sales;

            public sealed class OrderReport
            {
                public string Describe(Buyer buyer) => buyer.Name;
            }
            """,
            wrapped: false);

        result.ShouldCompile();
        result.ShouldHaveDiagnostic("DDD00022", at: "Customer");
    }

    [Fact]
    public void A_pragma_suppresses_one_reference()
    {
        var result = Sales("""
            public sealed class OrderReport
            {
            #pragma warning disable DDD00022
                public string Describe(Customer customer) => customer.Name;
            #pragma warning restore DDD00022
            }
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
    }

    [Fact]
    public void SuppressMessage_on_the_member_suppresses_it()
    {
        var result = Sales("""
            public sealed class OrderReport
            {
                [System.Diagnostics.CodeAnalysis.SuppressMessage("DDDToolkit.Modules", "DDD00022", Justification = "Deliberate.")]
                public string Describe(Customer customer) => customer.Name;
            }
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
    }

    [Fact]
    public void Types_of_this_module_never_report()
    {
        var result = Sales("""
            public sealed class OrderReport
            {
                public OrderLine? Line { get; set; }
            }

            public sealed class OrderLine
            {
                public string Sku { get; init; } = string.Empty;
            }
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
    }

    [Fact]
    public void Two_assemblies_with_the_same_module_name_are_one_module()
    {
        var result = GeneratorTestHost
            .Create("""
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using Crm;

                [assembly: Module("Crm")]

                namespace Crm.Reporting;

                public sealed class CustomerReport
                {
                    public string Describe(Customer customer) => customer.Name;
                }
                """)
            .WithReferencedAssembly(CrmModule, "Crm.Domain")
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
    }

    [Fact]
    public void An_assembly_that_is_not_a_module_is_never_reported_against()
    {
        var result = GeneratorTestHost
            .Create("""
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using Crm;

                [assembly: Module("Sales")]

                namespace Sales;

                public sealed class OrderReport
                {
                    public object? Find(CustomerId id) => CustomerLookup.Find(id);
                }
                """)
            .WithReferencedAssembly(PlainLibrary, "Crm.Plain")
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
    }

    [Fact]
    public void An_assembly_that_declares_no_module_reports_nothing()
    {
        var result = GeneratorTestHost
            .Create("""
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using Crm;

                namespace Sales;

                [AggregateRoot<Guid>("ORD")]
                public partial class Order
                {
                    public Customer Buyer { get; private set; } = Customer.Create();
                }
                """)
            .WithReferencedAssembly(CrmModule, "Crm.Domain")
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .WithNoWarn("DDD00021")
            .RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
        result.ShouldNotHaveDiagnostic("DDD00023");
    }

    [Fact]
    public void The_framework_and_the_toolkit_are_not_modules()
    {
        var result = Sales("""
            using System.Text;
            using DDDToolkit.BaseTypes;

            public sealed class OrderReport
            {
                public string Describe(StringBuilder builder, ValueObject value) => builder.ToString() + value;
            }
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
    }

    // ------------------------------------------------------------------ DDD00023

    [Fact]
    public void An_aggregate_holding_another_modules_aggregate_reports()
    {
        var result = Sales("""
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Customer Buyer { get; private set; } = Customer.Create();
            }
            """);

        result.ShouldCompile();
        var diagnostic = result.ShouldHaveDiagnostic("DDD00023", at: "Buyer");
        diagnostic.GetMessage().Should().Contain("Order.Buyer").And.Contain("Customer").And.Contain("'Crm'");
    }

    [Fact]
    public void Publishing_the_entity_does_not_make_it_holdable()
    {
        var result = GeneratorTestHost
            .Create("""
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using Crm;

                [assembly: Module("Sales")]

                namespace Sales;

                [AggregateRoot<Guid>("ORD")]
                public partial class Order
                {
                    public Customer? Buyer { get; private set; }
                }
                """)
            .WithReferencedAssembly(
                CrmModule.Replace("[AggregateRoot<CustomerId>]", "[ModuleContract]\n[AggregateRoot<CustomerId>]", StringComparison.Ordinal),
                "Crm.Domain")
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .WithNoWarn("DDD00021")
            .RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
        result.ShouldHaveDiagnostic("DDD00023", at: "Buyer");
    }

    [Fact]
    public void An_aggregate_holding_another_modules_child_entity_reports()
    {
        var result = Sales("""
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public CustomerAddress? ShipTo { get; private set; }
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveDiagnostic("DDD00023", at: "ShipTo");
    }

    [Fact]
    public void A_collection_of_another_modules_entity_reports_once()
    {
        var result = Sales("""
            using System.Collections.Generic;

            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public partial IReadOnlyList<Customer> Watchers { get; }
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveDiagnostic("DDD00023", at: "Watchers");
        result.Count("DDD00023").Should().Be(1, "the generated backing field is not a second violation");
    }

    [Fact]
    public void A_method_parameter_or_return_type_does_not_report()
    {
        var result = GeneratorTestHost
            .Create("""
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using Crm;

                [assembly: Module("Sales")]

                namespace Sales;

                [AggregateRoot<Guid>("ORD")]
                public partial class Order
                {
                    public CustomerId Buyer { get; private set; }

                    public void PlaceFor(Customer customer) => Buyer = customer.Id;

                    public Customer? Rehydrate() => null;
                }
                """)
            .WithReferencedAssembly(CrmModule, "Crm.Domain")
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .WithNoWarn("DDD00022")
            .RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00023");
    }

    [Fact]
    public void A_static_field_does_not_report()
    {
        var result = GeneratorTestHost
            .Create("""
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using Crm;

                [assembly: Module("Sales")]

                namespace Sales;

                [AggregateRoot<Guid>("ORD")]
                public partial class Order
                {
                    private static Customer? _cache;

                    public Guid Touch() => _cache is null ? Guid.Empty : Guid.NewGuid();
                }
                """)
            .WithReferencedAssembly(CrmModule, "Crm.Domain")
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .WithNoWarn("DDD00022")
            .RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00023");
    }

    [Fact]
    public void An_entity_of_this_module_does_not_report()
    {
        var result = Sales("""
            using System.Collections.Generic;

            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public partial IReadOnlyList<OrderLine> Lines { get; }
            }

            [Entity<Guid>]
            public partial class OrderLine
            {
                public string Sku { get; private set; } = string.Empty;
            }
            """);

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00023");
        result.ShouldNotHaveDiagnostic("DDD00022");
    }

    [Fact]
    public void A_plain_class_holding_another_modules_entity_is_left_to_DDD00022()
    {
        var result = Sales("""
            public sealed class OrderProjection
            {
                public Customer? Buyer { get; set; }
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveDiagnostic("DDD00022", at: "Customer");
        result.ShouldNotHaveDiagnostic("DDD00023");
    }

    [Fact]
    public void An_entity_of_an_assembly_that_is_not_a_module_does_not_report()
    {
        var result = GeneratorTestHost
            .Create("""
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using Crm;

                [assembly: Module("Sales")]

                namespace Sales;

                [AggregateRoot<Guid>("ORD")]
                public partial class Order
                {
                    public Customer? Buyer { get; private set; }
                }
                """)
            .WithReferencedAssembly(PlainLibrary, "Crm.Plain")
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .WithNoWarn("DDD00021")
            .RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00023");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Compiles <paramref name="body"/> as module "Sales" against module "Crm", with the module analyzer
    /// running over the generated output the way the compiler runs it. DDD00021 is switched off because
    /// several of these snippets hold another aggregate on purpose and that rule has its own tests.
    /// </summary>
    private static GeneratorRunOutcome Sales(string body, bool wrapped = true)
    {
        var source = wrapped
            ? $$"""
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using Crm;

                [assembly: Module("Sales")]

                namespace Sales;

                {{body}}
                """
            : body;

        return GeneratorTestHost
            .Create(source)
            .WithReferencedAssembly(CrmModule, "Crm.Domain")
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .WithNoWarn("DDD00021")
            .RunCore();
    }
}
