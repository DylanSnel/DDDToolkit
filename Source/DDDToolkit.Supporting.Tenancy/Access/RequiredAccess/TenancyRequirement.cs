using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// What a request requires of its caller where Tenancy is the one that knows: who is calling, in which tenant,
/// and what it holds for the whole of it. A request says which case, through <see cref="IRequireAccess"/>:
/// <code>
/// public sealed record CloseTheBooks(int Year) : IBillingRequest
/// {
///     AccessRequirement IRequireAccess.RequiredAccess =&gt; new TenancyRequirement.ForTheWholeTenant(BillingKeys.CloseTheBooks);
/// }
/// </code>
/// and the check the storage package registers for the module's request interface, with
/// <c>services.AddTenancyAccess&lt;IBillingRequest, BillingContext&gt;()</c>, holds the caller to it before the
/// handler runs. A caller that does not meet a case is refused with Tenancy's own codes, whichever module the
/// request is of.
/// </summary>
/// <remarks>
/// A closed set: the constructor is private, so the cases nested here are all there are, and each is a record,
/// so two requirements that say the same are equal and a test can hold every request to the one it is meant to
/// declare. A request that requires nothing at all says so with <see cref="AccessRequirement.Open"/> and its
/// reason.
/// <para>
/// The cases are about the tenant: as a whole, or at one of its units (<see cref="AtUnit{TUnitId}"/>). What is
/// held on one thing an application keeps at a unit is that module's own to declare and to check, with the
/// access questions asked inside its own statements.
/// </para>
/// </remarks>
[AccessCheckRegistration("services.AddTenancyAccess<{TRequests}, TContext>(), with the module's context for TContext")]
public abstract partial record TenancyRequirement : AccessRequirement
{
    private TenancyRequirement()
    {
    }
}
