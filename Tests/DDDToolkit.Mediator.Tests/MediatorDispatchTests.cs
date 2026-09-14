using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Mediator.Tests.Domain;
using DDDToolkit.Mediator.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Mediator.Tests;

/// <summary>
/// <c>DispatchWithMediator()</c> end to end: an aggregate raises events, <c>SaveChangesAsync</c>
/// dispatches them, and Mediator's generated implementation routes them to the handlers in this
/// assembly. Everything runs against a real in-memory SQLite database.
/// </summary>
public sealed class MediatorDispatchTests
{
    private static Basket NewBasket(string name = "Weekly") => new(BasketId.CreateUnique(), name);

    [Fact]
    public async Task Event_raised_on_an_aggregate_reaches_its_handler_exactly_once()
    {
        using var host = new TestHost();
        var basket = NewBasket();

        await host.InScopeAsync(async context =>
        {
            context.Baskets.Add(basket);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        host.Log.CountOf<BasketCreated>().Should().Be(2, "two handlers are registered for BasketCreated, each running once");
        host.Log.Entries.Where(entry => entry.Handler == nameof(BasketCreatedHandler)).Should().ContainSingle();
        host.CountRows("Baskets").Should().Be(1);
    }

    [Fact]
    public async Task Every_handler_registered_for_an_event_runs_once()
    {
        using var host = new TestHost();

        await host.InScopeAsync(async context =>
        {
            context.Baskets.Add(NewBasket());
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        host.Log.Handled.Should().Equal(
            $"{nameof(BasketCreatedHandler)}:basket.created",
            $"{nameof(BasketCreatedAuditHandler)}:basket.created");
    }

    [Fact]
    public async Task Events_reach_handlers_in_the_order_they_were_raised()
    {
        using var host = new TestHost();
        var basket = NewBasket();
        basket.AddItem("Milk");
        basket.AddItem("Bread");
        basket.Empty();

        await host.InScopeAsync(async context =>
        {
            context.Baskets.Add(basket);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        host.Log.Names.Should().Equal(
            "basket.created",
            "basket.created",
            "basket.item-added",
            "basket.item-added",
            "basket.emptied");

        host.Log.Entries
            .Where(entry => entry.Event is ItemAdded)
            .Select(entry => ((ItemAdded)entry.Event).Item)
            .Should().Equal("Milk", "Bread");
    }

    [Fact]
    public async Task Throwing_handler_aborts_the_save()
    {
        using var host = new TestHost();
        host.Log.Fail = (handler, _) => handler == nameof(ItemAddedHandler)
            ? new InvalidOperationException("handler failed")
            : null;

        var basket = NewBasket();
        basket.AddItem("Milk");

        var act = () => host.InScopeAsync(async context =>
        {
            context.Baskets.Add(basket);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler failed");
        host.CountRows("Baskets").Should().Be(0, "the save never happened");
    }

    [Fact]
    public async Task Event_that_is_not_a_Mediator_notification_fails_naming_the_event_type()
    {
        using var host = new TestHost();
        var basket = NewBasket();
        basket.Archive();

        var act = () => host.InScopeAsync(async context =>
        {
            context.Baskets.Add(basket);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should()
            .Contain(typeof(BasketArchived).FullName)
            .And.Contain("Mediator.INotification")
            .And.Contain(nameof(MediatorDispatchExtensions.DispatchWithMediator));

        host.CountRows("Baskets").Should().Be(0, "a domain event that cannot be published is not quietly dropped");
        host.Log.CountOf<BasketCreated>().Should().Be(2, "events raised before the unpublishable one were already handled");
    }

    [Fact]
    public async Task Handler_receives_the_DbContext_that_is_saving()
    {
        using var host = new TestHost();
        var basket = NewBasket();
        basket.Empty();

        var saving = await host.InScopeAsync(async (context, _) =>
        {
            context.Baskets.Add(basket);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            return context;
        });

        host.Log.ContextSeenByHandler.Should().BeSameAs(saving, "Mediator is registered scoped, so the handler resolves the scope's own context");
    }

    [Fact]
    public async Task Outbox_delivers_through_the_same_Mediator_dispatch()
    {
        using var host = new TestHost(options => options.UseOutbox(outbox => outbox.RegisterEventsFromAssemblyContaining<Basket>()));
        var basket = NewBasket();
        basket.AddItem("Milk");

        await host.InScopeAsync(async context =>
        {
            context.Baskets.Add(basket);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        host.Log.Entries.Should().BeEmpty("in outbox mode nothing is dispatched at save time");
        host.CountRows("OutboxMessages").Should().Be(2);

        using var scope = host.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor<BasketContext>>();
        var delivered = await processor.ProcessPendingAsync(cancellationToken: TestContext.Current.CancellationToken);

        delivered.Should().Be(2);

        // The outbox loads oldest first but makes no ordering promise, so this only checks that every
        // handler ran; ordering is the in-process test above.
        host.Log.Names.Should().BeEquivalentTo(["basket.created", "basket.created", "basket.item-added"]);
    }
}
