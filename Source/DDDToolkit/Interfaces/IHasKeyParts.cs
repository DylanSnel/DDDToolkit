namespace DDDToolkit.Interfaces;

/// <summary>
/// Names the <c>[KeyPart]</c> properties of an entity or aggregate root, in declaration order. The
/// generator implements it on every type that has at least one; a type without key parts does not
/// implement it at all.
/// <para>
/// It exists because reflection does not promise declaration order, and the order of a composite
/// key is not something to leave to chance. <c>DDDToolkit.EntityFramework</c>'s
/// <c>KeyPartConvention</c> reads it to build the key.
/// </para>
/// </summary>
/// <remarks>
/// You never implement this yourself; mark the properties with <c>[KeyPart]</c> instead.
/// </remarks>
public interface IHasKeyParts
{
    /// <summary>The names of the key-part properties, in the order they join the key, before the id.</summary>
    static abstract IReadOnlyList<string> KeyParts { get; }
}
