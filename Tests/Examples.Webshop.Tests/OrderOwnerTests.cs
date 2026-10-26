using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using Examples.Hosting;
using Examples.Webshop.Ordering;
using Examples.Webshop.Ordering.Application.Orders;
using Examples.Webshop.Ordering.Contracts;
using Examples.Webshop.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Webshop.Tests;

/// <summary>
/// The owner of an order, at the door: <c>Order.PlacedBy</c> is marked <c>[Owner]</c>, a fact of the domain, and
/// <see cref="ShipElsewhere"/> is <c>[OwnOnly]</c> with <c>[Own]</c> on the order's id, the policy. Ordering is hosted as a
/// host has it, on SQLite, where nothing runs as the caller; what row level security hides is the toolkit's own tests'.
/// </summary>
public sealed class OrderOwnerTests : IAsyncLifetime
{
    private static readonly Guid Alice = Guid.Parse("a11ce000-0000-4000-8000-000000000001");

    private static readonly Guid Bob = Guid.Parse("b0b00000-0000-4000-8000-000000000002");

    private readonly string _directory = Directory.CreateTempSubdirectory("examples-webshop-").FullName;

    private ServiceProvider _host = null!;

    private Order _alices = null!;

    private Order _bobs = null!;

    private Order _guests = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrderingModule(ModuleHost.InProcess(ModuleDatabase.Sqlite(_directory)));
        _host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = _host.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrderingContext>();
        await orders.Database.EnsureCreatedAsync(Cancellation);

        _alices = PlacedBy(Alice);
        _bobs = PlacedBy(Bob);
        _guests = PlacedBy(null);
        orders.Orders.AddRange(_alices, _bobs, _guests);
        await orders.SaveChangesAsync(Cancellation);
    }

    [Fact]
    public void The_request_is_about_the_owner_of_the_order_it_names()
    {
        var order = OrderId.CreateSequential();

        ((IRequireAccess)new ShipElsewhere(order, "Oudegracht 1", "Utrecht", "3511 AA")).RequiredAccess.Should().Be(AccessRequirement.OwnerOf(order),
            "the order marks who placed it as its owner, and the request marks the order's id [Own]");
    }

    [Fact]
    public async Task Only_the_customer_who_placed_an_order_sends_it_elsewhere()
    {
        await AskAsync(Caller.User(Alice), _alices.Id);

        (await RefusedAsync(Caller.User(Alice), _bobs.Id)).Code.Should().Be(ToolkitRefusals.NotOwn, "Bob placed it");
        (await RefusedAsync(Caller.User(Alice), _guests.Id)).Code.Should().Be(ToolkitRefusals.NotOwn, "a guest's order is nobody's");
        (await RefusedAsync(Caller.Anonymous, _guests.Id)).Code.Should().Be(ToolkitRefusals.NotOwn, "not even for a guest, who is no customer");
        (await RefusedAsync(Caller.User(Alice), OrderId.CreateSequential())).Code.Should().Be(ToolkitRefusals.NotFound);
    }

    [Fact]
    public void An_order_goes_elsewhere_while_it_is_placed_and_keeps_its_address_once_confirmed()
    {
        var order = PlacedBy(Alice);
        var elsewhere = new Address("Neude 11", "Utrecht", "3512 AE").ToValid();

        order.DeliverTo(elsewhere);
        order.ShipTo.Street.Should().Be("Neude 11");

        order.RecordStockReserved(DateTimeOffset.UnixEpoch);
        order.RecordPayment(DateTimeOffset.UnixEpoch);
        order.Status.Should().Be(OrderStatus.Confirmed);

        var refusal = FluentActions.Invoking(() => order.DeliverTo(new Address("Oudegracht 1", "Utrecht", "3511 AA").ToValid()))
            .Should().Throw<RefusalException>().Which;
        refusal.Code.Should().Be(Order.NoLongerPlaced);
        refusal.Kind.Should().Be(RefusalKind.Conflict);
        order.ShipTo.Street.Should().Be("Neude 11", "the van was booked for that one");
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A file SQLite still holds goes with the rest of the temporary directory later.
        }
    }

    private async Task AskAsync(Caller caller, OrderId order)
    {
        await using var scope = _host.CreateAsyncScope();
        using var running = Callers.Begin(caller);
        await scope.ServiceProvider.GetRequiredService<AccessChecks<IOrderingRequest>>()
            .RequireAsync(new ShipElsewhere(order, "Neude 11", "Utrecht", "3512 AE"), Cancellation);
    }

    private async Task<RefusalException> RefusedAsync(Caller caller, OrderId order)
        => (await FluentActions.Awaiting(() => AskAsync(caller, order)).Should().ThrowAsync<RefusalException>()).Which;

    private static Order PlacedBy(Guid? customer)
        => new(
            OrderId.CreateSequential(),
            new Address("Oudegracht 1", "Utrecht", "3511 AA").ToValid(),
            [new OrderLine(OrderLineId.CreateSequential(), "MUG", 1, new Money(8m, Money.Euro))],
            customer is { } id ? new CustomerId(id) : null);
}
