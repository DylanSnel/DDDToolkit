namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00007 (the name the generated id would take is already in use) and DDD00008 (the type argument
/// is neither an id nor something an id can wrap). Both are about <c>[Entity&lt;T&gt;]</c> and
/// <c>[AggregateRoot&lt;T&gt;]</c> over a raw value, where the toolkit generates the id itself.
/// </summary>
public class ImplicitEntityIdDiagnosticTests
{
    private const string Usings = "using DDDToolkit.Abstractions.Attributes;\nusing System;\n\nnamespace Sample;\n\n";

    // ------------------------------------------------------------------ DDD00007

    [Fact]
    public void A_type_already_holding_the_id_name_reports_DDD00007_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            public sealed class OrderId
            {
            }

            [AggregateRoot<Guid>]
            public partial class Order
            {
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00007", at: "Order");
        diagnostic.GetMessage().Should().Contain("OrderId");
        result.GeneratedSources.Should().BeEmpty("neither the id nor the aggregate can be generated");

        // Without the diagnostic this would have been CS0101 from the compiler, pointing at generated
        // code the author never wrote.
        result.CompilationDiagnostics.Should().NotContain(d => d.Id == "CS0101");
    }

    [Fact]
    public void An_explicitly_declared_id_of_that_name_reports_DDD00007()
    {
        // The author declared the id and then asked for a second one. The message points at the fix.
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>("ORD")]
            public readonly partial record struct OrderId;

            [AggregateRoot<Guid>]
            public partial class Order
            {
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00007", at: "Order");
        diagnostic.GetMessage().Should().Contain("[AggregateRoot<OrderId>]");

        // The explicit id is untouched; only the aggregate is refused.
        result.ShouldHaveGenerated(Hint.Of("Sample.OrderId"));
        result.GeneratedSources.Should().NotContain(source => source.HintName == Hint.Of("Sample.Order"));
    }

    [Fact]
    public void A_record_struct_of_that_name_that_is_not_partial_reports_DDD00007()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            public readonly record struct OrderId(Guid Value);

            [AggregateRoot<Guid>]
            public partial class Order
            {
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00007", at: "Order");
    }

    [Fact]
    public void A_partial_part_declaring_a_different_accessibility_reports_DDD00007()
    {
        // The generated part is public, because the aggregate is; two parts stating different
        // accessibilities is CS0262, so say so before the compiler has to.
        var result = GeneratorTestHost.Create(Usings +
            """
            internal readonly partial record struct OrderId;

            [AggregateRoot<Guid>]
            public partial class Order
            {
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00007", at: "Order");
        result.CompilationDiagnostics.Should().NotContain(d => d.Id == "CS0262");
    }

    [Fact]
    public void A_partial_record_struct_of_that_name_is_the_authors_own_part_and_reports_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            public readonly partial record struct OrderId
            {
                public bool IsLegacy => Value.ToString().StartsWith("0", StringComparison.Ordinal);
            }

            [AggregateRoot<Guid>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00007");
        result.ShouldCompile();
    }

    [Fact]
    public void The_name_is_only_taken_when_the_clash_is_in_the_same_scope()
    {
        var result = GeneratorTestHost.Create(
            """
            using DDDToolkit.Abstractions.Attributes;
            using System;

            namespace Elsewhere
            {
                public sealed class OrderId
                {
                }
            }

            namespace Sample
            {
                [AggregateRoot<Guid>]
                public partial class Order
                {
                    public Order(OrderId id) : base(id) { }
                }
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00007");
        result.ShouldCompile();
    }

    // ------------------------------------------------------------------ DDD00008

    [Fact]
    public void A_class_that_is_not_an_id_reports_DDD00008_and_generates_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            public sealed class Money
            {
            }

            [AggregateRoot<Money>]
            public partial class Order
            {
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00008", at: "Order");
        diagnostic.GetMessage().Should().Contain("Money");
        diagnostic.GetMessage().Should().Contain("EntityId");
        result.ShouldNotHaveGeneratedFor("Order");
    }

    [Fact]
    public void An_interface_reports_DDD00008()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            public interface IThing
            {
            }

            [Entity<IThing>]
            public partial class Widget
            {
            }
            """).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00008", at: "Widget");
        diagnostic.GetMessage().Should().Contain("Entity");
        result.ShouldNotHaveGeneratedFor("Widget");
    }

    [Fact]
    public void A_nullable_value_type_reports_DDD00008()
    {
        // An optional id is 'OrderId?'. An id over 'Guid?' would have two absent values, null and
        // Guid.Empty, and no way to tell them apart.
        var result = GeneratorTestHost.Create(Usings +
            """
            [AggregateRoot<Guid?>]
            public partial class Order
            {
            }
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00008", at: "Order");
        result.ShouldNotHaveGeneratedFor("Order");
    }

    [Fact]
    public void A_string_is_accepted_and_reports_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [AggregateRoot<string>("SKU")]
            public partial class Product
            {
                public Product(ProductId id) : base(id) { }
            }
            """).RunCore();

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
    }

    [Fact]
    public void Naming_an_existing_id_reports_nothing()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityId<Guid>("ORD")]
            public readonly partial record struct OrderId;

            [AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }
            }
            """).RunCore();

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
    }
}
