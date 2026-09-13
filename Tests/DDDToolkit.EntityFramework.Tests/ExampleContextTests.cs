using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.ExampleApi.Context;
using DDDToolkit.ExampleApi.Domain.ProductAggregate;
using DDDToolkit.ExampleApi.Domain.ProductAggregate.ValueObjects;
using DDDToolkit.ExampleApi.Domain.UserAggregate;
using DDDToolkit.ExampleApi.Domain.UserAggregate.Entities;
using DDDToolkit.ExampleApi.Domain.UserAggregate.Events;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>The showcase context of the ExampleApi builds on SQLite and round-trips its aggregates.</summary>
public sealed class ExampleContextTests : IDisposable
{
    private readonly SqliteDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private ExampleContext CreateContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        => new(_db.Options<ExampleContext>(builder => builder.AddInterceptors(interceptors)));

    [Fact]
    public void Model_maps_owned_orders_primitive_product_ids_complex_name_and_version_token()
    {
        using var context = CreateContext();
        var model = context.Model;

        var user = model.FindEntityType(typeof(User))!;
        user.FindProperty(nameof(IAggregateRoot.Version))!.IsConcurrencyToken.Should().BeTrue();
        user.FindComplexProperty(nameof(User.Name)).Should().NotBeNull("PersonName is a [ValueObject] complex type");
        user.FindProperty(nameof(User.Email))!.GetMaxLength().Should().Be(EmailAddress.MaxLength);
        user.FindNavigation(nameof(User.Orders))!.ForeignKey.IsOwnership.Should().BeTrue("[Entity] Order is owned");

        var order = model.FindEntityType(typeof(Order))!;
        order.IsOwned().Should().BeTrue();
        var products = order.FindProperty(nameof(Order.Products))!;
        products.IsPrimitiveCollection.Should().BeTrue();
        products.GetElementType()!.GetValueConverter().Should().BeOfType<ProductId.ProductIdConverter>();

        model.FindEntityType(typeof(Product))!.FindProperty(nameof(IAggregateRoot.Version))!.IsConcurrencyToken.Should().BeTrue();
        model.FindEntityType(typeof(OutboxMessage)).Should().NotBeNull();

        context.Database.EnsureCreated();
        context.Database.GenerateCreateScript().Should().Contain("CREATE TABLE \"Order\"").And.Contain("CREATE TABLE \"OutboxMessages\"");
    }

    [Fact]
    public async Task User_with_two_orders_saves_reloads_and_dispatches_its_events()
    {
        var dispatched = new List<IDomainEvent>();
        var options = new DDDEntityFrameworkOptions().DispatchInProcess((_, events, _) =>
        {
            dispatched.AddRange(events);
            return Task.CompletedTask;
        });
        var interceptors = new Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[]
        {
            new PublishDomainEventsInterceptor(new ServiceCollection().BuildServiceProvider(), options),
            new AggregateVersionInterceptor(),
        };

        var productA = new Product(ProductId.CreateUnique()) { Name = "A", Price = 1 };
        var productB = new Product(ProductId.CreateUnique()) { Name = "B", Price = 2 };
        var user = new User(UserId.CreateUnique(), new PersonName("John", "Doe"), EmailAddress.Create("johndoe@example.com"));
        var firstOrder = new Order(OrderId.CreateUnique(), [productA.Id, productB.Id]);
        var secondOrder = new Order(OrderId.CreateUnique(), [productB.Id]);
        user.AddOrder(firstOrder);
        user.AddOrder(secondOrder);

        using (var context = CreateContext(interceptors))
        {
            await context.Database.EnsureCreatedAsync();
            context.Products.AddRange(productA, productB);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        user.Version.Should().Be(1);
        dispatched.Select(e => e.GetType()).Should().Equal(typeof(UserCreated), typeof(OrderPlaced), typeof(OrderPlaced));
        dispatched.OfType<OrderPlaced>().Select(e => e.OrderId).Should().Equal(firstOrder.Id, secondOrder.Id);

        using (var context = CreateContext(interceptors))
        {
            var loaded = await context.Users.SingleAsync(u => u.Id == user.Id);

            loaded.Name.Should().Be(new PersonName("John", "Doe"));
            loaded.Email.Should().Be(EmailAddress.Create("johndoe@example.com"));
            loaded.Version.Should().Be(1);
            loaded.Orders.Should().HaveCount(2);
            loaded.Orders.Single(o => o.Id == firstOrder.Id).Products.Should().Equal(productA.Id, productB.Id);
            loaded.Orders.Single(o => o.Id == secondOrder.Id).Products.Should().Equal(productB.Id);
            loaded.Orders.Should().NotBeAssignableTo<List<Order>>();

            loaded.Orders.Single(o => o.Id == secondOrder.Id).AddProduct(productA.Id);
            await context.SaveChangesAsync();
            loaded.Version.Should().Be(2, "a change inside an owned order versions the user");
        }

        using (var context = CreateContext())
        {
            var loaded = await context.Users.SingleAsync(u => u.Id == user.Id);
            loaded.Orders.Single(o => o.Id == secondOrder.Id).Products.Should().Equal(productB.Id, productA.Id);
            loaded.Version.Should().Be(2);
            (await context.Products.CountAsync()).Should().Be(2);
        }
    }
}
