using System.Reflection;
using DDDToolkit.Interfaces;
using GreenDonut.Data.Cursors;
using GreenDonut.Data.Cursors.Serializers;

namespace DDDToolkit.HotChocolate.Paging;

/// <summary>
/// Lets HotChocolate's paging order by a generated id directly: <c>OrderBy(project =&gt; project.Id)</c> and
/// <c>ThenBy(inspection =&gt; inspection.Id)</c> in front of <c>ToPageAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ToPageAsync</c> writes the keys a list is ordered by into its cursors, and reads them back to find where
/// the next page starts. It knows how for the values it knows, a <see cref="Guid"/> or a <see cref="string"/>,
/// and refuses anything else: "The key type is not supported". An id is such a value with a type around it, so
/// this serializer hands the value to the serializer HotChocolate already has for it and puts the type back
/// around what that reads. The cursor of an id is therefore the cursor of its value, byte for byte: a list that
/// was ordered by <c>(Guid)project.Id</c> keeps its cursors when it orders by <c>project.Id</c>.
/// </para>
/// <para>
/// The comparison in the page's <c>WHERE</c> is the id's own <c>CompareTo</c>, which Entity Framework
/// translates to a comparison of the column, with no cast. That is why the id has to be comparable, and a
/// generated id declared as a <c>record struct</c> is; one declared as a class is not, and has no serializer.
/// </para>
/// <para>
/// HotChocolate keeps its serializers in one list for the process, so an id is registered once, with
/// <see cref="Register"/>. The generated <c>Add{Module}GraphQlRuntimeBindings()</c> does it for every struct id
/// it binds, when it is called and not when the schema is first built, so the lists of a REST route page the
/// same way from the first request on.
/// </para>
/// </remarks>
/// <typeparam name="T">The id.</typeparam>
/// <typeparam name="TValue">The value it holds: one HotChocolate has a cursor key serializer for.</typeparam>
public sealed class SingleValueCursorKeySerializer<T, TValue> : ICursorKeySerializer
    where T : ISingleValue<T, TValue>, IComparable<T>
{
    private static readonly MethodInfo CompareTo = typeof(T).GetMethod(nameof(IComparable<T>.CompareTo), [typeof(T)])!;

    private static readonly Lock Gate = new();

    private static bool _registered;

    private readonly ICursorKeySerializer _value;

    /// <summary>A serializer for <typeparamref name="T"/>.</summary>
    /// <exception cref="NotSupportedException">
    /// HotChocolate has no cursor key serializer for <typeparamref name="TValue"/>. It is thrown here, when the id
    /// is registered, rather than at the first page a request asks for.
    /// </exception>
    public SingleValueCursorKeySerializer() => _value = CursorKeySerializerRegistration.Find(typeof(TValue));

    /// <summary>
    /// Registers a serializer for <typeparamref name="T"/> with HotChocolate's paging, once: calling it again
    /// does nothing, so every module that names the id can.
    /// </summary>
    /// <exception cref="NotSupportedException">HotChocolate has no cursor key serializer for <typeparamref name="TValue"/>.</exception>
    public static void Register()
    {
        // Under a lock, so a caller that finds the id registered finds it in HotChocolate's list as well: two
        // schemas built side by side may both ask, and the second must not page before the first is done.
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            // A value without a serializer throws here and leaves the flag alone: every call fails, not only the first.
            CursorKeySerializerRegistration.Register(new SingleValueCursorKeySerializer<T, TValue>());
            _registered = true;
        }
    }

    /// <inheritdoc />
    public bool IsSupported(Type type) => type == typeof(T) || Nullable.GetUnderlyingType(type) == typeof(T);

    /// <inheritdoc />
    public MethodInfo GetCompareToMethod(Type type) => CompareTo;

    /// <inheritdoc />
    public object Parse(ReadOnlySpan<byte> formattedKey) => T.FromValue((TValue)_value.Parse(formattedKey));

    /// <inheritdoc />
    public bool TryFormat(object key, Span<byte> buffer, out int written)
    {
        ArgumentNullException.ThrowIfNull(key);

        return _value.TryFormat(((T)key).Value!, buffer, out written);
    }
}
