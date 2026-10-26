using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Tenants.Application.Access;

/// <summary>
/// What every command and query of this module implements, to say what it requires of its caller.
/// </summary>
/// <remarks>
/// The one line of access plumbing the module writes. A request says who may send it with an attribute: one of
/// Tenancy's cases, such as <c>[RequiresPermission(TenancyPermissions.RolesManage)]</c> for a permission held for the whole tenant,
/// <c>[AtUnit]</c> on the unit for a permission held there, or <c>[InTenant]</c>; or one of the toolkit's, such as
/// <c>[SignedIn]</c> for a signed-in user who has no seat yet, and <c>[OrOwn]</c> on the seat beside a permission, for a request
/// about the caller's own seat that another sends with the permission, as <c>RenameSeat</c> does. Where the requirement is computed from the request's
/// values it answers <see cref="IRequireAccess.RequiredAccess"/> itself, as <c>SeatGrants</c> does. The interface is
/// partial because the toolkit's generator writes <see cref="IRequireAccess.RequiredAccess"/> into it, one line per
/// request that says it with an attribute, and the pipeline behavior beside it,
/// <c>TenantsAccessBehavior&lt;TMessage, TResponse&gt;</c>, which asks the checks registered for this interface
/// before every handler of the module.
/// <para>
/// A requirement says who may send a request, not what the work runs with. System work is no requirement but a
/// scope trusted code begins around the work, <c>TenancyUseCases.BeginSystemIn(tenant)</c> as the seeding does, and
/// what only system work may do the handler or the package's use case refuses everyone else, with
/// <c>access.system-only</c>, as <c>MarkTenantAsDemo</c>'s does.
/// </para>
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
public partial interface ITenantsRequest : IRequireAccess;
