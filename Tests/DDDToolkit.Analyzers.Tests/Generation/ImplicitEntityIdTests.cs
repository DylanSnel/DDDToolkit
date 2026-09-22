using System.Text.Json;
using DDDToolkit.BaseTypes;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// What <c>[AggregateRoot&lt;Guid&gt;]</c> and <c>[Entity&lt;Guid&gt;]</c> produce when the type argument is a raw
/// value rather than an existing id: the id itself, named after the entity, with the surface an
/// explicitly declared struct id has. As everywhere else in this project the snippet is compiled,
/// emitted and invoked, because generated text that does not run is worth nothing.
/// </summary>
public class ImplicitEntityIdTests
{
    private const string Preamble = "using DDDToolkit.Abstractions.Attributes;\nusing System;\n\nnamespace Sample;\n\n";

    private static GeneratorRunOutcome Aggregate(string attribute = """[AggregateRoot<Guid>("ORD")]""")
        => GeneratorTestHost.Create(Preamble +
            $$"""
              {{attribute}}
              public partial class Order
              {
                  public Order(OrderId id) : base(id) { }
              }
              """).RunCore();

    // ------------------------------------------------------------------ the id exists and is an id

    [Fact]
    public void An_aggregate_over_a_raw_value_generates_the_id_beside_it()
    {
        var result = Aggregate();

        result.ShouldCompile();
        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldHaveGenerated(Hint.Of("Sample.OrderId"));
        result.ShouldContain(Hint.Of("Sample.OrderId"), "public readonly partial record struct OrderId",
            because: "nobody else declares the type, so the generated part carries the accessibility and 'readonly' itself");
    }

    [Fact]
    public void The_aggregate_derives_from_the_generated_id()
    {
        var emitted = Aggregate().Emit();
        var idType = emitted.Type("Sample.OrderId");

        emitted.Type("Sample.Order").BaseType.Should().Be(typeof(AggregateRoot<>).MakeGenericType(idType));
    }

    [Fact]
    public void The_aggregate_carries_the_generated_id_as_its_identity()
    {
        var emitted = Aggregate().Emit();
        var id = emitted.CallStatic("Sample.OrderId", "CreateUnique")!;

        var order = emitted.New("Sample.Order", id);

        emitted.Property(order, "Id").Should().Be(id);
        order.Should().Be(emitted.New("Sample.Order", id), "entities are equal when their ids are");
    }

    [Fact]
    public void A_child_entity_gets_one_too()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [Entity<int>("LINE")]
            public partial class OrderLine
            {
                public OrderLine(OrderLineId id) : base(id) { }
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveGenerated(Hint.Of("Sample.OrderLineId"));

        var emitted = result.Emit();
        var id = emitted.New("Sample.OrderLineId", 7);

        emitted.Property(emitted.New("Sample.OrderLine", id), "Id").Should().Be(id);
        id.ToString().Should().Be("LINE_7");
    }

    // ------------------------------------------------------------------ the same surface as an explicit id

    [Fact]
    public void The_id_has_the_members_an_explicitly_declared_struct_id_has()
    {
        var emitted = Aggregate().Emit();
        var guid = Guid.NewGuid();

        var id = emitted.New("Sample.OrderId", guid);

        emitted.Property(id, "Value").Should().Be(guid);
        emitted.StaticProperty("Sample.OrderId", "IdPrefix").Should().Be("ORD");
        id.ToString().Should().Be("ORD_" + guid);
        emitted.CallStatic("Sample.OrderId", "Parse", "ORD_" + guid).Should().Be(id);
        emitted.CallStatic("Sample.OrderId", "Parse", guid.ToString()).Should().Be(id);
        emitted.TryParse("Sample.OrderId", "nonsense").Success.Should().BeFalse();
        emitted.StaticProperty("Sample.OrderId", "Empty").Should().Be(emitted.Default("Sample.OrderId"));
        emitted.Property(emitted.Default("Sample.OrderId"), "IsEmpty").Should().Be(true);
        emitted.Property(id, "IsEmpty").Should().Be(false);
        emitted.Property(emitted.CallStatic("Sample.OrderId", "CreateUnique")!, "IsEmpty").Should().Be(false);
        emitted.Property(emitted.CallStatic("Sample.OrderId", "CreateSequential")!, "IsEmpty").Should().Be(false);
        ((int)emitted.Call(id, "CompareTo", id)!).Should().Be(0);
    }

    [Fact]
    public void The_id_implements_the_interfaces_an_explicitly_declared_struct_id_implements()
    {
        var idType = Aggregate().Emit().Type("Sample.OrderId");

        idType.IsValueType.Should().BeTrue();
        typeof(Abstractions.Interfaces.IEntityId<Guid>).IsAssignableFrom(idType).Should().BeTrue();
        typeof(IComparable<>).MakeGenericType(idType).IsAssignableFrom(idType).Should().BeTrue();
        typeof(IParsable<>).MakeGenericType(idType).IsAssignableFrom(idType).Should().BeTrue();
        typeof(IEquatable<>).MakeGenericType(idType).IsAssignableFrom(idType).Should().BeTrue();
    }

    [Fact]
    public void The_id_serializes_as_its_bare_value_through_the_generated_JSON_converter()
    {
        var emitted = Aggregate().Emit();
        var guid = Guid.NewGuid();
        var id = emitted.New("Sample.OrderId", guid);

        EmittedAssembly.ToJson(id).Should().Be(JsonSerializer.Serialize(guid));
        emitted.JsonRoundTrip(id).Should().Be(id);
    }

    [Fact]
    public void The_generated_file_is_what_the_explicit_declaration_would_have_produced()
    {
        // The same emitter writes both, so the only difference is the header: the implicit part has
        // nobody to take its accessibility from.
        var implicitForm = Aggregate().Source(Hint.Of("Sample.OrderId"));
        var explicitForm = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<Guid>("ORD")]
            public readonly partial record struct OrderId;
            """).RunCore().Source(Hint.Of("Sample.OrderId"));

        implicitForm.Should().Be(explicitForm.Replace(
            "readonly partial record struct OrderId",
            "public readonly partial record struct OrderId",
            StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the attribute's arguments

    [Fact]
    public void Without_a_prefix_the_id_prints_its_bare_value()
    {
        var emitted = Aggregate("[AggregateRoot<Guid>]").Emit();
        var guid = Guid.NewGuid();

        emitted.StaticProperty("Sample.OrderId", "IdPrefix").Should().Be("",
            "a prefix ends up in logs and URLs, so it is the author's choice and never guessed from the type name");
        emitted.New("Sample.OrderId", guid).ToString().Should().Be(guid.ToString());
    }

    [Fact]
    public void The_prefix_and_the_column_length_can_be_passed_by_name()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<string>(Prefix: "SKU", ColumnLength: 32)]
            public partial class Product
            {
                public Product(ProductId id) : base(id) { }
            }
            """)
            .WithEntityFramework()
            .WithModule("Catalog")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain("ConverterExtensions", "HaveMaxLength(32)");

        var emitted = result.Emit();
        emitted.New("Sample.ProductId", "abc").ToString().Should().Be("SKU_abc");
    }

    [Fact]
    public void An_id_over_a_value_type_without_parsing_is_generated_without_it()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            public readonly record struct Ticket(int Number);

            [AggregateRoot<Ticket>]
            public partial class Seat
            {
                public Seat(SeatId id) : base(id) { }
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldNotContain(Hint.Of("Sample.SeatId"), "TryParse");

        var emitted = result.Emit();
        emitted.HasMember("Sample.SeatId", "Parse").Should().BeFalse();
        emitted.Property(emitted.New("Sample.SeatId", emitted.New("Sample.Ticket", 3)), "IsEmpty").Should().Be(false);
    }

    // ------------------------------------------------------------------ living next to hand-written code

    [Fact]
    public void The_author_can_add_members_to_the_generated_id_in_their_own_file()
    {
        // The generated declaration is partial for exactly this. The author's part needs no
        // accessibility modifier; it takes the one the generated part states.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }
            }

            public readonly partial record struct OrderId
            {
                public string Short => Value.ToString("N").Substring(0, 8);
            }
            """).RunCore();

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();

        var emitted = result.Emit();
        var guid = Guid.NewGuid();

        emitted.Property(emitted.New("Sample.OrderId", guid), "Short").Should().Be(guid.ToString("N").Substring(0, 8));
    }

    [Fact]
    public void An_entity_nested_in_a_class_gets_its_id_nested_beside_it()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            public partial class Catalog
            {
                [AggregateRoot<Guid>("PRD")]
                public partial class Product
                {
                    public Product(ProductId id) : base(id) { }
                }
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveGenerated(Hint.Of("Sample.Catalog.ProductId"));

        var emitted = result.Emit();
        emitted.Type("Sample.Catalog+ProductId").Should().NotBeNull();
        emitted.HasType("Sample.ProductId").Should().BeFalse("the id belongs where the entity is");
    }

    [Fact]
    public void An_internal_aggregate_gets_an_internal_id()
    {
        // The id takes the entity's accessibility: an id nobody outside the assembly can name is of no
        // use to an aggregate nobody outside the assembly can name either.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            internal partial class Order
            {
                public Order(OrderId id) : base(id) { }
            }
            """)
            .WithEntityFramework()
            .WithHotChocolate()
            .WithModule("Sales")
            .RunCoreAnd([.. GeneratorTestHost.EntityFrameworkGenerators(), .. GeneratorTestHost.HotChocolateGenerators()]);

        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample.OrderId"), "internal readonly partial record struct OrderId");
        result.Emit().Type("Sample.OrderId").IsPublic.Should().BeFalse();
    }

    [Fact]
    public void Two_entities_in_one_namespace_get_two_distinct_ids()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<Guid>("ORD")]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }
            }

            [AggregateRoot<Guid>("CUS")]
            public partial class Customer
            {
                public Customer(CustomerId id) : base(id) { }
            }
            """).RunCore();

        result.ShouldCompile();

        var emitted = result.Emit();
        emitted.Type("Sample.OrderId").Should().NotBe(emitted.Type("Sample.CustomerId"));
    }

    // ------------------------------------------------------------------ the explicit form is untouched

    [Fact]
    public void Naming_an_existing_id_still_uses_that_id_and_generates_no_second_one()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<Guid>("ORD")]
            public readonly partial record struct OrderId;

            [AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }
            }
            """).RunCore();

        result.ShouldCompile();
        result.GeneratorDiagnostics.Should().BeEmpty();
        result.GeneratedSources.Select(source => source.HintName).Should().BeEquivalentTo([Hint.Of("Sample.OrderId"), Hint.Of("Sample.Order")]);

        var emitted = result.Emit();
        emitted.HasType("Sample.OrderIdId").Should().BeFalse("the type argument already was the id");
        emitted.Type("Sample.Order").BaseType.Should().Be(typeof(AggregateRoot<>).MakeGenericType(emitted.Type("Sample.OrderId")));
    }

    [Fact]
    public void An_id_that_is_a_record_class_is_still_accepted_as_the_type_argument()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [EntityId<Guid>("USR")]
            public partial record UserId
            {
                public static UserId Create(Guid value) => new(value);
            }

            [AggregateRoot<UserId>]
            public partial class User
            {
                public User(UserId id) : base(id) { }
            }
            """).RunCore();

        result.ShouldCompile();
        result.GeneratorDiagnostics.Should().BeEmpty();

        var emitted = result.Emit();
        emitted.HasType("Sample.UserIdId").Should().BeFalse();
        emitted.Type("Sample.User").BaseType.Should().Be(typeof(AggregateRoot<>).MakeGenericType(emitted.Type("Sample.UserId")));
    }

    [Fact]
    public void An_id_from_another_assembly_is_recognised_by_its_interface()
    {
        // The compiled DDDToolkit test assemblies are the only place an already-generated id exists;
        // here the point is that a type implementing IEntityId<T> is taken as the id, attribute or not.
        var result = GeneratorTestHost.Create(Preamble +
            """
            public readonly record struct LegacyId(Guid Value) : DDDToolkit.Abstractions.Interfaces.IEntityId<Guid>;

            [AggregateRoot<LegacyId>]
            public partial class Legacy
            {
                public Legacy(LegacyId id) : base(id) { }
            }
            """).RunCore();

        result.ShouldCompile();
        result.GeneratorDiagnostics.Should().BeEmpty();
        result.Emit().HasType("Sample.LegacyIdId").Should().BeFalse();
    }
}
