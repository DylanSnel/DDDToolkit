using DTS.DDD.Exceptions;
using DTS.DDD.Results;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DTS.DDD.Mediator.Tests;

/// <summary>
/// A request whose response is a <see cref="Result"/> or <see cref="Result{T}"/>: a refusal that a behavior or the
/// handler throws comes back as a failed result, an invalid value object as an <see cref="InvalidError"/>, and every
/// failure is logged at the level of its error type. A request with another response still throws.
/// </summary>
public sealed class ResultBoundaryTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_returned_value_is_a_success()
    {
        await using var provider = Provider(services => services.AddCommandHandler<Reserve, Result<int>, OnReserve>());

        var result = await Send(provider, new Reserve("tea", 3));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(3);
    }

    [Fact]
    public async Task A_returned_error_is_a_failure_and_logged_at_its_level()
    {
        var log = new LogLines();
        await using var provider = Provider(services => services.AddCommandHandler<Reserve, Result<int>, OnReserve>(), log);

        var result = await Send(provider, new Reserve("gone", 1));

        result.Error.Should().BeOfType<NotFoundError>().Which.Code.Should().Be("stock.unknown");
        log.Lines.Should().ContainSingle().Which.Should().Be("Information: Reserve gave NotFoundError stock.unknown");
    }

    [Fact]
    public async Task A_refusal_a_behavior_throws_is_a_failure_of_its_kind_and_the_handler_does_not_run()
    {
        await using var provider = Provider(services => services
            .AddCommandHandler<Reserve, Result<int>, OnReserve>()
            .AddPipelineBehavior(typeof(RefuseEverything<,>)));

        var result = await Send(provider, new Reserve("tea", 3));

        result.Error.Should().BeOfType<NotPermittedError>().Which.Code.Should().Be("stock.closed");
        provider.GetRequiredService<Steps>().All.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refusal_the_handler_throws_keeps_its_code_and_arguments()
    {
        await using var provider = Provider(services => services.AddCommandHandler<Reserve, Result<int>, OnReserve>());

        var result = await Send(provider, new Reserve("tea", 0));

        var error = result.Error.Should().BeOfType<InvalidError>().Which;
        error.Code.Should().Be("stock.quantity-invalid");
        error.Field.Should().Be("quantity");
    }

    [Fact]
    public async Task An_invalid_value_object_is_an_invalid_error_with_its_failures()
    {
        await using var provider = Provider(services => services.AddCommandHandler<Reserve, Result<int>, OnReserve>());

        var result = await Send(provider, new Reserve("bad", 1));

        var error = result.Error.Should().BeOfType<InvalidError>().Which;
        error.Code.Should().Be(Errors.ValueInvalidCode);
        error.Failures.Should().ContainSingle().Which.Code.Should().Be("sku.format");
    }

    [Fact]
    public async Task A_result_without_a_value_fails_the_same_way()
    {
        await using var provider = Provider(services => services
            .AddCommandHandler<Cancel, Result, OnCancel>()
            .AddPipelineBehavior(typeof(RefuseEverything<,>)));

        var result = await provider.CreateAsyncScope().ServiceProvider.GetRequiredService<ISender>().Send(new Cancel(), Cancellation);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stock.closed");
    }

    [Fact]
    public async Task A_request_without_a_result_still_throws()
    {
        await using var provider = Provider(services => services
            .AddCommandHandler<Count, int, OnCount>()
            .AddPipelineBehavior(typeof(RefuseEverything<,>)));

        var send = async () => await provider.CreateAsyncScope().ServiceProvider.GetRequiredService<ISender>().Send(new Count(), Cancellation);

        await send.Should().ThrowAsync<RefusalException>();
    }

    [Fact]
    public async Task A_level_of_the_application_wins_and_none_leaves_the_failure_out()
    {
        var log = new LogLines();
        await using var provider = Provider(
            services => services
                .AddCommandHandler<Reserve, Result<int>, OnReserve>()
                .AddDDDToolkitResultLogging(options => options.Level<NotFoundError>(LogLevel.None).Level<InvalidError>(LogLevel.Debug)),
            log);

        _ = await Send(provider, new Reserve("gone", 1));
        _ = await Send(provider, new Reserve("tea", 0));

        log.Lines.Should().ContainSingle().Which.Should().Be("Debug: Reserve gave InvalidError stock.quantity-invalid");
    }

    private static async Task<Result<int>> Send(ServiceProvider provider, Reserve command)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Cancellation);
    }

    private static ServiceProvider Provider(Action<IServiceCollection> register, LogLines? log = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Steps());
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(log ?? new LogLines()));
        services.AddDDDToolkitMediator();
        register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed record Reserve(string Sku, int Quantity) : ICommand<Result<int>>;

    private sealed record Cancel : ICommand<Result>;

    private sealed record Count : ICommand<int>;

    private sealed class Steps
    {
        public List<string> All { get; } = [];
    }

    private sealed class OnReserve(Steps steps) : ICommandHandler<Reserve, Result<int>>
    {
        public ValueTask<Result<int>> Handle(Reserve command, CancellationToken cancellationToken)
        {
            steps.All.Add("reserve:" + command.Sku);
            Result<int> result = command switch
            {
                { Sku: "gone" } => new NotFoundError("stock.unknown", "No such stock."),
                { Sku: "bad" } => throw new InvalidValueObjectException(typeof(Reserve), [new("The SKU is malformed.", "Sku", "sku.format")]),
                { Quantity: <= 0 } => throw new RefusalException("stock.quantity-invalid", RefusalKind.Invalid, "The quantity must be positive.", new Dictionary<string, object?> { [RefusalException.FieldArgument] = "quantity" }),
                _ => command.Quantity,
            };
            return ValueTask.FromResult(result);
        }
    }

    private sealed class OnCancel : ICommandHandler<Cancel, Result>
    {
        public ValueTask<Result> Handle(Cancel command, CancellationToken cancellationToken) => ValueTask.FromResult(Result.Success());
    }

    private sealed class OnCount : ICommandHandler<Count, int>
    {
        public ValueTask<int> Handle(Count command, CancellationToken cancellationToken) => ValueTask.FromResult(1);
    }

    private sealed class RefuseEverything<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : notnull, IMessage
    {
        public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
            => throw new RefusalException("stock.closed", RefusalKind.NotPermitted, "The stock is closed.");
    }

    private sealed class LogLines : ILoggerProvider
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => categoryName == "DTS.DDD.Results" ? new Writer(Lines) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class Writer(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => lines.Add($"{logLevel}: {formatter(state, exception)}");
        }
    }
}