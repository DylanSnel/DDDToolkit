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

/// <summary>
/// The shape <c>library.book-shelved</c> was published in before anyone asked which shelf. Kept in the
/// build so payloads written against it are still readable.
/// </summary>
[IntegrationEvent("library.book-shelved", Version = 1)]
public sealed record BookShelvedV1(string BookId);

/// <summary>The shape <c>library.book-shelved</c> is published in now.</summary>
[IntegrationEvent("library.book-shelved", Version = 2)]
public sealed record BookShelvedV2(string BookId, string Shelf);

/// <summary>The third shape, so a chain of two upcasters can be followed in one read.</summary>
[IntegrationEvent("library.book-shelved", Version = 3)]
public sealed record BookShelvedV3(string BookId, string Shelf, string Library);
