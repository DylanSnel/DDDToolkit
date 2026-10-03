using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Declares the application's organization unit class. The generator derives it from
/// <see cref="OrganizationUnitEntity{TUnitId}"/>, closed over the id given here:
/// <code>
/// [OrganizationUnit&lt;OrganizationUnitId&gt;]
/// public sealed partial class ShopUnit;
/// </code>
/// </summary>
/// <typeparam name="TUnitId">The application's unit id, a <c>readonly partial record struct</c> marked <c>[EntityId&lt;T&gt;]</c>.</typeparam>
[EntityTemplate(typeof(OrganizationUnitEntity<>))]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OrganizationUnitAttribute<TUnitId> : Attribute;
