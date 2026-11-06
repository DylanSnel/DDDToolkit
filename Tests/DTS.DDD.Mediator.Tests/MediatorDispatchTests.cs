using DTS.DDD.BaseTypes;
using DTS.DDD.EntityFramework;
using DTS.DDD.EntityFramework.Options;
using DTS.DDD.EntityFramework.Outbox;
using DTS.DDD.Mediator.Tests.Domain;
using DTS.DDD.Mediator.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DTS.DDD.Mediator.Tests;

/// <summary>
/// <c>DispatchWithMediator()</c> end to end: an aggregate raises events, <c>SaveChangesAsync</c>
/// dispatches them, and the toolkit's publisher hands them to the handlers that the generated
/// <c>AddMediatorTestsHandlers()</c> registered. Everything runs against a real in-memory SQLite database.
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

        // The generated registration lists a project's handlers by their full name, and they run in that order.
        host.Log.Handled.Should().Equal(
            $"{nameof(BasketCreatedAuditHandler)}:basket.created",
            $"{nameof(BasketCreatedHandler)}:basket.created");
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
    public async Task An_event_without_a_handler_is_published_to_nobody_and_the_save_goes_on()
    {
        using var host = new TestHost();
        var basket = NewBasket();
        basket.Archive();

        await host.InScopeAsync(async context =>
        {
            context.Baskets.Add(basket);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        host.Log.CountOf<BasketArchived>().Should().Be(0);
        host.Log.CountOf<BasketCreated>().Should().Be(2, "the event before it was handled");
        host.CountRows("Baskets").Should().Be(1);
    }

    [Fact]
    public async Task A_handler_declared_for_an_interface_runs_for_every_event_that_implements_it()
    {
        using var host = new TestHost(register: services => services.AddNotificationHandler<IBasketEvent, EveryBasketEventHandler>());
        var basket = NewBasket();
        basket.AddItem("Milk");
        basket.Archive();

        await host.InScopeAsync(async context =>
        {
            context.Baskets.Add(basket);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        host.Log.Entries
            .Where(entry => entry.Handler == nameof(EveryBasketEventHandler))
            .Select(entry => DomainEventName.For(entry.Event))
            .Should().Equal(["basket.created", "basket.item-added"], "BasketArchived is no IBasketEvent");
    }

    [Fact]
    public async Task The_generated_registration_leaves_out_a_private_handler()
    {
        using var host = new TestHost();

        await host.InScopeAsync(async context =>
        {
            context.Baskets.Add(NewBasket());
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        host.Log.Entries.Should().NotContain(entry => entry.Handler == nameof(EveryBasketEventHandler), "the registration cannot name a private nested class");
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
    public async Task Handlers_get_the_saving_context_when_it_comes_from_a_pool()
    {
        using var host = new TestHost(pooled: true);
        var basket = NewBasket();
        basket.Empty();

        var saving = await host.InScopeAsync(async (context, _) =>
        {
            context.IsPooled().Should().BeTrue();
            context.Baskets.Add(basket);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            return context;
        });

        host.Log.ContextSeenByHandler.Should().BeSameAs(saving, "the publisher comes from the scope that rented the context, so a handler that injects the context gets the one being saved");
        host.Log.CountOf<BasketEmptied>().Should().Be(1);
        host.CountRows("Baskets").Should().Be(1);
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

    [Fact]
    public void DispatchWithMediator_called_again_changes_nothing_and_beside_another_delegate_is_refused()
    {
        var modules = new DDDEntityFrameworkOptions();
        modules.DispatchWithMediator().DispatchWithMediator();
        modules.Dispatcher.Should().NotBeNull("each module whose outbox delivers in process may ask for the mediator");

        var host = new DDDEntityFrameworkOptions();
        host.DispatchInProcess((_, _, _) => Task.CompletedTask);
        var act = () => host.DispatchWithMediator();

        act.Should().Throw<InvalidOperationException>().WithMessage("*already configured*");
    }

    /// <summary>
    /// Declared for the interface every basket event implements. Private, so the generated registration leaves it
    /// out, and a test that wants it registers it.
    /// </summary>
    private sealed class EveryBasketEventHandler(EventLog log) : INotificationHandler<IBasketEvent>
    {
        public ValueTask Handle(IBasketEvent notification, CancellationToken cancellationToken)
        {
            log.Record(nameof(EveryBasketEventHandler), notification);
            return default;
        }
    }
}
