using System.Diagnostics.CodeAnalysis;
using DDDToolkit.Interfaces;
using HotChocolate.Utilities;

namespace DDDToolkit.HotChocolate.Types;

/// <summary>
/// Converts any generated id, single value object or always-valid twin to its value and back for the GraphQL
/// runtime, through <see cref="ISingleValue{TSelf, TValue}"/>.
/// <para>
/// The HotChocolate generator nests a <c>ChangeTypeProvider</c> of its own in every id a project declares when that
/// project references this package. This one is for the ids of a project that does not: a module's domain or
/// contracts project, kept free of HotChocolate and so of ASP.NET Core, whose ids the module's API project puts in
/// its schema. The generated <c>Add{Module}GraphQlRuntimeBindings()</c> of that project registers this provider for
/// them, next to the scalar each is printed as, so the schema registers them as it registers its own. Reading a
/// value does what the nested provider does: the plain type is built without a check, and the twin through its
/// public constructor, which validates.
/// </para>
/// </summary>
/// <typeparam name="T">The id or value object.</typeparam>
/// <typeparam name="TValue">The value it holds, which is what the scalar it is bound to carries.</typeparam>
public sealed class SingleValueChangeTypeProvider<T, TValue> : IChangeTypeProvider
    where T : ISingleValue<T, TValue>
{
    /// <summary>
    /// A converter from <typeparamref name="T"/> to <typeparamref name="TValue"/> or back. Every other pair is
    /// declined, so HotChocolate goes on to the next provider.
    /// </summary>
    /// <param name="source">The type a value arrives as.</param>
    /// <param name="target">The type it has to become.</param>
    /// <param name="root">HotChocolate's own lookup, for a provider that converts in steps. Not used: this is one step.</param>
    /// <param name="converter">The converter, when the pair is this provider's.</param>
    /// <returns>Whether the pair is this provider's.</returns>
    public bool TryCreateConverter(Type source, Type target, ChangeTypeProvider root, [NotNullWhen(true)] out ChangeType? converter)
    {
        if (source == typeof(T) && target == typeof(TValue))
        {
            converter = static value => ((T)value!).Value;
            return true;
        }

        if (source == typeof(TValue) && target == typeof(T))
        {
            converter = static value => T.FromValue((TValue)value!);
            return true;
        }

        converter = null;
        return false;
    }
}
