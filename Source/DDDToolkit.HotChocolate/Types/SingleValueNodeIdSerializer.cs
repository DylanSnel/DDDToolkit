using DDDToolkit.Interfaces;
using HotChocolate.Types.Relay;

namespace DDDToolkit.HotChocolate.Types;

/// <summary>
/// Writes any generated identifier into a Relay node id, and reads it back out of one, through
/// <see cref="ISingleValue{TSelf, TValue}"/>.
/// <para>
/// The HotChocolate generator nests a <c>NodeIdValueSerializer</c> of its own in every identifier a project declares
/// when that project references this package. This one is for the identifiers of a project that does not, such as
/// a module's domain or contracts project: the generated <c>Add{Module}GraphQlRuntimeBindings()</c> of the module's
/// project that builds the schema registers it for them. It writes what the nested serializer writes, with
/// HotChocolate's own helpers, so the node id is the one HotChocolate gives the bare value: <c>Order:</c> followed
/// by the value, which any HotChocolate server or Fusion gateway reads.
/// </para>
/// </summary>
/// <typeparam name="T">The identifier.</typeparam>
/// <typeparam name="TValue">
/// The value it holds: <see cref="Guid"/>, <see cref="string"/>, <see cref="int"/>, <see cref="long"/> or
/// <see cref="short"/>, the values HotChocolate can write into a node id.
/// </typeparam>
public sealed class SingleValueNodeIdSerializer<T, TValue> : CompositeNodeIdValueSerializer<T>
    where T : ISingleValue<T, TValue>
{
    /// <summary>A serializer for <typeparamref name="T"/>.</summary>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="TValue"/> is not one of the five values a node id can carry. It is thrown here, when
    /// the schema is built, rather than at the first node id a request writes.
    /// </exception>
    public SingleValueNodeIdSerializer()
    {
        if (!IsNodeIdValue)
        {
            throw Unsupported();
        }
    }

    private static bool IsNodeIdValue
        => typeof(TValue) == typeof(Guid)
           || typeof(TValue) == typeof(string)
           || typeof(TValue) == typeof(int)
           || typeof(TValue) == typeof(long)
           || typeof(TValue) == typeof(short);

    /// <inheritdoc />
    protected override NodeIdFormatterResult Format(Span<byte> buffer, T value, out int written)
    {
        // Each comparison is between two types the runtime knows when it compiles the method for a TValue, so only
        // the matching branch is left, and the cast through object in it is dropped: nothing is boxed.
        var raw = value.Value;
        bool fits;

        if (typeof(TValue) == typeof(Guid))
        {
            fits = TryFormatIdPart(buffer, (Guid)(object)raw!, out written);
        }
        else if (typeof(TValue) == typeof(string))
        {
            fits = TryFormatIdPart(buffer, (string)(object)raw!, out written);
        }
        else if (typeof(TValue) == typeof(int))
        {
            fits = TryFormatIdPart(buffer, (int)(object)raw!, out written);
        }
        else if (typeof(TValue) == typeof(long))
        {
            fits = TryFormatIdPart(buffer, (long)(object)raw!, out written);
        }
        else if (typeof(TValue) == typeof(short))
        {
            fits = TryFormatIdPart(buffer, (short)(object)raw!, out written);
        }
        else
        {
            throw Unsupported();
        }

        return fits ? NodeIdFormatterResult.Success : NodeIdFormatterResult.BufferTooSmall;
    }

    /// <inheritdoc />
    protected override bool TryParse(ReadOnlySpan<byte> buffer, out T value)
    {
        // Which serializer reads a node id is decided by the type name in front of it, so a buffer that reaches
        // this one holds this type's value.
        if (typeof(TValue) == typeof(Guid))
        {
            if (TryParseIdPart(buffer, out Guid raw, out _))
            {
                value = T.FromValue((TValue)(object)raw);
                return true;
            }
        }
        else if (typeof(TValue) == typeof(string))
        {
            // HotChocolate declares the string overload's out value nullable; it is set whenever it returns true.
            if (TryParseIdPart(buffer, out string? raw, out _))
            {
                value = T.FromValue((TValue)(object)raw!);
                return true;
            }
        }
        else if (typeof(TValue) == typeof(int))
        {
            if (TryParseIdPart(buffer, out int raw, out _))
            {
                value = T.FromValue((TValue)(object)raw);
                return true;
            }
        }
        else if (typeof(TValue) == typeof(long))
        {
            if (TryParseIdPart(buffer, out long raw, out _))
            {
                value = T.FromValue((TValue)(object)raw);
                return true;
            }
        }
        else if (typeof(TValue) == typeof(short))
        {
            if (TryParseIdPart(buffer, out short raw, out _))
            {
                value = T.FromValue((TValue)(object)raw);
                return true;
            }
        }

        value = default!;
        return false;
    }

    private static NotSupportedException Unsupported()
        => new(
            $"A node id cannot carry a {typeof(TValue).Name}, the value of {typeof(T).Name}. "
            + "HotChocolate writes a Guid, a string, an int, a long or a short into a node id, and nothing else.");
}
