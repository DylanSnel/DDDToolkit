namespace DTS.DDD.Mediator;

/// <summary>
/// One handler of one notification type, as <see cref="MediatorServiceCollectionExtensions.AddNotificationHandler{TNotification, THandler}"/>
/// registers it. <see cref="Invoke"/> was closed over both types when the registration was written, so a dispatch
/// casts the notification and calls the handler without reflection.
/// </summary>
/// <param name="notificationType">The type the handler is declared for.</param>
/// <param name="handlerType">The handler class.</param>
/// <param name="invoke">Resolves the handler from a scope and calls it inside its behaviors.</param>
internal sealed class NotificationHandlerRegistration(
    Type notificationType,
    Type handlerType,
    Func<IServiceProvider, object, CancellationToken, ValueTask> invoke)
{
    /// <summary>The type the handler is declared for.</summary>
    public Type NotificationType { get; } = notificationType;

    /// <summary>The handler class.</summary>
    public Type HandlerType { get; } = handlerType;

    /// <summary>Resolves the handler from the scope it is given and calls it inside its behaviors.</summary>
    public Func<IServiceProvider, object, CancellationToken, ValueTask> Invoke { get; } = invoke;
}
