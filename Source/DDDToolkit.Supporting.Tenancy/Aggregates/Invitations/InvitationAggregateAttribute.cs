using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Declares the application's invitation class, for an application that invites people into a tenant. The
/// generator derives it from
/// <see cref="InvitationAggregate{TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId}"/>, closed over the
/// invitation id given here and the tenant, unit, role and seat ids of the classes declared with the other
/// templates:
/// <code>
/// [InvitationAggregate&lt;InvitationId&gt;]
/// public sealed partial class ShopInvitation;
/// </code>
/// It is the one class of Tenancy's an application may leave out: without it there are no invitations, and
/// nothing else changes.
/// </summary>
/// <typeparam name="TInvitationId">The application's invitation id, a <c>readonly partial record struct</c> marked <c>[EntityId&lt;T&gt;]</c>. Tenancy makes a new one with its <c>Create()</c>, which the generator writes for an id over a <see cref="Guid"/> and an id over anything else declares itself (DDD00067).</typeparam>
[AggregateRootTemplate(typeof(InvitationAggregate<,,,,>), CreatesIds = true)]
[TemplateArgument(1, typeof(TenantAggregateAttribute<>))]
[TemplateArgument(2, typeof(OrganizationUnitAttribute<>))]
[TemplateArgument(3, typeof(RoleAggregateAttribute<>))]
[TemplateArgument(4, typeof(SeatAggregateAttribute<>))]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class InvitationAggregateAttribute<TInvitationId> : Attribute;
