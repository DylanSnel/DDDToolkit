using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DTS.DDD.Mediator.Tests;

/// <summary>
/// The toolkit's <see cref="ISender"/> on its own: which handler a request, a command or a query reaches, the
/// behaviors around it, and the stream kinds. The handlers and behaviors here are private, so the generated
/// registration leaves them out and each test registers what it needs.
/// </summary>
public sealed class SenderTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_command_a_query_and_a_request_each_reach_their_one_handler()
    {
        await using var provider = Provider(services => services
            .AddCommandHandler<Restock, int, OnRestock>()
            .AddQueryHandler<CountStock, int, OnCountStock>()
            .AddRequestHandler<Ping, Unit, OnPing>());
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        (await sender.Send(new Restock("tea", 3), Cancellation)).Should().Be(3);
        (await sender.Send(new CountStock("tea"), Cancellation)).Should().Be(7);
        (await sender.Send(new Ping(), Cancellation)).Should().Be(Unit.Value);
        scope.ServiceProvider.GetRequiredService<Steps>().All.Should().Equal("restock:tea", "count:tea", "ping");
    }

    [Fact]
    public async Task A_message_sent_as_an_object_reaches_its_handler_and_answers_as_an_object()
    {
        await using var provider = Provider(services => services.AddCommandHandler<Restock, int, OnRestock>());
        await using var scope = provider.CreateAsyncScope();

        var answer = await scope.ServiceProvider.GetRequiredService<ISender>().Send((object)new Restock("tea", 4), Cancellation);

        answer.Should().Be(4);
    }

    [Fact]
    public async Task The_mediator_sends_and_publishes_from_the_same_scope()
    {
        await using var provider = Provider(services => services
            .AddCommandHandler<Restock, int, OnRestock>()
            .AddNotificationHandler<Restocked, OnRestocked>());
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        mediator.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<ISender>());
        await mediator.Send(new Restock("tea", 1), Cancellation);
        await mediator.Publish(new Restocked("tea"), Cancellation);

        scope.ServiceProvider.GetRequiredService<Steps>().All.Should().Equal("restock:tea", "restocked:tea");
    }

    [Fact]
    public async Task A_message_without_a_handler_is_refused_with_its_type()
    {
        await using var provider = Provider(_ => { });
        await using var scope = provider.CreateAsyncScope();

        var act = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Restock("tea", 1), Cancellation);

        var refused = (await act.Should().ThrowAsync<MissingMessageHandlerException>()).Which;
        refused.Message.Should().Contain(typeof(Restock).FullName!);
        refused.MediatorMessage.Should().Be(new Restock("tea", 1));
    }

    [Fact]
    public async Task An_object_that_is_no_request_is_refused()
    {
        await using var provider = Provider(services => services.AddNotificationHandler<Restocked, OnRestocked>());
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var send = async () => await sender.Send(new Restocked("tea"), Cancellation);
        var stream = () => sender.CreateStream((object)new Restock("tea", 1), Cancellation);

        (await send.Should().ThrowAsync<InvalidMessageException>()).Which.Message.Should().Contain(typeof(Restocked).FullName!);
        stream.Should().Throw<InvalidMessageException>("a command answered once is no stream");
    }

    [Fact]
    public void A_second_handler_of_one_message_is_refused_and_the_same_handler_again_changes_nothing()
    {
        var services = new ServiceCollection()
            .AddCommandHandler<Restock, int, OnRestock>()
            .AddCommandHandler<Restock, int, OnRestock>();

        var second = () => services.AddCommandHandler<Restock, int, RestockTwice>();

        second.Should().Throw<InvalidOperationException>()
            .WithMessage($"'{typeof(Restock).FullName}' has a handler already, '{typeof(OnRestock).FullName}'.*'{typeof(RestockTwice).FullName}'*");
        services.Count(descriptor => descriptor.ServiceType == typeof(ICommandHandler<Restock, int>)).Should().Be(1);
    }

    [Fact]
    public async Task A_handler_of_a_base_type_handles_what_derives_from_it()
    {
        await using var provider = Provider(services => services.AddCommandHandler<StockCommand, int, OnAnyStockCommand>());
        await using var scope = provider.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Discard("tea"), Cancellation)).Should().Be(1);
    }

    [Fact]
    public async Task An_open_behavior_runs_around_the_handler_and_the_first_registered_is_the_outermost()
    {
        var steps = await SendAsync(new Restock("tea", 2), services => services
            .AddCommandHandler<Restock, int, OnRestock>()
            .AddPipelineBehavior(typeof(Outer<,>))
            .AddPipelineBehavior(typeof(Inner<,>)));

        steps.Should().Equal("outer>", "inner>", "restock:tea", "<inner", "<outer");
    }

    [Fact]
    public async Task An_open_behavior_with_a_constraint_runs_only_around_the_messages_that_meet_it()
    {
        var steps = await SendAsync(new CountStock("tea"), services => services
            .AddQueryHandler<CountStock, int, OnCountStock>()
            .AddCommandHandler<Restock, int, OnRestock>()
            .AddPipelineBehavior(typeof(OnlyAroundCommands<,>)));

        var commandSteps = await SendAsync(new Restock("tea", 1), services => services
            .AddCommandHandler<Restock, int, OnRestock>()
            .AddPipelineBehavior(typeof(OnlyAroundCommands<,>)));

        steps.Should().Equal("count:tea");
        commandSteps.Should().Equal("command>", "restock:tea");
    }

    [Fact]
    public async Task A_closed_behavior_runs_only_around_its_message()
    {
        var restock = await SendAsync(new Restock("tea", 1), services => services
            .AddCommandHandler<Restock, int, OnRestock>()
            .AddPipelineBehavior(typeof(AroundRestock)));
        var count = await SendAsync(new CountStock("tea"), services => services
            .AddQueryHandler<CountStock, int, OnCountStock>()
            .AddPipelineBehavior(typeof(AroundRestock)));

        restock.Should().Equal("around restock>", "restock:tea");
        count.Should().Equal("count:tea");
    }

    [Fact]
    public async Task A_behavior_that_calls_next_twice_runs_everything_inside_it_twice()
    {
        var steps = await SendAsync(new Restock("tea", 1), services => services
            .AddCommandHandler<Restock, int, OnRestock>()
            .AddPipelineBehavior(typeof(Twice<,>))
            .AddPipelineBehavior(typeof(Inner<,>)));

        steps.Should().Equal("inner>", "restock:tea", "<inner", "inner>", "restock:tea", "<inner");
    }

    [Fact]
    public void A_behavior_type_that_is_no_behavior_or_takes_its_parameters_the_other_way_round_is_refused()
    {
        var none = () => new ServiceCollection().AddPipelineBehavior(typeof(OnRestock));
        var reversed = () => new ServiceCollection().AddPipelineBehavior(typeof(Reversed<,>));

        none.Should().Throw<ArgumentException>().WithMessage("*OnRestock*");
        reversed.Should().Throw<ArgumentException>().WithMessage("*Reversed*the message first and the response second*");
    }

    [Fact]
    public async Task A_stream_query_streams_its_handler_s_items_through_its_behaviors()
    {
        await using var provider = Provider(services => services
            .AddStreamQueryHandler<ListStock, string, OnListStock>()
            .AddPipelineBehavior(typeof(Outer<,>))
            .AddStreamPipelineBehavior(typeof(CountItems<,>)));
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var items = new List<string>();
        await foreach (var item in sender.CreateStream(new ListStock(), Cancellation))
        {
            items.Add(item);
        }

        items.Should().Equal("tea", "coffee");
        scope.ServiceProvider.GetRequiredService<Steps>().All.Should().Equal(
            ["list", "item:tea", "item:coffee"],
            "the stream passes the behaviors of streams, and no behavior of the other pipeline");
    }

    [Fact]
    public async Task A_stream_command_and_a_stream_request_stream_as_well_and_as_objects()
    {
        await using var provider = Provider(services => services
            .AddStreamCommandHandler<Unpack, int, OnUnpack>()
            .AddStreamRequestHandler<Watch, int, OnWatch>());
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var unpacked = new List<int>();
        await foreach (var item in sender.CreateStream(new Unpack(2), Cancellation))
        {
            unpacked.Add(item);
        }

        var watched = new List<object?>();
        await foreach (var item in sender.CreateStream((object)new Watch(), Cancellation))
        {
            watched.Add(item);
        }

        unpacked.Should().Equal(1, 2);
        watched.Should().Equal(5);
    }

    [Fact]
    public async Task A_handler_registered_by_the_generated_registration_takes_the_scope_s_services()
    {
        // The generated AddMediatorTestsHandlers() registers this project's public handlers, a request handler among
        // them, so a module's code calls one line.
        var services = new ServiceCollection().AddSingleton(new Requests.StepLog());
        services.AddMediatorTestsHandlers();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Requests.Ping(), Cancellation)).Should().Be(Unit.Value);
        scope.ServiceProvider.GetRequiredService<ICommandHandler<Requests.Ping, Unit>>().Should().BeOfType<Requests.PingHandler>();
    }

    private static ServiceProvider Provider(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Steps());
        services.AddDDDToolkitMediator();
        register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<IReadOnlyList<string>> SendAsync(object message, Action<IServiceCollection> register)
    {
        await using var provider = Provider(register);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(message, Cancellation);
        return scope.ServiceProvider.GetRequiredService<Steps>().All;
    }

    private sealed class Steps
    {
        private readonly List<string> _all = [];

        public IReadOnlyList<string> All => _all;

        public void Add(string step) => _all.Add(step);
    }

    private sealed record Restock(string Item, int Count) : ICommand<int>;

    private sealed record CountStock(string Item) : IQuery<int>;

    private sealed record Ping : IRequest;

    private sealed record Restocked(string Item) : INotification;

    private abstract record StockCommand(string Item) : ICommand<int>;

    private sealed record Discard(string Item) : StockCommand(Item);

    private sealed record ListStock : IStreamQuery<string>;

    private sealed record Unpack(int Count) : IStreamCommand<int>;

    private sealed record Watch : IStreamRequest<int>;

    private sealed class OnRestock(Steps steps) : ICommandHandler<Restock, int>
    {
        public ValueTask<int> Handle(Restock command, CancellationToken cancellationToken)
        {
            steps.Add("restock:" + command.Item);
            return ValueTask.FromResult(command.Count);
        }
    }

    private sealed class RestockTwice : ICommandHandler<Restock, int>
    {
        public ValueTask<int> Handle(Restock command, CancellationToken cancellationToken) => ValueTask.FromResult(command.Count * 2);
    }

    private sealed class OnCountStock(Steps steps) : IQueryHandler<CountStock, int>
    {
        public ValueTask<int> Handle(CountStock query, CancellationToken cancellationToken)
        {
            steps.Add("count:" + query.Item);
            return ValueTask.FromResult(7);
        }
    }

    private sealed class OnPing(Steps steps) : IRequestHandler<Ping>
    {
        public ValueTask<Unit> Handle(Ping request, CancellationToken cancellationToken)
        {
            steps.Add("ping");
            return Unit.ValueTask;
        }
    }

    private sealed class OnRestocked(Steps steps) : INotificationHandler<Restocked>
    {
        public ValueTask Handle(Restocked notification, CancellationToken cancellationToken)
        {
            steps.Add("restocked:" + notification.Item);
            return default;
        }
    }

    private sealed class OnAnyStockCommand : ICommandHandler<StockCommand, int>
    {
        public ValueTask<int> Handle(StockCommand command, CancellationToken cancellationToken) => ValueTask.FromResult(1);
    }

    private sealed class OnListStock(Steps steps) : IStreamQueryHandler<ListStock, string>
    {
        public async IAsyncEnumerable<string> Handle(ListStock query, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            steps.Add("list");
            yield return "tea";
            await Task.Yield();
            yield return "coffee";
        }
    }

    private sealed class OnUnpack : IStreamCommandHandler<Unpack, int>
    {
        public async IAsyncEnumerable<int> Handle(Unpack command, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var item = 1; item <= command.Count; item++)
            {
                await Task.Yield();
                yield return item;
            }
        }
    }

    private sealed class OnWatch : IStreamRequestHandler<Watch, int>
    {
        public async IAsyncEnumerable<int> Handle(Watch request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return 5;
        }
    }

    private sealed class Outer<TMessage, TResponse>(Steps steps) : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
        {
            steps.Add("outer>");
            var response = await next(message, cancellationToken);
            steps.Add("<outer");
            return response;
        }
    }

    private sealed class Inner<TMessage, TResponse>(Steps steps) : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
        {
            steps.Add("inner>");
            var response = await next(message, cancellationToken);
            steps.Add("<inner");
            return response;
        }
    }

    private sealed class OnlyAroundCommands<TMessage, TResponse>(Steps steps) : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IBaseCommand
    {
        public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
        {
            steps.Add("command>");
            return next(message, cancellationToken);
        }
    }

    private sealed class AroundRestock(Steps steps) : IPipelineBehavior<Restock, int>
    {
        public ValueTask<int> Handle(Restock message, MessageHandlerDelegate<Restock, int> next, CancellationToken cancellationToken)
        {
            steps.Add("around restock>");
            return next(message, cancellationToken);
        }
    }

    private sealed class Twice<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
        {
            await next(message, cancellationToken);
            return await next(message, cancellationToken);
        }
    }

    private sealed class Reversed<TResponse, TMessage> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
            => next(message, cancellationToken);
    }

    private sealed class CountItems<TMessage, TResponse>(Steps steps) : IStreamPipelineBehavior<TMessage, TResponse>
        where TMessage : IStreamMessage
    {
        public async IAsyncEnumerable<TResponse> Handle(TMessage message, StreamHandlerDelegate<TMessage, TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var item in next(message, cancellationToken).WithCancellation(cancellationToken))
            {
                steps.Add("item:" + item);
                yield return item;
            }
        }
    }
}
