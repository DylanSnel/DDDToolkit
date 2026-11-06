using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DTS.DDD.Mediator;

public static partial class MediatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the handler of <typeparamref name="TRequest"/>. The handler is
    /// scoped, unless the container already has a registration of it, and it is also registered as
    /// <see cref="IRequestHandler{TRequest, TResponse}"/>. Registering the same pair again changes nothing.
    /// </summary>
    /// <typeparam name="TRequest">The request the handler is declared for.</typeparam>
    /// <typeparam name="TResponse">What the handler answers with.</typeparam>
    /// <typeparam name="THandler">The handler class.</typeparam>
    /// <param name="services">The container.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Another handler of <typeparamref name="TRequest"/> is registered already.</exception>
    public static IServiceCollection AddRequestHandler<TRequest, TResponse, THandler>(this IServiceCollection services)
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
        => AddHandler<TRequest, TResponse, THandler, IRequestHandler<TRequest, TResponse>>(
            services,
            new Func<IServiceProvider, object, CancellationToken, ValueTask<TResponse>>(static (provider, message, cancellationToken) => SendToRequestHandler<TRequest, TResponse, THandler>(provider, (TRequest)message, cancellationToken)),
            static (provider, message, cancellationToken) => Boxed(SendToRequestHandler<TRequest, TResponse, THandler>(provider, (TRequest)message, cancellationToken)),
            stream: null);

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the handler of <typeparamref name="TCommand"/>. The handler is
    /// scoped, unless the container already has a registration of it, and it is also registered as
    /// <see cref="ICommandHandler{TCommand, TResponse}"/>. Registering the same pair again changes nothing.
    /// </summary>
    /// <typeparam name="TCommand">The command the handler is declared for.</typeparam>
    /// <typeparam name="TResponse">What the handler answers with.</typeparam>
    /// <typeparam name="THandler">The handler class.</typeparam>
    /// <param name="services">The container.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Another handler of <typeparamref name="TCommand"/> is registered already.</exception>
    public static IServiceCollection AddCommandHandler<TCommand, TResponse, THandler>(this IServiceCollection services)
        where TCommand : ICommand<TResponse>
        where THandler : class, ICommandHandler<TCommand, TResponse>
        => AddHandler<TCommand, TResponse, THandler, ICommandHandler<TCommand, TResponse>>(
            services,
            new Func<IServiceProvider, object, CancellationToken, ValueTask<TResponse>>(static (provider, message, cancellationToken) => SendToCommandHandler<TCommand, TResponse, THandler>(provider, (TCommand)message, cancellationToken)),
            static (provider, message, cancellationToken) => Boxed(SendToCommandHandler<TCommand, TResponse, THandler>(provider, (TCommand)message, cancellationToken)),
            stream: null);

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the handler of <typeparamref name="TQuery"/>. The handler is
    /// scoped, unless the container already has a registration of it, and it is also registered as
    /// <see cref="IQueryHandler{TQuery, TResponse}"/>. Registering the same pair again changes nothing.
    /// </summary>
    /// <typeparam name="TQuery">The query the handler is declared for.</typeparam>
    /// <typeparam name="TResponse">What the handler answers with.</typeparam>
    /// <typeparam name="THandler">The handler class.</typeparam>
    /// <param name="services">The container.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Another handler of <typeparamref name="TQuery"/> is registered already.</exception>
    public static IServiceCollection AddQueryHandler<TQuery, TResponse, THandler>(this IServiceCollection services)
        where TQuery : IQuery<TResponse>
        where THandler : class, IQueryHandler<TQuery, TResponse>
        => AddHandler<TQuery, TResponse, THandler, IQueryHandler<TQuery, TResponse>>(
            services,
            new Func<IServiceProvider, object, CancellationToken, ValueTask<TResponse>>(static (provider, message, cancellationToken) => SendToQueryHandler<TQuery, TResponse, THandler>(provider, (TQuery)message, cancellationToken)),
            static (provider, message, cancellationToken) => Boxed(SendToQueryHandler<TQuery, TResponse, THandler>(provider, (TQuery)message, cancellationToken)),
            stream: null);

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the handler of the stream request <typeparamref name="TRequest"/>.
    /// It is registered as <see cref="AddRequestHandler{TRequest, TResponse, THandler}"/> registers a handler.
    /// </summary>
    /// <typeparam name="TRequest">The request the handler is declared for.</typeparam>
    /// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
    /// <typeparam name="THandler">The handler class.</typeparam>
    /// <param name="services">The container.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Another handler of <typeparamref name="TRequest"/> is registered already.</exception>
    public static IServiceCollection AddStreamRequestHandler<TRequest, TResponse, THandler>(this IServiceCollection services)
        where TRequest : IStreamRequest<TResponse>
        where THandler : class, IStreamRequestHandler<TRequest, TResponse>
        => AddHandler<TRequest, TResponse, THandler, IStreamRequestHandler<TRequest, TResponse>>(
            services,
            new Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<TResponse>>(static (provider, message, cancellationToken) => StreamFromRequestHandler<TRequest, TResponse, THandler>(provider, (TRequest)message, cancellationToken)),
            send: null,
            static (provider, message, cancellationToken) => BoxedEach(StreamFromRequestHandler<TRequest, TResponse, THandler>(provider, (TRequest)message, cancellationToken)));

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the handler of the stream command <typeparamref name="TCommand"/>.
    /// It is registered as <see cref="AddCommandHandler{TCommand, TResponse, THandler}"/> registers a handler.
    /// </summary>
    /// <typeparam name="TCommand">The command the handler is declared for.</typeparam>
    /// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
    /// <typeparam name="THandler">The handler class.</typeparam>
    /// <param name="services">The container.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Another handler of <typeparamref name="TCommand"/> is registered already.</exception>
    public static IServiceCollection AddStreamCommandHandler<TCommand, TResponse, THandler>(this IServiceCollection services)
        where TCommand : IStreamCommand<TResponse>
        where THandler : class, IStreamCommandHandler<TCommand, TResponse>
        => AddHandler<TCommand, TResponse, THandler, IStreamCommandHandler<TCommand, TResponse>>(
            services,
            new Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<TResponse>>(static (provider, message, cancellationToken) => StreamFromCommandHandler<TCommand, TResponse, THandler>(provider, (TCommand)message, cancellationToken)),
            send: null,
            static (provider, message, cancellationToken) => BoxedEach(StreamFromCommandHandler<TCommand, TResponse, THandler>(provider, (TCommand)message, cancellationToken)));

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the handler of the stream query <typeparamref name="TQuery"/>.
    /// It is registered as <see cref="AddQueryHandler{TQuery, TResponse, THandler}"/> registers a handler.
    /// </summary>
    /// <typeparam name="TQuery">The query the handler is declared for.</typeparam>
    /// <typeparam name="TResponse">The type of each item of the stream.</typeparam>
    /// <typeparam name="THandler">The handler class.</typeparam>
    /// <param name="services">The container.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Another handler of <typeparamref name="TQuery"/> is registered already.</exception>
    public static IServiceCollection AddStreamQueryHandler<TQuery, TResponse, THandler>(this IServiceCollection services)
        where TQuery : IStreamQuery<TResponse>
        where THandler : class, IStreamQueryHandler<TQuery, TResponse>
        => AddHandler<TQuery, TResponse, THandler, IStreamQueryHandler<TQuery, TResponse>>(
            services,
            new Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<TResponse>>(static (provider, message, cancellationToken) => StreamFromQueryHandler<TQuery, TResponse, THandler>(provider, (TQuery)message, cancellationToken)),
            send: null,
            static (provider, message, cancellationToken) => BoxedEach(StreamFromQueryHandler<TQuery, TResponse, THandler>(provider, (TQuery)message, cancellationToken)));

    /// <summary>
    /// Registers a behavior that runs around the handlers of requests, commands and queries. An open generic type,
    /// such as <c>typeof(MyBehavior&lt;,&gt;)</c>, runs around the handler of every message that satisfies its
    /// constraints. A closed type runs around the handlers of the messages it implements
    /// <see cref="IPipelineBehavior{TMessage, TResponse}"/> for. The behavior registered first is the outermost.
    /// Registering the same behavior again changes nothing.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="behaviorType">The behavior class: open generic over the message and the response, in that order, or closed.</param>
    /// <param name="lifetime">The behavior's lifetime. Scoped by default, so it can take the scope's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="behaviorType"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="behaviorType"/> implements no <see cref="IPipelineBehavior{TMessage, TResponse}"/>, or is an open generic type whose type parameters are not the message and the response, in that order.</exception>
    public static IServiceCollection AddPipelineBehavior(this IServiceCollection services, Type behaviorType, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        => AddBehavior(services, behaviorType, typeof(IPipelineBehavior<,>), lifetime);

    /// <summary>
    /// Registers a behavior that runs around the handlers of stream requests, stream commands and stream queries, as
    /// <see cref="AddPipelineBehavior"/> registers one around the other handlers.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="behaviorType">The behavior class: open generic over the message and the item type, in that order, or closed.</param>
    /// <param name="lifetime">The behavior's lifetime. Scoped by default, so it can take the scope's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="behaviorType"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="behaviorType"/> implements no <see cref="IStreamPipelineBehavior{TMessage, TResponse}"/>, or is an open generic type whose type parameters are not the message and the item type, in that order.</exception>
    public static IServiceCollection AddStreamPipelineBehavior(this IServiceCollection services, Type behaviorType, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        => AddBehavior(services, behaviorType, typeof(IStreamPipelineBehavior<,>), lifetime);

    private static IServiceCollection AddHandler<TMessage, TResponse, THandler, THandlerInterface>(
        IServiceCollection services,
        Delegate typed,
        Func<IServiceProvider, object, CancellationToken, ValueTask<object?>>? send,
        Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<object?>>? stream)
        where THandler : class, THandlerInterface
        where THandlerInterface : class
    {
        ArgumentNullException.ThrowIfNull(services);

        var streamed = stream is not null;
        foreach (var descriptor in services)
        {
            if (!descriptor.IsKeyedService
                && descriptor.ImplementationInstance is RequestHandlerRegistration registered
                && registered.MessageType == typeof(TMessage)
                && registered.Streamed == streamed)
            {
                if (registered.HandlerType == typeof(THandler))
                {
                    return services;
                }

                throw new InvalidOperationException(
                    $"'{typeof(TMessage).FullName}' has a handler already, '{registered.HandlerType.FullName}'. A message has one handler, so '{typeof(THandler).FullName}' is not registered for it as well.");
            }
        }

        services.AddDDDToolkitMediator();
        services.TryAddScoped<THandler>();
        services.TryAddScoped<THandlerInterface>(static provider => provider.GetRequiredService<THandler>());
        services.AddSingleton(new RequestHandlerRegistration(typeof(TMessage), typeof(TResponse), typeof(THandler), streamed, typed, send, stream));
        return services;
    }

    private static IServiceCollection AddBehavior(IServiceCollection services, Type behaviorType, Type pipeline, ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(behaviorType);

        var behaviors = Array.FindAll(
            behaviorType.GetInterfaces(),
            type => type.IsGenericType && type.GetGenericTypeDefinition() == pipeline);

        var name = pipeline.Name[..pipeline.Name.IndexOf('`', StringComparison.Ordinal)];
        if (behaviors.Length == 0 || behaviorType.IsAbstract)
        {
            throw new ArgumentException(
                $"'{behaviorType.FullName}' is no class that implements {name}<TMessage, TResponse>.",
                nameof(behaviorType));
        }

        services.AddDDDToolkitMediator();

        if (behaviorType.IsGenericTypeDefinition)
        {
            // The container closes an open registration by position: the class's first type parameter becomes the
            // message and its second the response. A class declared the other way round would be closed wrong.
            var parameters = behaviorType.GetGenericArguments();
            if (parameters.Length != 2 || !behaviors.Any(behavior => behavior.GenericTypeArguments.SequenceEqual(parameters)))
            {
                throw new ArgumentException(
                    $"'{behaviorType.FullName}' is no open {name}<TMessage, TResponse> over its own two type parameters, the message first and the response second.",
                    nameof(behaviorType));
            }

            services.TryAddEnumerable(new ServiceDescriptor(pipeline, behaviorType, lifetime));
            return services;
        }

        foreach (var behavior in behaviors)
        {
            services.TryAddEnumerable(new ServiceDescriptor(behavior, behaviorType, lifetime));
        }

        return services;
    }

    private static ValueTask<TResponse> SendToRequestHandler<TRequest, TResponse, THandler>(IServiceProvider provider, TRequest request, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        var handler = provider.GetRequiredService<THandler>();
        var behaviors = PipelineOf<TRequest, TResponse>(provider);
        return behaviors.Length == 0 ? handler.Handle(request, cancellationToken) : Around(behaviors, handler.Handle, request, cancellationToken);
    }

    private static ValueTask<TResponse> SendToCommandHandler<TCommand, TResponse, THandler>(IServiceProvider provider, TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand<TResponse>
        where THandler : class, ICommandHandler<TCommand, TResponse>
    {
        var handler = provider.GetRequiredService<THandler>();
        var behaviors = PipelineOf<TCommand, TResponse>(provider);
        return behaviors.Length == 0 ? handler.Handle(command, cancellationToken) : Around(behaviors, handler.Handle, command, cancellationToken);
    }

    private static ValueTask<TResponse> SendToQueryHandler<TQuery, TResponse, THandler>(IServiceProvider provider, TQuery query, CancellationToken cancellationToken)
        where TQuery : IQuery<TResponse>
        where THandler : class, IQueryHandler<TQuery, TResponse>
    {
        var handler = provider.GetRequiredService<THandler>();
        var behaviors = PipelineOf<TQuery, TResponse>(provider);
        return behaviors.Length == 0 ? handler.Handle(query, cancellationToken) : Around(behaviors, handler.Handle, query, cancellationToken);
    }

    private static IAsyncEnumerable<TResponse> StreamFromRequestHandler<TRequest, TResponse, THandler>(IServiceProvider provider, TRequest request, CancellationToken cancellationToken)
        where TRequest : IStreamRequest<TResponse>
        where THandler : class, IStreamRequestHandler<TRequest, TResponse>
    {
        var handler = provider.GetRequiredService<THandler>();
        var behaviors = StreamPipelineOf<TRequest, TResponse>(provider);
        return behaviors.Length == 0 ? handler.Handle(request, cancellationToken) : Around(behaviors, handler.Handle, request, cancellationToken);
    }

    private static IAsyncEnumerable<TResponse> StreamFromCommandHandler<TCommand, TResponse, THandler>(IServiceProvider provider, TCommand command, CancellationToken cancellationToken)
        where TCommand : IStreamCommand<TResponse>
        where THandler : class, IStreamCommandHandler<TCommand, TResponse>
    {
        var handler = provider.GetRequiredService<THandler>();
        var behaviors = StreamPipelineOf<TCommand, TResponse>(provider);
        return behaviors.Length == 0 ? handler.Handle(command, cancellationToken) : Around(behaviors, handler.Handle, command, cancellationToken);
    }

    private static IAsyncEnumerable<TResponse> StreamFromQueryHandler<TQuery, TResponse, THandler>(IServiceProvider provider, TQuery query, CancellationToken cancellationToken)
        where TQuery : IStreamQuery<TResponse>
        where THandler : class, IStreamQueryHandler<TQuery, TResponse>
    {
        var handler = provider.GetRequiredService<THandler>();
        var behaviors = StreamPipelineOf<TQuery, TResponse>(provider);
        return behaviors.Length == 0 ? handler.Handle(query, cancellationToken) : Around(behaviors, handler.Handle, query, cancellationToken);
    }

    // The container hands enumerables out as arrays. With no behavior the handler is called directly, and nothing is
    // allocated for the call.
    private static IPipelineBehavior<TMessage, TResponse>[] PipelineOf<TMessage, TResponse>(IServiceProvider provider)
        where TMessage : notnull, IMessage
    {
        var resolved = provider.GetServices<IPipelineBehavior<TMessage, TResponse>>();
        return resolved as IPipelineBehavior<TMessage, TResponse>[] ?? [.. resolved];
    }

    private static IStreamPipelineBehavior<TMessage, TResponse>[] StreamPipelineOf<TMessage, TResponse>(IServiceProvider provider)
        where TMessage : IStreamMessage
    {
        var resolved = provider.GetServices<IStreamPipelineBehavior<TMessage, TResponse>>();
        return resolved as IStreamPipelineBehavior<TMessage, TResponse>[] ?? [.. resolved];
    }

    // Built from the inside out, so the behavior registered first is the outermost. Each step is a delegate of its
    // own, so a behavior that calls next twice, such as a retry, runs everything inside it twice.
    private static ValueTask<TResponse> Around<TMessage, TResponse>(IPipelineBehavior<TMessage, TResponse>[] behaviors, MessageHandlerDelegate<TMessage, TResponse> handler, TMessage message, CancellationToken cancellationToken)
        where TMessage : notnull, IMessage
    {
        var next = handler;
        for (var index = behaviors.Length - 1; index >= 0; index--)
        {
            var behavior = behaviors[index];
            var inner = next;
            next = (current, token) => behavior.Handle(current, inner, token);
        }

        return next(message, cancellationToken);
    }

    private static IAsyncEnumerable<TResponse> Around<TMessage, TResponse>(IStreamPipelineBehavior<TMessage, TResponse>[] behaviors, StreamHandlerDelegate<TMessage, TResponse> handler, TMessage message, CancellationToken cancellationToken)
        where TMessage : IStreamMessage
    {
        var next = handler;
        for (var index = behaviors.Length - 1; index >= 0; index--)
        {
            var behavior = behaviors[index];
            var inner = next;
            next = (current, token) => behavior.Handle(current, inner, token);
        }

        return next(message, cancellationToken);
    }

    private static async ValueTask<object?> Boxed<TResponse>(ValueTask<TResponse> response) => await response.ConfigureAwait(false);

    private static async IAsyncEnumerable<object?> BoxedEach<TResponse>(IAsyncEnumerable<TResponse> items)
    {
        await foreach (var item in items.ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
