using DDDToolkit.EntityFramework.Options;
using DDDToolkit.Interfaces;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Mediator;

/// <summary>
/// Dispatches DDDToolkit domain events through
/// <see href="https://github.com/martinothamar/Mediator">Mediator</see>.
/// <code>
/// builder.Services.AddMediator(options =&gt; options.ServiceLifetime = ServiceLifetime.Scoped);
/// builder.Services.AddDDDToolkitEntityFramework(options =&gt; options.DispatchWithMediator());
/// </code>
/// The toolkit itself has no mediator dependency: in-process dispatch is a delegate, and this package
/// only writes that delegate for you.
/// </summary>
public static class MediatorDispatchExtensions
{
    /// <summary>
    /// Delivers domain events by resolving <see cref="IPublisher"/> from the scope that owns the saving
    /// <c>DbContext</c> and publishing each event, in the order it was raised, awaiting one before
    /// starting the next.
    /// <para>
    /// This is the same delegate as <see cref="DDDEntityFrameworkOptions.DispatchInProcess"/>, so it
    /// works for both delivery modes: on its own the handlers run inside <c>SaveChanges</c>, and
    /// combined with <see cref="DDDEntityFrameworkOptions.UseOutbox"/> it is what the outbox processor
    /// delivers through.
    /// </para>
    /// <para>
    /// Every event must implement <see cref="INotification"/>; Mediator cannot publish anything else.
    /// One that does not makes the dispatch throw an <see cref="InvalidOperationException"/> naming the
    /// event type rather than skipping it, because a skipped event is a lost event: the interceptor has
    /// already dequeued it from the aggregate and nothing records that it was dropped. The usual fix is
    /// a shared marker interface, <c>interface IMyDomainEvent : IDomainEvent, INotification</c>, that
    /// every event in the solution implements.
    /// </para>
    /// <para>
    /// Register Mediator with <c>ServiceLifetime.Scoped</c> if your handlers inject the
    /// <c>DbContext</c>. Mediator's source generator defaults every handler to a singleton, and a
    /// singleton cannot take a scoped dependency.
    /// </para>
    /// </summary>
    /// <param name="options">The DDDToolkit Entity Framework options being configured.</param>
    /// <returns>The same options, so calls chain.</returns>
    public static DDDEntityFrameworkOptions DispatchWithMediator(this DDDEntityFrameworkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.DispatchInProcess(PublishAsync);
    }

    private static async Task PublishAsync(IServiceProvider services, IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        var publisher = services.GetRequiredService<IPublisher>();

        foreach (var domainEvent in events)
        {
            if (domainEvent is not INotification)
            {
                throw new InvalidOperationException(
                    $"Domain event '{domainEvent.GetType().FullName}' does not implement {typeof(INotification).FullName}, so Mediator cannot publish it. " +
                    $"Let the event implement {nameof(INotification)} (a shared marker interface deriving from both {nameof(IDomainEvent)} and {nameof(INotification)} is the usual way), " +
                    $"or replace {nameof(DispatchWithMediator)}() with your own {nameof(DDDEntityFrameworkOptions.DispatchInProcess)}(...) delegate.");
            }

            // The static type here is IDomainEvent, which does not satisfy the generic overload's
            // constraint, so this binds to Publish(object, CancellationToken). Mediator switches on the
            // runtime type, so the handlers registered for the concrete event type are the ones that run.
            await publisher.Publish(domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
