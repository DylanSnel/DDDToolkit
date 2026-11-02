using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// What every command and query of this module implements, to say what it requires of its caller.
/// </summary>
/// <remarks>
/// The one line of access plumbing the module writes, and on it the module's default, <c>[InTenant]</c>: a caller who
/// works in the tenant, which a request of the module that says nothing of its own requires. The queries any seat may
/// send take it so, such as <c>TenantProjectRoles</c> and <c>PermissionsHeldAtRoot</c>. A request that says more says it with an attribute of its
/// own, or a marker on one of its members, and keeps only that: a case of the Membership package's for a permission held on a project,
/// <c>[OnResource(ChangeProjectName.RequiredPermission)]</c> on the project and <c>[ExpectedVersion]</c> on the version the
/// caller read, or <c>[SeenWith&lt;ProjectId&gt;(permission)]</c> on a list; one of Tenancy's, <c>[RequiresPermission(permission)]</c>
/// for the whole tenant and <c>[Operator]</c>; or the module's own case of a permission held at a unit where a project is
/// to be, <c>[AtProjectUnit(permission)]</c> (<see cref="ProjectsRequirement"/>). Several markers all hold, so a request that
/// requires two permissions in two places says both, as <c>MoveProjectToUnit</c> does. A request whose requirement depends
/// on its values answers <see cref="IRequireAccess.RequiredAccess"/> itself, as <c>OpenProject</c> does, which requires the
/// owner's permission only where it names an owner; it implements the member explicitly, so the requirement is said next to
/// the request's own fields without becoming one of them. The
/// interface is partial because the toolkit's generator writes <see cref="IRequireAccess.RequiredAccess"/> into it, one
/// line per request, the default's call for each that takes it, and the pipeline behavior beside it,
/// <c>ProjectsAccessBehavior&lt;TMessage, TResponse&gt;</c>, which asks the checks registered for this interface before
/// every handler of the module.
/// <para>
/// The default has a price, which the module pays on purpose: a command that forgot its permission would take it, and let
/// every seat of the tenant through, with nothing said when it builds. So the sample's <c>AccessDeclarationTests</c>
/// pins what each request requires, and a forgotten permission fails there.
/// </para>
/// <para>
/// A request that implemented no such interface would pass the behavior untouched, which is why a test fails
/// for it: every command and query declares what it requires.
/// </para>
/// </remarks>
[AccessRequests, InTenant]
public partial interface IProjectsRequest : IRequireAccess;
