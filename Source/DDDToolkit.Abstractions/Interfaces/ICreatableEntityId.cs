#if NET7_0_OR_GREATER
namespace DDDToolkit.Abstractions.Interfaces;

/// <summary>
/// An id that makes a new one of itself: <c>TId.Create()</c>. Code that is generic over an application's ids, a
/// supporting domain's use cases say, makes a new one with it, in code, before anything is saved, so the id is known
/// to every event and every row of the change that makes it. The database never makes one.
/// <para>
/// The generator implements it for every id over a <see cref="Guid"/>, with <c>Create()</c> making a time-ordered one
/// (<c>CreateSequential()</c>). An id over anything else, a <see cref="long"/> or a <see cref="string"/>, says how a
/// new one is made with a <c>Create()</c> of its own, in its partial declaration, and the generator then implements
/// the interface with it; so does an id over a <see cref="Guid"/> that wants another kind:
/// <code>
/// [EntityId&lt;long&gt;]
/// public readonly partial record struct TenantId
/// {
///     public static TenantId Create() => new(Snowflakes.Next());   // a snowflake, or a value of a HiLo block
/// }
/// </code>
/// An id written by hand, without <c>[EntityId&lt;T&gt;]</c>, has nothing from the generator, so it declares the
/// <c>Create()</c> and implements this interface itself.
/// </para>
/// </summary>
/// <typeparam name="TSelf">The id itself.</typeparam>
/// <remarks>
/// It is in the .NET 10 build of DDDToolkit.Abstractions alone: a static member of an interface needs a runtime that
/// has them, which the <c>netstandard2.0</c> build cannot assume. The generator implements it where the project sees
/// it, and writes <c>Create()</c> for an id over a <see cref="Guid"/> wherever <c>CreateSequential()</c> is.
/// </remarks>
public interface ICreatableEntityId<TSelf> : IEntityId
    where TSelf : ICreatableEntityId<TSelf>
{
    /// <summary>A new id, made in code: one no other row has.</summary>
    static abstract TSelf Create();
}
#endif
