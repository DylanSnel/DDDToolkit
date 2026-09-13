using System.ComponentModel;

namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a strongly typed identifier wrapping a <typeparamref name="TType"/>.
/// <para>
/// Apply to a <c>partial record</c> for a reference id (derives from <c>EntityId&lt;TType&gt;</c> and gets an
/// always-valid twin) or to a <c>readonly partial record struct</c> for an allocation-free id.
/// </para>
/// </summary>
/// <param name="Prefix">Optional prefix written by <c>ToString()</c> as <c>PREFIX_value</c> and accepted by <c>Parse</c>.</param>
/// <param name="ColumnLength">Optional maximum column length applied by the generated EF Core configuration.</param>
#pragma warning disable CS9113 // Parameter is unread (read by the source generator).
#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable S2326 // Unused type parameters should be removed
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class EntityIdAttribute<TType>(string Prefix = "", int ColumnLength = -1) : EntityIdAttribute
#pragma warning restore S2326
#pragma warning restore IDE1006
#pragma warning restore CS9113
{
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class EntityIdAttribute : Attribute
{
    internal EntityIdAttribute() { }
}
