using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Declares the application's tenant class. The generator derives it from
/// <see cref="TenantAggregate{TTenantId}"/>, closed over the id given here:
/// <code>
/// [TenantAggregate&lt;TenantId&gt;]
/// public sealed partial class ShopTenant;
/// </code>
/// </summary>
/// <typeparam name="TTenantId">The application's tenant id, a <c>readonly partial record struct</c> marked <c>[EntityId&lt;T&gt;]</c>. Tenancy makes a new one with its <c>Create()</c>, which the generator writes for an id over a <see cref="Guid"/> and an id over anything else declares itself (DDD00067).</typeparam>
[AggregateRootTemplate(typeof(TenantAggregate<>), CreatesIds = true)]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TenantAggregateAttribute<TTenantId> : Attribute;
