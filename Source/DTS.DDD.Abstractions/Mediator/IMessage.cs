#if NET
namespace DTS.DDD.Mediator;

/// <summary>
/// Marks every message of the toolkit's mediator that is answered once: a request, a command, a query and a
/// notification. A pipeline behavior is declared for messages of this kind.
/// </summary>
public interface IMessage;

/// <summary>
/// Marks every message of the toolkit's mediator that is answered with a stream: a stream request, a stream command
/// and a stream query. A stream pipeline behavior is declared for messages of this kind.
/// </summary>
public interface IStreamMessage;
#endif
