using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.HotChocolate.Attributes;
using HotChocolate.Types;

namespace DDDToolkit.HotChocolate.Tests.Domain;

/// <summary>
/// A struct id declared in the test assembly, with a prefix. The prefix belongs to
/// <c>ToString()</c>/<c>Parse</c>; over the wire only the wrapped <see cref="Guid"/> travels, which
/// is what <c>GraphQlScalarBindingTests</c> pins down.
/// </summary>
[EntityId<Guid>("TST")]
public readonly partial record struct TicketId
{
    /// <summary>An id over the given value.</summary>
    public static TicketId Create(Guid value) => new(value);
}

/// <summary>A struct id over an <see cref="int"/> rather than a Guid: it binds to <c>Int</c>.</summary>
[EntityId<int>]
public readonly partial record struct SeatNumber
{
    /// <summary>An id over the given value.</summary>
    public static SeatNumber Create(int value) => new(value);
}

/// <summary>
/// A string-valued struct id that names its schema type. Without
/// <c>[GraphQLType&lt;EmailAddressType&gt;]</c> it would bind to <c>String</c>, so the schema shows
/// whether the attribute was honoured — and whether it can be applied to a struct at all, which it
/// could not before the attribute's usage was widened.
/// </summary>
[EntityId<string>("USR")]
[GraphQLType<EmailAddressType>]
public readonly partial record struct LoginId
{
    /// <summary>An id over the given value.</summary>
    public static LoginId Create(string value) => new(value);
}

/// <summary>A value of two parts, which no GraphQL scalar and no paging cursor carries.</summary>
public readonly record struct GridCell(int Column, int Row) : IComparable<GridCell>
{
    /// <inheritdoc />
    public int CompareTo(GridCell other) => (Column, Row).CompareTo((other.Column, other.Row));
}

/// <summary>
/// A struct id over such a value. The generated bindings register its converter and nothing else: no scalar is
/// known for it, and it is not registered as a paging key, which would fail when the bindings are added.
/// </summary>
[EntityId<GridCell>]
public readonly partial record struct PegId
{
    /// <summary>An id over the given value.</summary>
    public static PegId Create(GridCell value) => new(value);
}
