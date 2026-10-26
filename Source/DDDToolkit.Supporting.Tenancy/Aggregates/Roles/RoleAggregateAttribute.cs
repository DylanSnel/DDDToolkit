using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Declares the application's role class. The generator derives it from
/// <see cref="RoleAggregate{TRoleId, TTenantId}"/>, closed over the role id given here and the tenant id of
/// the class declared with <see cref="TenantAggregateAttribute{TTenantId}"/>:
/// <code>
/// [RoleAggregate&lt;RoleId&gt;]
/// public sealed partial class ShopRole;
/// </code>
/// </summary>
/// <typeparam name="TRoleId">The application's role id, a <c>readonly partial record struct</c> marked <c>[EntityId&lt;T&gt;]</c>. Tenancy makes a new one with its <c>Create()</c>, which the generator writes for an id over a <see cref="Guid"/> and an id over anything else declares itself (DDD00067).</typeparam>
[AggregateRootTemplate(typeof(RoleAggregate<,>), CreatesIds = true)]
[TemplateArgument(1, typeof(TenantAggregateAttribute<>))]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RoleAggregateAttribute<TRoleId> : Attribute;
