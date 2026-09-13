using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using DDDToolkit.Interfaces;

namespace DDDToolkit.Testing.Internal;

/// <summary>
/// Compares and renders the payload of a domain event: everything the event carries except the
/// metadata every event carries. Record equality cannot do this job, because two events with the
/// same payload always differ in <c>EventId</c> and almost always in <c>OccurredAt</c>.
/// </summary>
internal static class EventPayload
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Members = new();

    /// <summary>The payload properties of an event type, in declaration order, metadata excluded.</summary>
    internal static PropertyInfo[] MembersOf(Type eventType) => Members.GetOrAdd(eventType, Build);

    /// <summary>
    /// Compares two events of the same runtime type by payload.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the payloads are equal, otherwise a description of the first
    /// member that differs.
    /// </returns>
    internal static string? FirstDifference(IDomainEvent expected, IDomainEvent actual)
    {
        var expectedType = expected.GetType();
        var actualType = actual.GetType();

        if (expectedType != actualType)
        {
            return $"the event was {actualType.Name}, expected {expectedType.Name}";
        }

        foreach (var member in MembersOf(expectedType))
        {
            var expectedValue = SafeRead(member, expected);
            var actualValue = SafeRead(member, actual);

            if (!ValuesEqual(expectedValue, actualValue))
            {
                return $"{member.Name} was {Format(actualValue)}, expected {Format(expectedValue)}";
            }
        }

        return null;
    }

    /// <summary>Whether two events carry the same payload. Metadata is ignored.</summary>
    internal static bool Matches(IDomainEvent expected, IDomainEvent actual)
        => FirstDifference(expected, actual) is null;

    /// <summary>Renders an event as <c>OrderCancelled { OrderId = ORD_1, Reason = "out of stock" }</c>.</summary>
    internal static string Describe(IDomainEvent domainEvent)
    {
        var type = domainEvent.GetType();
        var members = MembersOf(type);

        if (members.Length == 0)
        {
            return type.Name;
        }

        var parts = members.Select(member => $"{member.Name} = {Format(SafeRead(member, domainEvent))}");
        return $"{type.Name} {{ {string.Join(", ", parts)} }}";
    }

    /// <summary>Renders a value for a failure message.</summary>
    internal static string Format(object? value, int depth = 0)
    {
        switch (value)
        {
            case null:
                return "null";
            case string text:
                return $"\"{text}\"";
            case IEnumerable items when depth < 2:
                var rendered = items.Cast<object?>().Select(item => Format(item, depth + 1)).ToArray();
                return $"[{string.Join(", ", rendered)}]";
            case IDomainEvent nested when depth < 2:
                return Describe(nested);
            default:
                return value.ToString() ?? value.GetType().Name;
        }
    }

    private static object? SafeRead(PropertyInfo member, object instance)
    {
        try
        {
            return member.GetValue(instance);
        }
        catch (TargetInvocationException invocation)
        {
            // A computed property that throws must not hide the assertion that was actually failing.
            return $"<threw {invocation.InnerException?.GetType().Name ?? nameof(Exception)}>";
        }
    }

    private static bool ValuesEqual(object? expected, object? actual)
    {
        if (ReferenceEquals(expected, actual))
        {
            return true;
        }

        if (expected is null || actual is null)
        {
            return false;
        }

        if (expected is string || actual is string)
        {
            return expected.Equals(actual);
        }

        if (expected is IEnumerable expectedItems && actual is IEnumerable actualItems)
        {
            return SequencesEqual(expectedItems, actualItems);
        }

        return expected.Equals(actual);
    }

    private static bool SequencesEqual(IEnumerable expected, IEnumerable actual)
    {
        var left = expected.Cast<object?>().ToArray();
        var right = actual.Cast<object?>().ToArray();

        return left.Length == right.Length
            && !left.Where((item, index) => !ValuesEqual(item, right[index])).Any();
    }

    private static PropertyInfo[] Build(Type eventType)
    {
        var interfaceMembers = InterfaceGetters(eventType);

        return eventType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Where(property => property.GetMethod is not null)
            .Where(property => !interfaceMembers.Contains(property.GetMethod!.MethodHandle))
            .Where(property => property.Name is not (nameof(IDomainEvent.EventId) or nameof(IDomainEvent.OccurredAt)))
            .ToArray();
    }

    private static HashSet<RuntimeMethodHandle> InterfaceGetters(Type eventType)
    {
        var handles = new HashSet<RuntimeMethodHandle>();

        if (eventType.IsInterface || !typeof(IDomainEvent).IsAssignableFrom(eventType))
        {
            return handles;
        }

        // Catches an event that implements IDomainEvent explicitly, or names its metadata members
        // something else. The name check in Build catches the ordinary case.
        var map = eventType.GetInterfaceMap(typeof(IDomainEvent));
        foreach (var method in map.TargetMethods)
        {
            handles.Add(method.MethodHandle);
        }

        return handles;
    }
}
