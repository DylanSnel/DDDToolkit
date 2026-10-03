using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// What every command and query of this module implements, to say what it requires of its caller.
/// </summary>
/// <remarks>
/// The one line of access plumbing the module writes. A request answers <see cref="IRequireAccess.RequiredAccess"/>
/// with a case of the Membership package's for a key held on a project (<c>MemberAccess.On</c>,
/// <c>MemberAccess.SeenWith</c>), of the module's own for a key held at a unit (<see cref="ProjectsRequirement"/>),
/// or of Tenancy's; it implements the member explicitly, so the requirement is said next to the request's own
/// fields without becoming one of them.
/// The checks registered for this interface hold the caller to it, and the pipeline behavior the toolkit's
/// generator writes for the interface, <c>ProjectsAccessBehavior&lt;TMessage, TResponse&gt;</c>, asks them before
/// every handler of the module.
/// <para>
/// A request that implemented no such interface would pass the behavior untouched, which is why a test fails
/// for it: every command and query declares what it requires.
/// </para>
/// </remarks>
[AccessRequests]
public interface IProjectsRequest : IRequireAccess;
