using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.EntityFramework.Tests.Domain.Events;

/// <summary>
/// The published counterpart of <see cref="ShelfCreated"/>. It deliberately shares no type with the
/// domain event: primitives only, its own name and its own version, so the aggregate can be
/// refactored without touching what other systems read.
/// </summary>
[IntegrationEvent("library.shelf-opened", Version = 3)]
public sealed record ShelfOpenedV3(string ShelfId, string DisplayName);

/// <summary>A contract with no attribute at all: the name falls back to the class name, version to 1.</summary>
public sealed record BookShelved(string BookId);
