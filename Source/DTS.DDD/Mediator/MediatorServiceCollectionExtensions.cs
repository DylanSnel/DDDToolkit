using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DTS.DDD.Mediator;

/// <summary>
/// Registers the toolkit's mediator: the scoped <see cref="IPublisher"/>, the handlers of each notification type
/// and the behaviors around them.
/// <para>
/// An application rarely calls these methods itself. The toolkit's generator writes <c>Add{Module}Handlers()</c>
/// into every project that has handlers, with one <see cref="AddNotificationHandler{TNotification, THandler}"/> per
/// handler and notification type. <c>AddDDDToolkitEntityFramework</c> registers the publisher.
/// </para>
/// </summary>
public static class MediatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers the scoped <see cref="IPublisher"/>. Calling it again changes nothing.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddDDDToolkitMediator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<NotificationRoutes>();
        services.TryAddScoped<IPublisher, Publisher>();
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as a handler of <typeparamref name="TNotification"/>. The handler is
    /// scoped, unless the container already has a registration of it. Registering the same pair again changes
    /// nothing.
    /// <para>
    /// The handler runs for every notification whose runtime type is <typeparamref name="TNotification"/> or derives
    /// from it. It runs inside every <see cref="INotificationBehavior{TNotification}"/> of
    /// <typeparamref name="TNotification"/>. The handlers of one notification run in the order they were registered.
    /// </para>
    /// </summary>
    /// <typeparam name="TNotification">The notification type the handler is declared for.</typeparam>
    /// <typeparam name="THandler">The handler class.</typeparam>
    /// <param name="services">The container.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddNotificationHandler<TNotification, THandler>(this IServiceCollection services)
        where TNotification : INotification
        where THandler : class, INotificationHandler<TNotification>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDDDToolkitMediator();
        services.TryAddScoped<THandler>();

        foreach (var descriptor in services)
        {
            if (!descriptor.IsKeyedService
                && descriptor.ImplementationInstance is NotificationHandlerRegistration registered
                && registered.NotificationType == typeof(TNotification)
                && registered.HandlerType == typeof(THandler))
            {
                return services;
            }
        }

        // The lambda is static, and closed over the two types of this call: it is made once per pair, and a
        // dispatch only casts the notification it is handed.
        services.AddSingleton(new NotificationHandlerRegistration(
            typeof(TNotification),
            typeof(THandler),
            static (provider, notification, cancellationToken) => Invoke<TNotification, THandler>(provider, (TNotification)notification, cancellationToken)));

        return services;
    }

    /// <summary>
    /// Registers a behavior that runs around notification handlers. An open generic type, such as
    /// <c>typeof(MyBehavior&lt;&gt;)</c>, runs around the handlers of every notification type that satisfies its
    /// constraints. A closed type runs around the handlers of the types it implements
    /// <see cref="INotificationBehavior{TNotification}"/> for. The behavior registered first is the outermost.
    /// Registering the same behavior again changes nothing.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="behaviorType">The behavior class, open generic with one type parameter, or closed.</param>
    /// <param name="lifetime">The behavior's lifetime. Scoped by default, so it can take the scope's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="behaviorType"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="behaviorType"/> implements no <see cref="INotificationBehavior{TNotification}"/>, or is an open generic type with more than one type parameter.</exception>
    public static IServiceCollection AddNotificationBehavior(this IServiceCollection services, Type behaviorType, ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(behaviorType);

        var behaviors = Array.FindAll(
            behaviorType.GetInterfaces(),
            static type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(INotificationBehavior<>));

        if (behaviors.Length == 0 || behaviorType.IsAbstract)
        {
            throw new ArgumentException(
                $"'{behaviorType.FullName}' is no class that implements {nameof(INotificationBehavior<INotification>)}<TNotification>.",
                nameof(behaviorType));
        }

        if (behaviorType.IsGenericTypeDefinition)
        {
            if (behaviorType.GetGenericArguments().Length != 1)
            {
                throw new ArgumentException(
                    $"'{behaviorType.FullName}' has more than one type parameter. An open generic behavior has one, the notification type.",
                    nameof(behaviorType));
            }

            services.AddDDDToolkitMediator();
            services.TryAddEnumerable(new ServiceDescriptor(typeof(INotificationBehavior<>), behaviorType, lifetime));
            return services;
        }

        services.AddDDDToolkitMediator();
        foreach (var behavior in behaviors)
        {
            services.TryAddEnumerable(new ServiceDescriptor(behavior, behaviorType, lifetime));
        }

        return services;
    }

    private static ValueTask Invoke<TNotification, THandler>(IServiceProvider provider, TNotification notification, CancellationToken cancellationToken)
        where TNotification : INotification
        where THandler : class, INotificationHandler<TNotification>
    {
        var handler = provider.GetRequiredService<THandler>();

        // The container hands enumerables out as arrays. With no behavior the handler is called directly, and
        // nothing is allocated for the call.
        var resolved = provider.GetServices<INotificationBehavior<TNotification>>();
        var behaviors = resolved as INotificationBehavior<TNotification>[] ?? [.. resolved];

        if (behaviors.Length == 0)
        {
            return handler.Handle(notification, cancellationToken);
        }

        // Built from the inside out, so the behavior registered first is the outermost. Each step is a delegate of
        // its own, so a behavior that calls next twice, such as a retry, runs everything inside it twice.
        NotificationHandlerDelegate<TNotification> next = handler.Handle;
        for (var index = behaviors.Length - 1; index >= 0; index--)
        {
            var behavior = behaviors[index];
            var inner = next;
            next = (current, token) => behavior.Handle(current, inner, token);
        }

        return next(notification, cancellationToken);
    }
}
