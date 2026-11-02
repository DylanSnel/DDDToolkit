using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// What every command and query of this module implements, to say what it requires of its caller.
/// </summary>
/// <remarks>
/// The module writes this one line of access plumbing. The line declares the module's default, <c>[InTenant]</c>. A
/// request of the module that states no requirement of its own requires a caller who works in the tenant. The queries
/// that any seat may send use this default, such as <c>TenantProjectRoles</c> and <c>PermissionsHeldAtRoot</c>. A request
/// that requires more states it with an attribute or a marker, and gets only that requirement:
/// <list type="bullet">
/// <item>a case of the Membership package for a permission on a project: <c>[OnResource(ProjectPermissionNames.Edit)]</c>
/// on the project and <c>[ExpectedVersion]</c> on the version the caller read, or <c>[SeenWith&lt;ProjectId&gt;(permission)]</c>
/// on a list;</item>
/// <item>a case of Tenancy: <c>[RequiresPermission(permission)]</c> for the whole tenant, or <c>[Operator]</c>;</item>
/// <item>the module's own case of a permission at the unit where a project is to be: <c>[AtProjectUnit(permission)]</c>
/// (<see cref="ProjectsRequirement"/>).</item>
/// </list>
/// When a request has several markers, all of them are required. <c>MoveProjectToUnit</c> has a marker on the project
/// and a marker on the unit. A request whose requirement depends on its values implements
/// <see cref="IRequireAccess.RequiredAccess"/> itself. <c>OpenProject</c> does this, because it requires the owner's
/// permission only when it names an owner. It implements the member explicitly, so the requirement is written next to the
/// request's fields without becoming one of them. The interface is partial because the toolkit's generator writes
/// <see cref="IRequireAccess.RequiredAccess"/> into it: one line per request, and the default's call for the requests
/// that take the default. Beside the interface the generator writes the pipeline behavior
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
