using System.Text.Json;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// All nine generators over one realistic domain, the way a real project runs them. The individual tests
/// each run a narrow combination; this one catches what only shows up when everything is on at once — two
/// generators claiming the same hint name, one generator's output failing to compile against another's,
/// or a member emitted twice.
/// </summary>
public class AllGeneratorsTogetherTests
{
    private const string Domain =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.HotChocolate.Attributes;
        using FluentValidation;
        using System;
        using System.Collections.Generic;

        namespace Shop;

        [EntityId<Guid>("PRD")]
        public readonly partial record struct ProductId;

        [EntityId<Guid>("ORD")]
        public readonly partial record struct OrderId;

        [EntityId<Guid>("USR")]
        public partial record UserId
        {
            public static UserId Create(Guid value) => new(value);
        }

        [GraphQLType<HotChocolate.Types.StringType>]
        [SingleValueObject<string>(ColumnLength: 255)]
        public partial record EmailAddress
        {
            public static EmailAddress Create(string value) => new(value);

            partial class Validator
            {
                public Validator() => RuleFor(x => x.Value).NotEmpty().EmailAddress();
            }
        }

        [ValueObject]
        public partial record PersonName
        {
            public PersonName(string firstName, string? middleNames, string lastName)
            {
                FirstName = firstName;
                MiddleNames = middleNames;
                LastName = lastName;
            }

            public string FirstName { get; protected init; }

            [DontCompare]
            public string? MiddleNames { get; protected init; }

            public string LastName { get; protected init; }

            partial class Validator
            {
                public Validator()
                {
                    RuleFor(x => x.FirstName).NotEmpty();
                    RuleFor(x => x.LastName).NotEmpty();
                }
            }
        }

        [AggregateRoot<Guid>("INV")]
        public partial class Invoice
        {
            public Invoice(InvoiceId id, OrderId order) : base(id) => Order = order;

            public OrderId Order { get; private set; }
        }

        [Entity<OrderId>]
        public partial class Order
        {
            public Order(OrderId id, IEnumerable<ProductId> products) : base(id) => _products.AddRange(products);

            public partial IReadOnlyList<ProductId> Products { get; }
        }

        [AggregateRoot<UserId>]
        public partial class User
        {
            public User(UserId id, PersonName name, EmailAddress email) : base(id)
            {
                Name = name;
                Email = email;
            }

            public PersonName Name { get; private set; }

            public EmailAddress Email { get; private set; }

            public partial IReadOnlyList<Order> Orders { get; }

            public partial IReadOnlySet<ProductId> Wishlist { get; }

            public void Place(Order order) => _orders.Add(order);

            public void Wish(ProductId product) => _wishlist.Add(product);
        }
        """;

    private static GeneratorRunOutcome Run() => GeneratorTestHost.Create(Domain)
        .WithAssemblyName("Shop.Domain")
        .WithEntityFramework()
        .WithFluentValidation()
        .WithHotChocolate()
        .WithModule("Shop")
        .RunCoreAnd(
        [
            .. GeneratorTestHost.EntityFrameworkGenerators(),
            .. GeneratorTestHost.FluentValidationGenerators(),
            .. GeneratorTestHost.HotChocolateGenerators(),
        ]);

    [Fact]
    public void A_whole_domain_compiles_with_every_generator_on()
    {
        var result = Run();

        result.ShouldNotCrash();
        result.GeneratorDiagnostics.Should().BeEmpty();
        result.ShouldCompile();
        result.GeneratedSources.Select(source => source.HintName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_generator_contributed()
    {
        var hintNames = Run().GeneratedSources.Select(source => source.HintName).ToList();

        hintNames.Should().Contain("Shop.ProductId.g.cs");
        hintNames.Should().Contain("Shop.InvoiceId.g.cs", "the id generated from [AggregateRoot<Guid>] is emitted like any other");
        hintNames.Should().Contain("Shop.InvoiceId.Converter.g.cs");
        hintNames.Should().Contain("Shop.InvoiceId.HotChocolate.g.cs");
        hintNames.Should().Contain("Shop.User.g.cs");
        hintNames.Should().Contain("Shop.EmailAddress.g.cs");
        hintNames.Should().Contain("Shop.PersonName.g.cs");
        hintNames.Should().Contain("Shop.ProductId.Converter.g.cs");
        hintNames.Should().Contain("Shop.Order.EntityFramework.g.cs");
        hintNames.Should().Contain("Shop.PersonName.EntityFramework.g.cs");
        hintNames.Should().Contain("Shop.EmailAddress.FluentValidation.g.cs");
        hintNames.Should().Contain("Shop.ProductId.HotChocolate.g.cs");
        hintNames.Should().Contain("ConverterExtensions.g.cs");
        hintNames.Should().Contain("BindingExtensions.g.cs");
    }

    [Fact]
    public void The_aggregate_behaves_as_written()
    {
        var emitted = Run().Emit();

        var userId = emitted.CallStatic("Shop.UserId", "Create", Guid.NewGuid())!;
        var name = emitted.New("Shop.PersonName", "Ada", null, "Lovelace");
        var email = emitted.CallStatic("Shop.EmailAddress", "Create", "ada@example.com")!;
        var user = emitted.New("Shop.User", userId, name, email);

        var productId = emitted.CallStatic("Shop.ProductId", "CreateUnique")!;
        var products = System.Array.CreateInstance(emitted.Type("Shop.ProductId"), 1);
        products.SetValue(productId, 0);
        var order = emitted.New("Shop.Order", emitted.CallStatic("Shop.OrderId", "CreateUnique")!, products);

        emitted.Call(user, "Place", order);
        emitted.Call(user, "Wish", productId);

        // Cast through the non-generic interface: IEnumerable<T> of a struct is not IEnumerable<object>.
        Items(emitted.Property(user, "Orders")).Should().ContainSingle().Which.Should().Be(order);
        Items(emitted.Property(user, "Wishlist")).Should().ContainSingle().Which.Should().Be(productId);
        Items(emitted.Property(order, "Products")).Should().ContainSingle().Which.Should().Be(productId);
        emitted.Property(email, "IsValid").Should().Be(true);
        emitted.Property(name, "IsValid").Should().Be(true);
    }

    [Fact]
    public void The_aggregates_ids_survive_a_JSON_round_trip_inside_the_aggregate()
    {
        var emitted = Run().Emit();
        var productId = emitted.CallStatic("Shop.ProductId", "CreateUnique")!;

        var json = EmittedAssembly.ToJson(productId);
        var back = JsonSerializer.Deserialize(json, emitted.Type("Shop.ProductId"));

        back.Should().Be(productId);
        json.Should().Be(JsonSerializer.Serialize((Guid)emitted.Property(productId, "Value")!));
    }

    private static IEnumerable<object> Items(object? collection)
        => ((System.Collections.IEnumerable)collection!).Cast<object>();
}
