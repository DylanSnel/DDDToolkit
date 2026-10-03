namespace DDDToolkit.Interfaces;

/// <summary>
/// A type that is one value, and the way back to it from that value: every entity id, every single value
/// object and every always-valid twin the generator writes implements it, the static member explicitly.
/// <para>
/// It exists so that code which stores such a type as its value can be written once, as a generic, in a project
/// that does not declare the type. <c>DDDToolkit.EntityFramework</c>'s <c>SingleValueConverter&lt;T, TValue&gt;</c> is
/// that code for Entity Framework: with it, a module's domain and contracts projects need no Entity Framework
/// reference of their own, because the project that holds the context registers a converter for each of their
/// ids. Without it the only way back is a constructor the declaring project has to call, which is why the Entity
/// Framework generator nests a converter in the type when the declaring project references Entity Framework.
/// </para>
/// </summary>
/// <typeparam name="TSelf">The type itself.</typeparam>
/// <typeparam name="TValue">The value it holds, such as <see cref="Guid"/> or <see cref="string"/>.</typeparam>
/// <remarks>
/// <see cref="FromValue"/> is for reconstituting a value that was stored, and nothing else. A generic constraint
/// makes it callable by anyone, and that is harmless by design: for the plain type it does what the stored
/// value's reader always did, building a value that is not yet checked, and for the always-valid twin it goes
/// through the twin's public constructor, which still validates. Code that makes a new value calls the type's own
/// factories.
/// <para>
/// You never implement this yourself; declare the type with <c>[EntityId&lt;T&gt;]</c>, <c>[SingleValueObject&lt;T&gt;]</c>,
/// or an <c>[AggregateRoot&lt;T&gt;]</c> or <c>[Entity&lt;T&gt;]</c> that asks for an id.
/// </para>
/// </remarks>
public interface ISingleValue<TSelf, TValue>
    where TSelf : ISingleValue<TSelf, TValue>
{
    /// <summary>The value the type holds.</summary>
    TValue Value { get; }

    /// <summary>
    /// The type holding <paramref name="value"/>, as it is read back from storage: the plain type without a
    /// check, the always-valid twin validated.
    /// </summary>
    static abstract TSelf FromValue(TValue value);
}
