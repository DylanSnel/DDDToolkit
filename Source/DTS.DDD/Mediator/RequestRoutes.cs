using System.Collections.Concurrent;

namespace DTS.DDD.Mediator;

/// <summary>
/// The handler of every message type. One per container.
/// <para>
/// A runtime type's handler is the registration declared for that type. Where there is none, it is the one
/// registration declared for a base class or an interface of the type. It is worked out the first time the type is
/// sent and kept, so every later send of the type is one dictionary lookup.
/// </para>
/// </summary>
/// <param name="registrations">Every request handler registration of the container.</param>
internal sealed class RequestRoutes(IEnumerable<RequestHandlerRegistration> registrations)
{
    private readonly RequestHandlerRegistration[] _registrations = [.. registrations];
    private readonly ConcurrentDictionary<(Type Type, bool Streamed), RequestHandlerRegistration> _byType = new();

    /// <summary>The handler of a message of <paramref name="messageType"/>, or null when it has none.</summary>
    /// <param name="messageType">The message's runtime type.</param>
    /// <param name="streamed">Whether the message is answered with a stream.</param>
    /// <exception cref="InvalidOperationException">Handlers of more than one base type or interface of the type are registered.</exception>
    public RequestHandlerRegistration? For(Type messageType, bool streamed)
    {
        if (_byType.TryGetValue((messageType, streamed), out var known))
        {
            return known;
        }

        var found = Find(messageType, streamed);
        return found is null ? null : _byType.GetOrAdd((messageType, streamed), found);
    }

    private RequestHandlerRegistration? Find(Type messageType, bool streamed)
    {
        var exact = Array.Find(_registrations, registration => registration.Streamed == streamed && registration.MessageType == messageType);
        if (exact is not null)
        {
            return exact;
        }

        var assignable = Array.FindAll(_registrations, registration => registration.Streamed == streamed && registration.MessageType.IsAssignableFrom(messageType));
        return assignable.Length switch
        {
            0 => null,
            1 => assignable[0],
            _ => throw new InvalidOperationException(
                $"'{messageType.FullName}' has no handler of its own and {assignable.Length} handlers of types it derives from: "
                + string.Join(", ", assignable.Select(static registration => $"'{registration.HandlerType.FullName}' for '{registration.MessageType.FullName}'"))
                + ". A message has one handler. Register a handler for the type itself."),
        };
    }
}
