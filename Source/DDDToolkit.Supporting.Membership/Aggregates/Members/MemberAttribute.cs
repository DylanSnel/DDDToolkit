using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Membership;

/// <summary>
/// Declares the member class of one of the application's resources. The generator derives it from
/// <see cref="MemberEntity{TId, TMemberId, TRoleId}"/>, closed over the first three types given here:
/// <code>
/// [Member&lt;DocumentShareId, UserId, NamedRole, Document&gt;]
/// public sealed partial class DocumentShare;
/// </code>
/// The resource's aggregate stays the application's own, declared as it always was, and holds its members as
/// a collection of this class. The package's generator writes the member list over that collection on the
/// aggregate, <c>Members</c>, which the aggregate's own methods change its members through
/// (<see cref="MemberList{TMember, TId, TMemberId, TRoleId}"/>).
/// <para>
/// The fourth type is the resource the members are of. The member itself has no use for it: it is what the
/// registrations of the resource are named after and closed over, so an application that declares the members
/// of a <c>Document</c> calls <c>AddDocumentMembership</c>, and writes neither the resource nor its id there.
/// </para>
/// <para>
/// An application declares one member class for each kind of resource it has members on, its documents and
/// its folders, each with a class, a table and rules of its own. So the template may be declared more than
/// once in a project, and everything the package offers is asked for by the member class or by the resource.
/// </para>
/// </summary>
/// <typeparam name="TId">The member class's own id, a <c>readonly partial record struct</c> marked <c>[EntityId&lt;T&gt;]</c>.</typeparam>
/// <typeparam name="TMemberId">
/// What a member is known by: the id of whoever can be a member of the resource, such as a user's, or that of
/// the place somebody has in an organization,
/// a <c>readonly partial record struct</c> marked <c>[EntityId&lt;T&gt;]</c>.
/// </typeparam>
/// <typeparam name="TRoleId">
/// What a role is known by: <see cref="NamedRole"/> when the roles are the ones the resource's rules declare,
/// or the id of the application's own roles, a <c>readonly partial record struct</c> marked <c>[EntityId&lt;T&gt;]</c>:
/// that of its role class for this resource (<see cref="CustomRoleAttribute{TRoleId, TResource}"/>), or of
/// roles it keeps elsewhere.
/// </typeparam>
/// <typeparam name="TResource">The resource the members are of: the application's aggregate that holds them.</typeparam>
/// <remarks>
/// Membership's Entity Framework package maps the resource's collection of this class, through
/// <c>AddDDDToolkitConventions()</c>, as <c>HasMembers</c> does: the template names that convention. A model that maps
/// the class without that package in the application is refused while it is built.
/// </remarks>
[EntityTemplate(typeof(MemberEntity<,,>), AllowSeveral = true)]
[ModelConvention("DDDToolkit.Supporting.Membership.EntityFramework.MembershipModelConvention, DDDToolkit.Supporting.Membership.EntityFramework")]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MemberAttribute<TId, TMemberId, TRoleId, TResource> : Attribute
    where TResource : class;
