namespace DDDToolkit.Access;

/// <summary>
/// What a command or a query implements to say what it requires of its caller.
/// </summary>
/// <remarks>
/// A module declares one interface of its own that derives from this one, and every request of the module
/// implements that:
/// <code>
/// public interface IBillingRequest : IRequireAccess;
///
/// public sealed record CloseInvoice(InvoiceId Invoice) : IBillingRequest
/// {
///     AccessRequirement IRequireAccess.RequiredAccess => new BillingAccess.OnInvoice(BillingKeys.Close, Invoice);
/// }
/// </code>
/// The module's interface is what its checks are registered for
/// (<see cref="AccessCheckServiceCollectionExtensions.AddAccessCheck{TRequests, TCheck}"/>) and what
/// <see cref="AccessChecks{TRequests}"/> is closed over, so a request of one module is never held to the
/// checks of another.
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
    /// <see cref="AccessRequirement.AllowAnonymous"/>, and one that declares nothing is stopped.
    /// </summary>
    AccessRequirement RequiredAccess { get; }
}
