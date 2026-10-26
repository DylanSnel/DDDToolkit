using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DDDToolkit.EntityFramework.Storage;

/// <summary>
/// Stores any generated id, single value object or always-valid twin as its value, and reads it back through
/// <see cref="ISingleValue{TSelf, TValue}.FromValue"/>.
/// <para>
/// The Entity Framework generator nests a converter of its own in every id a project declares when that project
/// references Entity Framework. This one is for the ids of a project that does not: a module's domain or contracts
/// project, kept free of Entity Framework, whose ids the project holding the context stores. The generated
/// <c>Add{Module}Converters()</c> of that project registers this converter for them, so the context registers them
/// as it registers its own. Reading back does what the nested converter does: the plain type is rebuilt without a
/// check, and the twin through its public constructor, which validates.
/// </para>
/// </summary>
/// <typeparam name="T">The id or value object.</typeparam>
/// <typeparam name="TValue">The value it holds, and the column's type.</typeparam>
public sealed class SingleValueConverter<T, TValue>() : ValueConverter<T, TValue>(
    static single => single.Value,
    static value => Create(value))
    where T : ISingleValue<T, TValue>
{
    /// <summary>
    /// <see cref="ISingleValue{TSelf, TValue}.FromValue"/>, called through a method: an expression tree cannot call a
    /// static abstract member of a type parameter (CS8927), and a converter's two directions are expression trees.
    /// </summary>
    private static T Create(TValue value) => T.FromValue(value);
}
