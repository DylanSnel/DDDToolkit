using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// Declares the role class of one of the application's resources: the roles a member of that resource can
/// hold, kept as rows a customer makes and changes. The generator derives it from
/// <see cref="KeptRoleAggregate{TRoleId}"/>, closed over the id given here:
/// <code>
/// [KeptRole&lt;PlotRoleId, Plot&gt;]
/// public sealed partial class PlotRole
/// {
///     public PlotRole(PlotRoleId id, KeptRoleDraft draft) : base(id, draft, PlotMembership.Rules) { }
/// }
/// </code>
/// A role is an aggregate of its own, beside the resource: it is made, renamed, given keys and archived by
/// itself, and a member of the resource holds it by its id
/// (<c>[Member&lt;PlotGardenerId, UserId, PlotRoleId, Plot&gt;]</c>). The resource's rules say that its
/// roles are kept this way (<see cref="Access.MembershipRules.RolesKept"/>).
/// <para>
/// The second type is the resource the roles are of. The role itself has no use for it: it is how the roles
/// of a resource are found where its members are asked about, so an application with plots and with sheds
/// declares a role class for each, and neither's roles are the other's. So the template may be declared more
/// than once in a project.
/// </para>
/// <para>
/// The class is the application's: it adds what only it knows, the customer a role belongs to say, in
/// properties of its own, and keeps the rows of one customer from another's by its own rule, as it does for
/// any aggregate. The package assumes no such column.
/// </para>
/// </summary>
/// <typeparam name="TRoleId">The role class's id, a <c>readonly partial record struct</c> marked <c>[EntityId&lt;T&gt;]</c>: what a member holds a role by.</typeparam>
/// <typeparam name="TResource">The resource the roles are of: the application's aggregate whose members hold them.</typeparam>
[AggregateRootTemplate(typeof(KeptRoleAggregate<>), AllowSeveral = true)]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class KeptRoleAttribute<TRoleId, TResource> : Attribute
    where TResource : class;
