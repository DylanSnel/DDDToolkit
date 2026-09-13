using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.EntityFramework.Tests.Domain;

/// <summary>Struct id with a prefix, used as the primary key of <see cref="Shelf"/>.</summary>
[EntityId<Guid>("SHELF")]
public readonly partial record struct ShelfId;

/// <summary>Struct id without a prefix, used as the key of the owned <see cref="Book"/> entity.</summary>
[EntityId<Guid>]
public readonly partial record struct BookId;

/// <summary>Struct id wrapping an int, stored in a primitive collection on <see cref="Book"/>.</summary>
[EntityId<int>("TAG")]
public readonly partial record struct TagId;

/// <summary>Struct id of the owned <see cref="Note"/> entity.</summary>
[EntityId<Guid>]
public readonly partial record struct NoteId;

/// <summary>Struct id used as the primary key of <see cref="Person"/>.</summary>
[EntityId<Guid>("PER")]
public readonly partial record struct MemberId;
