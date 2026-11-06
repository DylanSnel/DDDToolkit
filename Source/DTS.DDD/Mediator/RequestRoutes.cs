namespace DTS.DDD.Mediator;

/// <summary>
/// The handler of every message type. One per container.
/// <para>
/// A message's handler is the registration declared for its runtime type, as in Mediator. A handler declared for a base
/// class or an interface of the type does not handle it. Every send of a type is one dictionary lookup.
/// </para>
/// </summary>
/// <param name="registrations">Every request handler registration of the container. Each type has at most one.</param>
internal sealed class RequestRoutes(IEnumerable<RequestHandlerRegistration> registrations)
{
    private readonly Dictionary<(Type Type, bool Streamed), RequestHandlerRegistration> _byType =
        registrations.ToDictionary(static registration => (registration.MessageType, registration.Streamed));

    /// <summary>The handler of a message of <paramref name="messageType"/>, or null when it has none.</summary>
    /// <param name="messageType">The message's runtime type.</param>
    /// <param name="streamed">Whether the message is answered with a stream.</param>
    public RequestHandlerRegistration? For(Type messageType, bool streamed)
        => _byType.GetValueOrDefault((messageType, streamed));
}
