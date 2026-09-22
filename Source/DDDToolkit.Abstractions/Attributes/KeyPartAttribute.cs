namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Makes a property of an <c>[AggregateRoot]</c> or <c>[Entity]</c> part of its primary key, ahead of
/// the identifier:
/// <code>
/// [AggregateRoot&lt;ProjectId&gt;]
/// public partial class Project
/// {
///     [KeyPart]
///     public RegionId RegionId { get; }
/// }
/// </code>
/// maps <c>Project</c> with the key <c>(RegionId, Id)</c>, and every owned child of <c>Project</c> with
/// a foreign key that starts with <c>RegionId</c> too, so a child can never point at a parent in
/// another region.
/// <para>
/// The property is an ordinary domain property: you set it, usually through the constructor. The
/// toolkit does not assign it, does not validate it and does not know what it means. It takes no
/// part in identity: two instances with the same <c>Id</c> are equal whatever their key parts hold.
/// </para>
/// <para>
/// With more than one key part, the key follows declaration order in the source, top to bottom.
/// Declare them together in one part of a partial class; spreading them over several files is
/// reported as DDD00030, because there is no declaration order between files.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class KeyPartAttribute : Attribute
{
}
