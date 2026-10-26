using System.Linq.Expressions;
using System.Reflection;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// Makes an instance of the application's own member class, which the package cannot name, through the
/// parameterless constructor the generator writes on every class declared with a template: the one Entity
/// Framework uses to load it.
/// <para>
/// That constructor is not public, so a <c>new()</c> constraint cannot reach it. It is found once per class
/// and compiled into a delegate, so making an instance afterwards costs a call, not a reflection lookup. The
/// instance comes out empty; the parent's <c>InitializeNew</c> gives it its id and state, which keeps "what
/// the application writes" to the one-line class declaration.
/// </para>
/// </summary>
/// <typeparam name="T">The application's class.</typeparam>
internal static class HostInstances<T>
    where T : class
{
    private static Func<T>? _create;

    /// <summary>A new, empty instance of <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> has no parameterless constructor to call.</exception>
    public static T New() => (_create ??= Build())();

    private static Func<T> Build()
    {
        var type = typeof(T);
        var constructor = type.IsAbstract
            ? null
            : type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);

        if (constructor is null)
        {
            throw new InvalidOperationException(
                "Membership cannot create a " + type.Name + ": it has no parameterless constructor. Declare it with the "
                + "member template, such as [Member<" + type.Name + "Id, UserId, NamedRole, Document>] public sealed partial class "
                + type.Name + "; for the members of a Document, and the generator writes one.");
        }

        return Expression.Lambda<Func<T>>(Expression.New(constructor)).Compile();
    }
}
