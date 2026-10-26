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
/// <typeparam name="TUnitId">The application's unit id, a <c>readonly partial record struct</c> marked <c>[EntityId&lt;T&gt;]</c>. Tenancy makes a new one with its <c>Create()</c>, which the generator writes for an id over a <see cref="Guid"/> and an id over anything else declares itself (DDD00067).</typeparam>
[EntityTemplate(typeof(OrganizationUnitEntity<>), CreatesIds = true)]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OrganizationUnitAttribute<TUnitId> : Attribute;
