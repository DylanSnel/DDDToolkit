namespace DDDToolkit.Access;

/// <summary>
/// What a command or a query implements to say what it requires of its caller: who may send it.
/// </summary>
/// <remarks>
/// A module declares one interface of its own that derives from this one, and every request of the module
/// implements that. A request says what it requires with an attribute where the requirement reads no value of the
/// request but a key and an id it carries, and answers <see cref="RequiredAccess"/> itself where it computes it:
/// <code>
/// [AccessRequests]
/// public partial interface IBillingRequest : IRequireAccess;
///
/// [RequiresKey(BillingKeys.Close)]
/// public sealed record CloseInvoice(string Number) : IBillingRequest;
///
/// public sealed record ListInvoices(OrganizationUnitId? Unit) : IBillingRequest
/// {
///     AccessRequirement IRequireAccess.RequiredAccess =&gt; Unit is { } unit
///         ? TenancyAccess.AtUnit(BillingKeys.Read, unit)
///         : TenancyAccess.ForTheWholeTenant(BillingKeys.Read);
/// }
/// </code>
/// The toolkit's generator turns the attributes into plain C# at compile time: the module's interface, marked
/// <c>[AccessRequests]</c> and declared <c>partial</c>, gets <see cref="RequiredAccess"/> implemented with one line per
/// request that states its requirement with attributes, and a request that answers the property itself answers with
/// its own. So every reader of the property, the module's checks, a handler that asks them again or a test, reads the
/// same requirement either way.
/// <para>
/// The module's interface is what its checks are registered for
/// (<see cref="AccessCheckServiceCollectionExtensions.AddAccessCheck{TRequests, TCheck}"/>) and what
/// <see cref="AccessChecks{TRequests}"/> is closed over, so a request of one module is never held to the
/// checks of another.
/// </para>
/// <para>
/// A request implements the member explicitly, so the requirement is said next to the request's own fields
/// without becoming one of them. A request that implemented no such interface would pass unchecked, which is
/// why a test of the application holds every command and query to declaring one.
/// </para>
/// </remarks>
public interface IRequireAccess
{
    /// <summary>
    /// What the request requires of its caller. Never null: a request that anyone may send says so with
    /// <see cref="AllowAnonymousCallersAttribute"/> or <see cref="AccessRequirement.AllowAnonymous"/>, and one that
    /// declares nothing is stopped.
    /// </summary>
    AccessRequirement RequiredAccess { get; }
}
