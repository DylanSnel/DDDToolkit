using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Tenants.Application.Access;

/// <summary>
/// What every command and query of this module implements, to say what it requires of its caller.
/// </summary>
/// <remarks>
/// The one line of access plumbing the module writes. A request answers <see cref="IRequireAccess.RequiredAccess"/>
/// with what it requires: one of Tenancy's cases (<see cref="TenancyAccess"/>), such as a key held for the whole
/// tenant, or one of the toolkit's, such as a signed-in user (<see cref="AccessRequirement.SignedIn"/>) or system
/// work (<see cref="AccessRequirement.RequiresSystemWork"/>). It implements the member explicitly, so the
/// requirement is said next to the request's own fields without becoming one of them. The checks registered for
/// this interface hold the caller to it, and the pipeline behavior the toolkit's generator writes for the
/// interface, <c>TenantsAccessBehavior&lt;TMessage, TResponse&gt;</c>, asks them before every handler of the module.
/// <para>
/// Most of this module's requests are handed to a use case of the Tenancy package, which asks again for what the
/// request requires and then for what only it can read, such as who may give a role that manages access. The
/// request says the first; the package keeps the rest, and the database's policies ask once more.
/// </para>
/// <para>
/// A request that implemented no such interface would pass the behavior untouched, which is why a test fails
/// for it: every command and query declares what it requires.
/// </para>
/// </remarks>
[AccessRequests]
public interface ITenantsRequest : IRequireAccess;
