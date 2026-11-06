using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DTS.DDD.Mediator.Tests;

/// <summary>
/// The toolkit's <see cref="IPublisher"/> on its own: which handlers a notification reaches, in which order, and the
/// behaviors around them. The handlers and behaviors here are private, so the generated registration leaves them out
/// and each test registers what it needs.
/// </summary>
public sealed class PublisherTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_notification_reaches_the_handlers_of_its_type_its_base_class_and_its_interfaces_in_registration_order()
    {
        var steps = await PublishAsync(new Stocked("tea"), services => services
            .AddNotificationHandler<IStockNotification, OnAnyStock>()
            .AddNotificationHandler<Stocked, OnStocked>()
            .AddNotificationHandler<StockNotification, OnStockBase>());

        steps.Should().Equal("any:tea", "stocked:tea", "base:tea");
    }

    [Fact]
    public async Task A_notification_without_a_handler_is_published_to_nobody()
    {
        var steps = await PublishAsync(new Unhandled(), services => services.AddNotificationHandler<Stocked, OnStocked>());

        steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Registering_the_same_handler_twice_runs_it_once()
    {
        var steps = await PublishAsync(new Stocked("tea"), services => services
            .AddNotificationHandler<Stocked, OnStocked>()
            .AddNotificationHandler<Stocked, OnStocked>());

        steps.Should().Equal("stocked:tea");
    }

    [Fact]
    public async Task The_first_exception_stops_the_handlers_after_it()
    {
        var act = () => PublishAsync(new Stocked("tea"), services => services
            .AddNotificationHandler<Stocked, Throws>()
            .AddNotificationHandler<Stocked, OnStocked>());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("refused tea");
    }

    [Fact]
    public async Task Publishing_an_object_that_is_no_notification_is_refused()
    {
        var services = new ServiceCollection().AddDDDToolkitMediator().BuildServiceProvider();
        using var scope = services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        var act = async () => await publisher.Publish((object)"tea", Cancellation);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("System.String");
    }

    [Fact]
    public async Task An_open_behavior_runs_around_each_handler_and_the_first_registered_is_the_outermost()
    {
        var steps = await PublishAsync(new Stocked("tea"), services => services
            .AddNotificationHandler<Stocked, OnStocked>()
            .AddNotificationHandler<IStockNotification, OnAnyStock>()
            .AddNotificationBehavior(typeof(Outer<>))
            .AddNotificationBehavior(typeof(Inner<>)));

        steps.Should().Equal(
            "outer>", "inner>", "stocked:tea", "<inner", "<outer",
            "outer>", "inner>", "any:tea", "<inner", "<outer");
    }

    [Fact]
    public async Task An_open_behavior_with_a_constraint_runs_only_around_handlers_of_types_that_meet_it()
    {
        var steps = await PublishAsync(new Stocked("tea"), services => services
            .AddNotificationHandler<Stocked, OnStocked>()
            .AddNotificationHandler<IStockNotification, OnAnyStock>()
            .AddNotificationBehavior(typeof(OnlyAroundCounted<>)));

        steps.Should().Equal("counted>", "stocked:tea", "any:tea");
    }

    [Fact]
    public async Task A_closed_behavior_runs_only_around_handlers_declared_for_its_type()
    {
        var steps = await PublishAsync(new Stocked("tea"), services => services
            .AddNotificationHandler<Stocked, OnStocked>()
            .AddNotificationHandler<IStockNotification, OnAnyStock>()
            .AddNotificationBehavior(typeof(AroundStocked)));

        steps.Should().Equal("around stocked>", "stocked:tea", "any:tea");
    }

    [Fact]
    public async Task A_behavior_that_calls_next_twice_runs_everything_inside_it_twice()
    {
        var steps = await PublishAsync(new Stocked("tea"), services => services
            .AddNotificationHandler<Stocked, OnStocked>()
            .AddNotificationBehavior(typeof(Twice<>))
            .AddNotificationBehavior(typeof(Inner<>)));

        steps.Should().Equal("inner>", "stocked:tea", "<inner", "inner>", "stocked:tea", "<inner");
    }

    [Fact]
    public void A_behavior_type_that_is_no_behavior_is_refused()
    {
        var act = () => new ServiceCollection().AddNotificationBehavior(typeof(OnStocked));

        act.Should().Throw<ArgumentException>().WithMessage("*OnStocked*");
    }

    private static async Task<IReadOnlyList<string>> PublishAsync(object notification, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        var steps = new Steps();
        services.AddSingleton(steps);
        register(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(notification, Cancellation);

        return steps.All;
    }

    private sealed class Steps
    {
        private readonly List<string> _all = [];

        public IReadOnlyList<string> All => _all;

        public void Add(string step) => _all.Add(step);
    }

    private interface IStockNotification : INotification
    {
        string Item { get; }
    }

    /// <summary>A notification that a behavior with a constraint applies to.</summary>
    private interface ICounted : INotification;

    private abstract record StockNotification(string Item) : IStockNotification;

    private sealed record Stocked(string Item) : StockNotification(Item), ICounted;

    private sealed record Unhandled : INotification;

    private sealed class OnStocked(Steps steps) : INotificationHandler<Stocked>
    {
        public ValueTask Handle(Stocked notification, CancellationToken cancellationToken)
        {
            steps.Add("stocked:" + notification.Item);
            return default;
        }
    }

    private sealed class OnStockBase(Steps steps) : INotificationHandler<StockNotification>
    {
        public ValueTask Handle(StockNotification notification, CancellationToken cancellationToken)
        {
            steps.Add("base:" + notification.Item);
            return default;
        }
    }

    private sealed class OnAnyStock(Steps steps) : INotificationHandler<IStockNotification>
    {
        public ValueTask Handle(IStockNotification notification, CancellationToken cancellationToken)
        {
            steps.Add("any:" + notification.Item);
            return default;
        }
    }

    private sealed class Throws : INotificationHandler<Stocked>
    {
        public ValueTask Handle(Stocked notification, CancellationToken cancellationToken)
            => throw new InvalidOperationException("refused " + notification.Item);
    }

    private sealed class Outer<TNotification>(Steps steps) : INotificationBehavior<TNotification>
        where TNotification : INotification
    {
        public async ValueTask Handle(TNotification notification, NotificationHandlerDelegate<TNotification> next, CancellationToken cancellationToken)
        {
            steps.Add("outer>");
            await next(notification, cancellationToken);
            steps.Add("<outer");
        }
    }

    private sealed class Inner<TNotification>(Steps steps) : INotificationBehavior<TNotification>
        where TNotification : INotification
    {
        public async ValueTask Handle(TNotification notification, NotificationHandlerDelegate<TNotification> next, CancellationToken cancellationToken)
        {
            steps.Add("inner>");
            await next(notification, cancellationToken);
            steps.Add("<inner");
        }
    }

    private sealed class OnlyAroundCounted<TNotification>(Steps steps) : INotificationBehavior<TNotification>
        where TNotification : ICounted
    {
        public ValueTask Handle(TNotification notification, NotificationHandlerDelegate<TNotification> next, CancellationToken cancellationToken)
        {
            steps.Add("counted>");
            return next(notification, cancellationToken);
        }
    }

    private sealed class AroundStocked(Steps steps) : INotificationBehavior<Stocked>
    {
        public ValueTask Handle(Stocked notification, NotificationHandlerDelegate<Stocked> next, CancellationToken cancellationToken)
        {
            steps.Add("around stocked>");
            return next(notification, cancellationToken);
        }
    }

    private sealed class Twice<TNotification> : INotificationBehavior<TNotification>
        where TNotification : INotification
    {
        public async ValueTask Handle(TNotification notification, NotificationHandlerDelegate<TNotification> next, CancellationToken cancellationToken)
        {
            await next(notification, cancellationToken);
            await next(notification, cancellationToken);
        }
    }
}
