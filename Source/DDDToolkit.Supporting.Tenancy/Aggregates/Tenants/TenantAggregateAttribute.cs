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
/// <typeparam name="TTenantId">The application's tenant id, a <c>readonly partial record struct</c> marked <c>[EntityId&lt;T&gt;]</c>.</typeparam>
[AggregateRootTemplate(typeof(TenantAggregate<>))]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TenantAggregateAttribute<TTenantId> : Attribute;
