using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;

namespace DDDToolkit.EntityFramework.Tests.Domain.Events;

[DomainEventName("shelf.created")]
public sealed record ShelfCreated(ShelfId ShelfId, string Name) : DomainEvent;

[DomainEventName("shelf.renamed")]
public sealed record ShelfRenamed(ShelfId ShelfId, string Name) : DomainEvent;

/// <summary>No [DomainEventName]: the stable name is the conventional one, <c>book-added</c>.</summary>
public sealed record BookAdded(ShelfId ShelfId, BookId BookId, string Title) : DomainEvent;

/// <summary>
/// The shape <c>shelf.catalogued</c> rows were written in before the cataloguing system was named. It is
/// still in the build only so rows written against it can be read back; nothing raises it any more.
/// <para>
/// Both attributes carry the same name on purpose. <c>[DomainEventName]</c> is what the outbox stores in
/// the row, <c>[IntegrationEvent]</c> is what says which shape that row is, and a version is meaningless
/// unless the two agree.
/// </para>
/// </summary>
[DomainEventName("shelf.catalogued")]
[IntegrationEvent("shelf.catalogued", Version = 1)]
public sealed record ShelfCataloguedV1(ShelfId ShelfId, string Code) : DomainEvent;

/// <summary>The shape <c>shelf.catalogued</c> is raised in now.</summary>
[DomainEventName("shelf.catalogued")]
[IntegrationEvent("shelf.catalogued", Version = 2)]
public sealed record ShelfCataloguedV2(ShelfId ShelfId, string Code, string System) : DomainEvent;
