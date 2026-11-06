#if NET
namespace DDDToolkit.Mediator;

/// <summary>
/// Runs around each call of a notification handler. Register it with
/// <c>services.AddNotificationBehavior(typeof(MyBehavior&lt;&gt;))</c> for every notification, or closed over one type
/// for that type alone.
/// <code>
/// internal sealed class TimeTheHandler&lt;TNotification&gt;(ILogger&lt;TNotification&gt; logger)
///     : INotificationBehavior&lt;TNotification&gt;
///     where TNotification : INotification
/// {
///     public async ValueTask Handle(TNotification notification, NotificationHandlerDelegate&lt;TNotification&gt; next, CancellationToken cancellationToken)
///     {
///         var started = Stopwatch.GetTimestamp();
///         await next(notification, cancellationToken);
///         logger.LogDebug("{Notification} was handled in {Elapsed}", typeof(TNotification).Name, Stopwatch.GetElapsedTime(started));
///     }
/// }
/// </code>
/// <para>
/// A behavior is resolved for the type the handler is declared for, from the scope that publishes. A handler of
/// <c>SeatAdded&lt;TenantId, SeatId&gt;</c> runs inside every <c>INotificationBehavior&lt;SeatAdded&lt;TenantId, SeatId&gt;&gt;</c>.
/// An open generic behavior with a constraint applies only to the types that satisfy the constraint. The behavior
/// registered first is the outermost.
/// </para>
/// </summary>
/// <typeparam name="TNotification">The type the handler is declared for.</typeparam>
public interface INotificationBehavior<TNotification>
    where TNotification : INotification
{
    /// <summary>Runs around one handler call. Call <paramref name="next"/> to run the handler.</summary>
    /// <param name="notification">The notification being handled.</param>
    /// <param name="next">The next behavior, or the handler itself.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    ValueTask Handle(TNotification notification, NotificationHandlerDelegate<TNotification> next, CancellationToken cancellationToken);
}

/// <summary>The next step around a notification handler: another behavior, or the handler.</summary>
/// <typeparam name="TNotification">The type the handler is declared for.</typeparam>
/// <param name="notification">The notification being handled.</param>
/// <param name="cancellationToken">Cancels the handler.</param>
public delegate ValueTask NotificationHandlerDelegate<in TNotification>(TNotification notification, CancellationToken cancellationToken)
    where TNotification : INotification;
#endif
