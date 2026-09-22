using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00021: an entity or aggregate root that holds another aggregate root instead of its id.
/// <para>
/// The rule is only worth having if it stays quiet on correct code, so most of the tests below are
/// negative: a child entity of the same aggregate, a back-navigation to the owning root, an id, a method
/// that takes another root as an argument. Each of those is normal and none of them may report.
/// </para>
/// </summary>
public class AggregateBoundaryDiagnosticTests
{
    private const string Preamble =
        """
        using DDDToolkit.Abstractions.Attributes;
        using System;
        using System.Collections.Generic;

        namespace Sample;

        [EntityId<Guid>("CUS")]
        public readonly partial record struct CustomerId;

        [AggregateRoot<CustomerId>]
        public partial class Customer
        {
            public Customer(CustomerId id) : base(id) { }

            public string Name { get; private set; } = "";
        }


        """;

    // ------------------------------------------------------------------ what the rule catches

    [Fact]
    public void A_property_typed_as_another_aggregate_root_reports_DDD00021()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public Customer Buyer { get; private set; } = default!;
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "Buyer");
    }

    [Fact]
    public void The_message_names_the_id_to_hold_instead()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public Customer Buyer { get; private set; } = default!;
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00021", at: "Buyer");

        diagnostic.GetMessage().Should().Contain("Order.Buyer").And.Contain("Customer").And.Contain("CustomerId");
    }

    [Fact]
    public void The_message_names_the_generated_id_when_the_other_root_declares_none()
    {
        // Warehouse asks the toolkit to generate its id, so the id to hold is WarehouseId even though no
        // WarehouseId declaration exists anywhere in the source.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("WH")]
            public partial class Warehouse
            {
                public Warehouse(WarehouseId id) : base(id) { }
            }

            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public Warehouse ShipsFrom { get; private set; } = default!;
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "ShipsFrom")
            .GetMessage().Should().Contain("WarehouseId");
    }

    [Fact]
    public void A_field_typed_as_another_aggregate_root_reports_DDD00021()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                private readonly Customer _buyer = default!;

                public string BuyerName => _buyer.Name;
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "_buyer");
    }

    [Fact]
    public void A_collection_of_another_aggregate_root_reports_DDD00021()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public partial IReadOnlyList<Customer> Watchers { get; }
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "Watchers");
    }

    [Fact]
    public void An_array_of_another_aggregate_root_reports_DDD00021()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public Customer[] Watchers { get; private set; } = [];
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "Watchers");
    }

    [Fact]
    public void A_dictionary_valued_by_another_aggregate_root_reports_DDD00021()
    {
        // The root hides behind a second type argument, which a "one type argument" unwrap would miss.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                private readonly Dictionary<string, Customer> _byRole = new();
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "_byRole");
    }

    [Fact]
    public void A_nullable_reference_to_another_aggregate_root_reports_DDD00021()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public Customer? Buyer { get; private set; }
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "Buyer");
    }

    [Fact]
    public void A_root_holding_another_instance_of_its_own_type_reports_DDD00021()
    {
        // Employee.Manager is a different aggregate of the same type: a different row, loaded and saved
        // on its own. The rule is about instances, not about type names.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("EMP")]
            public partial class Employee
            {
                public Employee(EmployeeId id) : base(id) { }

                public Employee? Manager { get; private set; }
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "Manager");
    }

    [Fact]
    public void Two_roots_pointing_at_each_other_are_both_reported()
    {
        // A mutual reference is not an inverse navigation: neither root is inside the other.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public Invoice Invoice { get; private set; } = default!;
            }

            [AggregateRoot<Guid>("INV")]
            public partial class Invoice
            {
                public Invoice(InvoiceId id) : base(id) { }

                public Order Order { get; private set; } = default!;
            }
            """).RunCore();

        result.GeneratorDiagnostics.Count(diagnostic => diagnostic.Id == "DDD00021")
            .Should().Be(2, "neither root owns the other");
    }

    [Fact]
    public void A_child_entity_pointing_at_a_root_that_does_not_own_it_reports_DDD00021()
    {
        // The back-navigation exemption is not "any entity may hold any root": the root has to hold this
        // entity back. Warehouse does not, so OrderLine is reaching across a boundary.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("WH")]
            public partial class Warehouse
            {
                public Warehouse(WarehouseId id) : base(id) { }
            }

            [Entity<Guid>("LINE")]
            public partial class OrderLine
            {
                public OrderLine(OrderLineId id) : base(id) { }

                public Warehouse ShipsFrom { get; private set; } = default!;
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "ShipsFrom");
    }

    [Fact]
    public void A_child_entity_holding_many_of_its_owning_root_reports_DDD00021()
    {
        // An inverse navigation is single valued: a child belongs to one aggregate. A collection of the
        // owner is something else, so the exemption does not cover it.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public partial IReadOnlyList<OrderLine> Lines { get; }
            }

            [Entity<Guid>("LINE")]
            public partial class OrderLine
            {
                public OrderLine(OrderLineId id) : base(id) { }

                public partial IReadOnlyList<Order> Orders { get; }
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "Orders");
    }

    // ------------------------------------------------------------------ what the rule must stay quiet about

    [Fact]
    public void A_child_entity_of_the_same_aggregate_is_not_reported()
    {
        // The ordinary case: a root owning its children, one singly and many in a collection.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [Entity<Guid>("LINE")]
            public partial class OrderLine
            {
                public OrderLine(OrderLineId id) : base(id) { }
            }

            [Entity<Guid>("ADDR")]
            public partial class ShippingAddress
            {
                public ShippingAddress(ShippingAddressId id) : base(id) { }
            }

            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public partial IReadOnlyList<OrderLine> Lines { get; }

                public ShippingAddress ShipTo { get; private set; } = default!;
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00021");
        result.ShouldCompile();
    }

    [Fact]
    public void A_child_entity_navigating_back_to_the_root_that_owns_it_is_not_reported()
    {
        // Entity Framework needs this reference to map the owned type, and the Examples in this
        // repository use it. It is the one direction that does not widen the boundary: loading the line
        // already loaded the order.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public partial IReadOnlyList<OrderLine> Lines { get; }
            }

            [Entity<Guid>("LINE")]
            public partial class OrderLine
            {
                public OrderLine(OrderLineId id) : base(id) { }

                public Order Order { get; private set; } = default!;
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00021");
        result.ShouldCompile();
    }

    [Fact]
    public void The_back_navigation_is_allowed_when_the_root_holds_the_child_in_a_single_property_too()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public ShippingAddress ShipTo { get; private set; } = default!;
            }

            [Entity<Guid>("ADDR")]
            public partial class ShippingAddress
            {
                public ShippingAddress(ShippingAddressId id) : base(id) { }

                public Order Order { get; private set; } = default!;
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00021");
        result.ShouldCompile();
    }

    [Fact]
    public void Referring_to_another_aggregate_by_its_id_is_not_reported()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id, CustomerId buyer) : base(id) => Buyer = buyer;

                public CustomerId Buyer { get; private set; }

                public CustomerId? SecondaryBuyer { get; private set; }

                public partial IReadOnlyList<CustomerId> Watchers { get; }
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00021");
        result.ShouldCompile();
    }

    [Fact]
    public void A_method_that_takes_or_returns_another_root_is_not_reported()
    {
        // Passing one aggregate into another aggregate's method is how the two cooperate, and nothing is
        // stored by doing it. Entity Framework cannot see a method either, so no navigation comes of it.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public CustomerId Buyer { get; private set; }

                public void PlaceFor(Customer customer) => Buyer = customer.Id;

                public Customer Pick(IReadOnlyList<Customer> candidates) => candidates[0];
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00021");
        result.ShouldCompile();
    }

    [Fact]
    public void A_static_member_typed_as_a_root_is_not_reported()
    {
        // Static state is not part of any one aggregate's load, so it is outside what this rule is about.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public static Customer? House { get; set; }
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00021");
        result.ShouldCompile();
    }

    [Fact]
    public void Value_objects_ids_and_primitives_are_not_reported()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [ValueObject]
            public partial record Address
            {
                public string City { get; protected init; } = "";
            }

            [SingleValueObject<string>]
            public partial record Reference;

            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public Address ShipTo { get; private set; } = default!;

                public Reference? Code { get; private set; }

                public decimal Total { get; private set; }

                public partial IReadOnlyList<string> Notes { get; }
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00021");
        result.ShouldCompile();
    }

    // ------------------------------------------------------------------ how it behaves

    [Fact]
    public void It_is_a_warning_and_the_entity_is_still_generated()
    {
        // Unlike every other entity diagnostic this one does not stop generation: the code is valid C#
        // and the model is a judgement call, so the build keeps working and a team can suppress it.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public Customer Buyer { get; private set; } = default!;
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "Buyer").Severity.Should().Be(DiagnosticSeverity.Warning);
        result.ShouldCompile();
        result.ShouldHaveGenerated(Hint.Of("Sample.Order"));
        result.ShouldHaveGenerated(Hint.Of("Sample.OrderId"));
    }

    [Fact]
    public void NoWarn_turns_the_rule_off()
    {
        // The documented escape hatch: <NoWarn>DDD00021</NoWarn> in the project file. This is the route
        // the documentation tells a team to take, so it is worth a test rather than a claim.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public Customer Buyer { get; private set; } = default!;
            }
            """)
            .WithNoWarn("DDD00021")
            .RunCore();

        result.ShouldNotHaveDiagnostic("DDD00021");
        result.ShouldCompile();
    }

    [Fact]
    public void A_pragma_does_not_turn_the_rule_off()
    {
        // Not a bug we can fix here, and the documentation says so. A generator reports its diagnostics
        // with a location rebuilt from a file path, deliberately holding no syntax tree so the pipeline
        // stays cacheable, and the compiler has no tree to match a pragma against.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

            #pragma warning disable DDD00021
                public Customer Buyer { get; private set; } = default!;
            #pragma warning restore DDD00021
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "Buyer");
    }

    [Fact]
    public void Each_offending_member_is_reported_exactly_once()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public Customer Buyer { get; private set; } = default!;

                public partial IReadOnlyList<Customer> Watchers { get; }
            }
            """).RunCore();

        result.GeneratorDiagnostics.Count(diagnostic => diagnostic.Id == "DDD00021")
            .Should().Be(2, "one per member, not one per generator pass");
    }

    [Fact]
    public void A_class_carrying_both_entity_attributes_reports_only_the_clash()
    {
        // Both providers see this class. Reporting the boundary rule from each of them would print every
        // member twice, on a class that generates nothing anyway.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [Entity<Guid>("THG")]
            [AggregateRoot<Guid>("THG")]
            public partial class Thing
            {
                public Customer Buyer { get; private set; } = default!;
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00009", at: "Thing");
        result.ShouldNotHaveDiagnostic("DDD00021");
    }

    [Fact]
    public void A_root_in_another_assembly_counts_too()
    {
        // Warehouse comes from a compiled reference here, so its [AggregateRoot] is read out of metadata
        // rather than out of source. An aggregate in another project is if anything more clearly a
        // separate loading boundary, so the rule has to survive the trip.
        var result = GeneratorTestHost.Create(
            """
            using DDDToolkit.Abstractions.Attributes;
            using System;

            namespace Sample;

            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public Logistics.Warehouse ShipsFrom { get; private set; } = default!;
            }
            """)
            .WithReferencedAssembly(
                """
                using DDDToolkit.Abstractions.Attributes;
                using System;

                namespace Logistics;

                [AggregateRoot<Guid>("WH")]
                public partial class Warehouse
                {
                    public Warehouse(WarehouseId id) : base(id) { }
                }
                """)
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "ShipsFrom")
            .GetMessage().Should().Contain("WarehouseId");
    }
}
