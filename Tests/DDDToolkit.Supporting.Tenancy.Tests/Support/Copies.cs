using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DDDToolkit.Supporting.Tenancy.Tests.Support;

/// <summary>
/// Deep copies of aggregates, field by field, so the in-memory store can hand a unit of work its own
/// instances and keep what was saved untouched when a command is refused half way, as a database does.
/// <para>
/// Objects of the package and the test host, lists and read-only views over lists are copied; everything
/// else (strings, ids, events' payloads from elsewhere) is immutable here and shared.
/// </para>
/// </summary>
internal static class Copies
{
    private static readonly Assembly[] Copied = [typeof(TenantAggregate<>).Assembly, typeof(HostTenant).Assembly];

    /// <summary>A deep copy of <paramref name="source"/>.</summary>
    public static T Of<T>(T source)
        where T : class
        => (T)Copy(source, new Dictionary<object, object>(ReferenceEqualityComparer.Instance))!;

    private static object? Copy(object? value, Dictionary<object, object> copied)
    {
        if (value is null or string)
        {
            return value;
        }

        var type = value.GetType();
        if (type.IsValueType)
        {
            return value;
        }

        if (copied.TryGetValue(value, out var existing))
        {
            return existing;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (IList)Activator.CreateInstance(type)!;
            copied[value] = list;
            foreach (var item in (IEnumerable)value)
            {
                list.Add(Copy(item, copied));
            }

            return list;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>))
        {
            // A cached view over a list the copy has its own of: a view over the copied list.
            var items = type.GetProperty("Items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
            var view = Activator.CreateInstance(type, Copy(items, copied))!;
            copied[value] = view;
            return view;
        }

        if (!Copied.Contains(type.Assembly))
        {
            return value;
        }

        var copy = RuntimeHelpers.GetUninitializedObject(type);
        copied[value] = copy;
        for (var declaring = type; declaring is not null && declaring != typeof(object); declaring = declaring.BaseType)
        {
            foreach (var field in declaring.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                field.SetValue(copy, Copy(field.GetValue(value), copied));
            }
        }

        return copy;
    }
}
