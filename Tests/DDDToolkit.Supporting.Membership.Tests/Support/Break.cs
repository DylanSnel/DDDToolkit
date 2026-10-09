using System.Reflection;

namespace DDDToolkit.Supporting.Membership.Tests.Support;

/// <summary>
/// Puts an object in a state its own methods never would, to see the rule under them catch it: the net is
/// only worth having if it holds what went round the methods.
/// </summary>
internal static class Break
{
    /// <summary>The private list behind a read-only collection, such as an aggregate's members or a member's roles.</summary>
    public static List<T> ListOf<T>(object owner, string field)
    {
        for (var type = owner.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } found)
            {
                return (List<T>)found.GetValue(owner)!;
            }
        }

        throw new InvalidOperationException(owner.GetType().Name + " has no field " + field + ".");
    }

    /// <summary>Sets a property through its private setter, wherever in the hierarchy it is declared.</summary>
    public static void Set(object target, string property, object? value)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)?.GetSetMethod(nonPublic: true) is { } setter)
            {
                setter.Invoke(target, [value]);
                return;
            }
        }

        throw new InvalidOperationException(target.GetType().Name + " has no settable property " + property + ".");
    }
}
