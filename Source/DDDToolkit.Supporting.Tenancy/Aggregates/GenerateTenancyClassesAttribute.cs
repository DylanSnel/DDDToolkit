using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Has the generator write Tenancy's classes this project does not declare, as the package ships them, and every id
/// of theirs that no project declares. One line instead of five classes and four ids:
/// <code>
/// [assembly: GenerateTenancyClasses]
/// </code>
/// The project then has a <c>Tenant</c>, an <c>Organization</c>, an <c>OrganizationUnit</c>, a <c>Role</c> and a
/// <c>Seat</c>, each declared with its template, and a <c>TenantId</c>, an <c>OrganizationUnitId</c>, a <c>RoleId</c>
/// and a <c>SeatId</c>, each an <c>[EntityId&lt;Guid&gt;]</c>: public, in the project's root namespace, with everything
/// the toolkit's generators write for a class and an id declared by hand, <c>modelBuilder.AddTenancy()</c> and the
/// class the use cases are named through, <c>TenancyUseCases</c>, among them, and Membership's member list over a
/// written <c>SeatId</c>. An organization shares its tenant's id, so there is no <c>OrganizationId</c>.
/// <para>
/// <b>Seen from the next project up.</b> Other generators of this project, HotChocolate's for one, do not see what the
/// switch wrote: a module of one project with an <c>[ObjectType&lt;Role&gt;]</c> declares that class itself. Code outside
/// the root namespace imports the written types with a <c>global using</c> of the root namespace, since a file's using
/// does not reach the parts the generators write.
/// </para>
/// <para>
/// <b>Your own class wins.</b> Declare one of them yourself, when it needs fields, rules or behaviour, and the
/// generator writes the rest: <c>[SeatAggregate&lt;SeatId&gt;] public sealed partial class Seat { ... }</c> anywhere in
/// the project, and no <c>Seat</c> is written. An id you declare, here or in a project of the module that this one
/// references, is taken rather than written. Every generated class and id says in its documentation that it was
/// generated, and how to replace it.
/// </para>
/// <para>
/// <b>In a module split by layer</b> the ids belong in the contracts project, which the other modules reference, and
/// the classes in the domain project: put <see cref="GenerateTenancyIdsAttribute"/> in the first and this in the
/// second, which then takes the ids from the first. A contracts project that should not reference this package
/// declares the four ids itself, one line each.
/// </para>
/// <para>
/// The invitation is not written: it is the one class an application may leave out, and declaring it is what turns
/// invitations on. An application that wants them declares <c>[InvitationAggregate&lt;InvitationId&gt;]</c> and the id.
/// </para>
/// </summary>
[TemplateDefaults(
    typeof(TenantAggregateAttribute<>),
    typeof(OrganizationAggregateAttribute<>),
    typeof(OrganizationUnitAttribute<>),
    typeof(RoleAggregateAttribute<>),
    typeof(SeatAggregateAttribute<>))]
[AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
public sealed class GenerateTenancyClassesAttribute : Attribute;
