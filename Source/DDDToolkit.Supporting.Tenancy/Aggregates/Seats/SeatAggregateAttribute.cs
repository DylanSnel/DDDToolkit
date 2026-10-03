using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Declares the application's seat class. The generator derives it from
/// <see cref="SeatAggregate{TSeatId, TTenantId, TUnitId, TRoleId}"/>, closed over the seat id given here and
/// the tenant, unit and role ids of the classes declared with the other templates:
/// <code>
/// [SeatAggregate&lt;SeatId&gt;]
/// public sealed partial class ShopSeat;
/// </code>
/// </summary>
/// <typeparam name="TSeatId">The application's seat id, a <c>readonly partial record struct</c> marked <c>[EntityId&lt;T&gt;]</c>.</typeparam>
[AggregateRootTemplate(typeof(SeatAggregate<,,,>))]
[TemplateArgument(1, typeof(TenantAggregateAttribute<>))]
[TemplateArgument(2, typeof(OrganizationUnitAttribute<>))]
[TemplateArgument(3, typeof(RoleAggregateAttribute<>))]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SeatAggregateAttribute<TSeatId> : Attribute;
