using System.ComponentModel;

namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Marks a child entity: an object with identity that belongs to exactly one aggregate. Apply to a
/// <c>partial class</c>.
/// <para>
/// <typeparamref name="TType"/> is either an existing strongly typed id (a type marked with
/// <c>[EntityId&lt;T&gt;]</c>) or the raw value an id should wrap, such as <c>Guid</c>. In the second case
/// the toolkit also generates the id itself, named after this type: <c>[Entity&lt;Guid&gt;("LINE")]</c> on
/// <c>OrderLine</c> generates <c>OrderLineId</c>.
/// </para>
/// </summary>
/// <param name="Prefix">Optional prefix for a generated id, written by <c>ToString()</c> as <c>PREFIX_value</c>. Ignored when <typeparamref name="TType"/> is already an id.</param>
/// <param name="ColumnLength">Optional maximum column length applied by the generated EF Core configuration of a generated id. Ignored when <typeparamref name="TType"/> is already an id.</param>
/// <remarks>
/// Records and structs are accepted by the attribute so that applying it to one reports DDD00002,
/// which explains the requirement, instead of the compiler's generic "attribute is not valid on this
/// declaration type".
/// </remarks>
#pragma warning disable S2326 // Unused type parameters should be removed (read by the source generator).
#pragma warning disable CS9113 // Parameter is unread (read by the source generator).
#pragma warning disable IDE1006 // Naming Styles
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class EntityAttribute<TType>(string Prefix = "", int ColumnLength = -1) : EntityAttribute
#pragma warning restore IDE1006
#pragma warning restore CS9113
#pragma warning restore S2326
{
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class EntityAttribute : Attribute
{
    internal EntityAttribute() { }
}
