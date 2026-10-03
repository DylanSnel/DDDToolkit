using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Tenants.Application.Access;

/// <summary>
/// What every command and query of this module implements, to say what it requires of its caller.
/// </summary>
/// <remarks>
/// The one line of access plumbing the module writes. A request answers <see cref="IRequireAccess.RequiredAccess"/>
/// with one of Tenancy's cases (<see cref="TenancyRequirement"/>), or with <see cref="AccessRequirement.Open"/> and
/// the reason it requires nothing; it implements the member explicitly, so the requirement is said next to the
/// request's own fields without becoming one of them. The checks registered for this interface hold the caller
/// to it, and the pipeline behavior the toolkit's generator writes for the interface,
/// <c>TenantsAccessBehavior&lt;TMessage, TResponse&gt;</c>, asks them before every handler of the module.
/// <para>
/// A request that implemented no such interface would pass the behavior untouched, which is why a test fails
/// for it: every command and query declares what it requires, or is marked open with a reason.
/// </para>
/// </remarks>
[AccessRequests]
public interface ITenantsRequest : IRequireAccess;
