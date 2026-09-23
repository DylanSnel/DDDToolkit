namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// What this process handles and what it publishes itself, by published contract name, gathered from the
/// modules' registrations. A transport reads <see cref="FromElsewhere"/> to ask for exactly the messages
/// other services send this one, in its own terms: the queue bindings of a broker, the topic bindings of a
/// pgmq queue, the handlers Wolverine listens for.
/// <para>
/// A contract this process publishes itself is left out of <see cref="FromElsewhere"/> even when one of its
/// modules handles it. The module sink already hands it to that module; asking the broker for it too would
/// deliver every such message twice.
/// </para>
/// <para>
/// The names come from the generated <c>module.Add{Module}IntegrationEvents()</c> and
/// <c>outbox.Add{Module}IntegrationEvents()</c>, which wrote them down when the modules compiled, and the
/// contract types travel as generic arguments to <see cref="VisitFromElsewhere"/>. Filling this and reading
/// it takes no reflection.
/// </para>
/// <para>
/// One instance per service collection. Get it with <c>services.IntegrationEventSubscriptions()</c>, after
/// the modules have registered, or resolve it from the container.
/// </para>
/// </summary>
public sealed class IntegrationEventSubscriptions
{
    private readonly SortedDictionary<string, Action<IIntegrationEventContractVisitor>> _handled = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _published = new(StringComparer.Ordinal);

    /// <summary>The published names of the contracts this process has a handler for, each once, in ordinal order.</summary>
    public IReadOnlyCollection<string> Handled => [.. _handled.Keys];

    /// <summary>The published names of the contracts this process's outboxes publish, each once, in ordinal order.</summary>
    public IReadOnlyCollection<string> Published => [.. _published];

    /// <summary>
    /// The contracts this process handles and does not publish itself: what it has to be sent by other
    /// services. Each once, in ordinal order.
    /// </summary>
    public IReadOnlyCollection<string> FromElsewhere => [.. _handled.Keys.Where(name => !_published.Contains(name))];

    /// <summary>
    /// Calls <see cref="IIntegrationEventContractVisitor.Visit{TContract}"/> once for every contract in
    /// <see cref="FromElsewhere"/>, with the contract type as its generic argument. This is how a transport
    /// that routes by type, MassTransit or Wolverine, registers a consumer per contract without being handed
    /// a <see cref="Type"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is null.</exception>
    public void VisitFromElsewhere(IIntegrationEventContractVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        foreach (var (name, visit) in _handled)
        {
            if (!_published.Contains(name))
            {
                visit(visitor);
            }
        }
    }

    internal void Handles<TContract>(string name) where TContract : class
        => _handled.TryAdd(name, static visitor => visitor.Visit<TContract>());

    internal void Publishes(string name) => _published.Add(name);
}

/// <summary>
/// Receives each contract type of <see cref="IntegrationEventSubscriptions.VisitFromElsewhere"/> as a
/// generic argument, so a transport can register what it needs per contract with ordinary generic calls.
/// </summary>
public interface IIntegrationEventContractVisitor
{
    /// <summary>Called once for each contract this process has to be sent.</summary>
    void Visit<TContract>() where TContract : class;
}
