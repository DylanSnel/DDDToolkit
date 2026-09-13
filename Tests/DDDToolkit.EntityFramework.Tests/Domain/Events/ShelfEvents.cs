using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;

namespace DDDToolkit.EntityFramework.Tests.Domain.Events;

[DomainEventName("shelf.created")]
public sealed record ShelfCreated(ShelfId ShelfId, string Name) : DomainEvent;

[DomainEventName("shelf.renamed")]
public sealed record ShelfRenamed(ShelfId ShelfId, string Name) : DomainEvent;

/// <summary>No [DomainEventName]: the stable name falls back to the class name.</summary>
public sealed record BookAdded(ShelfId ShelfId, BookId BookId, string Title) : DomainEvent;
