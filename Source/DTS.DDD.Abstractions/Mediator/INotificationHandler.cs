#if NET
namespace DTS.DDD.Mediator;

/// <summary>
/// Handles one kind of notification. Implement it once per notification type the class reacts to.
/// <code>
/// internal sealed class SendWelcome(IMailer mailer) : INotificationHandler&lt;SeatAdded&lt;TenantId, SeatId&gt;&gt;
/// {
///     public ValueTask Handle(SeatAdded&lt;TenantId, SeatId&gt; notification, CancellationToken cancellationToken)
///         =&gt; mailer.SendWelcomeAsync(notification.SeatId, cancellationToken);
/// }
/// </code>
/// <para>
/// The toolkit's generator registers every handler of a project in <c>Add{Module}Handlers()</c>. A handler may be
/// internal. It is resolved from the scope that publishes, so it can take the scope's <c>DbContext</c>.
/// </para>
/// <para>
/// A handler declared for a base type or an interface also runs for every notification that derives from it.
/// </para>
/// </summary>
/// <typeparam name="TNotification">The notification this handler reacts to.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>Reacts to <paramref name="notification"/>.</summary>
    /// <param name="notification">The notification that was published.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    ValueTask Handle(TNotification notification, CancellationToken cancellationToken);
}
#endif
