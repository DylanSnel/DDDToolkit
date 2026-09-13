using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace DDDToolkit.Serialization;

/// <summary>
/// Describes how a type that wraps exactly one value - a <see cref="SingleValueObject{T}"/>, a reference
/// type id (<c>[EntityId&lt;T&gt;] partial record</c>) or a struct id
/// (<c>[EntityId&lt;T&gt;] readonly partial record struct</c>) - maps to and from the value it wraps.
/// <para>
/// This is the piece the System.Text.Json and the Newtonsoft.Json integrations share, and it is public so
/// that a third serializer (or a message contract mapper) can be written without repeating the reflection.
/// Descriptions are resolved once per type and cached.
/// </para>
/// <example>
/// <code>
/// var description = SingleValueDescription.For(typeof(CatId));   // ValueType == typeof(Guid)
/// var id = description!.Create(Guid.NewGuid());                  // boxed CatId
/// var value = description.GetValue(id);                          // the Guid back
/// </code>
/// </example>
/// </summary>
public sealed class SingleValueDescription
{
    private static readonly ConcurrentDictionary<Type, SingleValueDescription?> Cache = new();

    private readonly ConstructorInfo? _constructor;
    private readonly Func<object, object?> _valueAccessor;

    private SingleValueDescription(Type type, Type valueType, bool isSingleValueObject, ConstructorInfo? constructor, Func<object, object?> valueAccessor)
    {
        Type = type;
        ValueType = valueType;
        IsSingleValueObject = isSingleValueObject;
        _constructor = constructor;
        _valueAccessor = valueAccessor;
    }

    /// <summary>The wrapping type, for example <c>CatId</c> or <c>EmailAddress</c>.</summary>
    public Type Type { get; }

    /// <summary>The type of the single value being wrapped, for example <see cref="Guid"/> or <see cref="string"/>.</summary>
    public Type ValueType { get; }

    /// <summary>True when the wrapping type derives from <see cref="SingleValueObject{T}"/>, which reference type ids do.</summary>
    public bool IsSingleValueObject { get; }

    /// <summary>True when the wrapping type is a struct id, which can never be null.</summary>
    public bool IsValueType => Type.IsValueType;

    /// <summary>
    /// False when the type has no constructor taking the single value, so it can be written but not read
    /// back. Callers check this before <see cref="Create"/> to fail with an exception of their own kind.
    /// </summary>
    public bool CanCreate => _constructor is not null;

    /// <summary>
    /// Describes <paramref name="type"/>, or returns <see langword="null"/> when it does not wrap a single
    /// value - which is exactly when a converter should decline it.
    /// </summary>
    public static SingleValueDescription? For(Type type)
    {
        if (type is null)
        {
            return null;
        }

        return Cache.GetOrAdd(type, static candidate => Describe(candidate));
    }

    /// <summary>
    /// Creates an instance of <see cref="Type"/> wrapping <paramref name="value"/>. The constructor may
    /// still reject the value (an always valid type validates in its constructor); that exception is
    /// rethrown as-is rather than wrapped in a <see cref="TargetInvocationException"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="CanCreate"/> is false.</exception>
    public object Create(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (_constructor is null)
        {
            throw new InvalidOperationException($"'{Type.Name}' has no constructor taking a single {ValueType.Name}.");
        }

        try
        {
            return _constructor.Invoke([value]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw; // unreachable; keeps the compiler happy.
        }
    }

    /// <summary>
    /// Returns the single value wrapped by <paramref name="instance"/>. Reference types go through
    /// <see cref="ISingleValueObject"/>; struct ids through a cached delegate that casts to
    /// <see cref="IEntityId{TValue}"/> - neither looks a property up by name at call time.
    /// </summary>
    public object? GetValue(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return instance is ISingleValueObject singleValueObject
            ? singleValueObject.GetValue()
            : _valueAccessor(instance);
    }

    private static SingleValueDescription? Describe(Type type)
    {
        if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition || type.ContainsGenericParameters)
        {
            return null;
        }

        var valueType = SingleValueObjectValueType(type);
        var isSingleValueObject = valueType is not null;
        valueType ??= EntityIdValueType(type);

        if (valueType is null)
        {
            return null;
        }

        var constructor = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [valueType],
            modifiers: null);

        return new SingleValueDescription(type, valueType, isSingleValueObject, constructor, ValueAccessor(valueType));
    }

    /// <summary>Walks the base chain looking for <c>SingleValueObject&lt;T&gt;</c>; ids based on <c>EntityId&lt;T&gt;</c> are found here too.</summary>
    private static Type? SingleValueObjectValueType(Type type)
    {
        if (!typeof(ISingleValueObject).IsAssignableFrom(type))
        {
            return null;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(SingleValueObject<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>The <c>TValue</c> of the single <see cref="IEntityId{TValue}"/> the type implements, if there is exactly one.</summary>
    private static Type? EntityIdValueType(Type type)
    {
        if (!typeof(IEntityId).IsAssignableFrom(type))
        {
            return null;
        }

        Type? valueType = null;
        foreach (var candidate in type.GetInterfaces())
        {
            if (!candidate.IsGenericType || candidate.GetGenericTypeDefinition() != typeof(IEntityId<>))
            {
                continue;
            }

            if (valueType is not null)
            {
                return null; // Ambiguous: the type wraps more than one value.
            }

            valueType = candidate.GetGenericArguments()[0];
        }

        return valueType;
    }

    private static Func<object, object?> ValueAccessor(Type valueType)
    {
        var reader = typeof(ValueReader<>).MakeGenericType(valueType);
        var read = reader.GetMethod(nameof(ValueReader<object>.Read), BindingFlags.Static | BindingFlags.Public)!;
        return read.CreateDelegate<Func<object, object?>>();
    }

    private static class ValueReader<TValue>
    {
        public static object? Read(object instance) => ((IEntityId<TValue>)instance).Value;
    }
}
