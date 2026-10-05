using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// What a request requires of its caller where Tenancy is the one that knows: a seat in the tenant, a key for the
/// whole of it or at one of its units, or an operator. A request says which case through
/// <see cref="IRequireAccess"/>, spelled with <see cref="TenancyAccess"/>:
/// <code>
/// public sealed record CloseTheBooks(int Year) : IBillingRequest
/// {
///     AccessRequirement IRequireAccess.RequiredAccess =&gt; TenancyAccess.ForTheWholeTenant(BillingKeys.CloseTheBooks);
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
/// declare. What is about who is calling and nothing else is the core's: anyone
/// (<see cref="AccessRequirement.AllowAnonymous"/>), a signed-in user who need not have a seat yet
/// (<see cref="AccessRequirement.SignedIn"/>), and the application itself
/// (<see cref="AccessRequirement.RequiresSystemWork"/>).
/// <para>
/// A case says the first thing a request requires, the one the request itself can name. What Tenancy's own use
/// cases ask beyond it, such as who may give a role that manages access, or whether a unit may move with what
/// hangs below it, they ask themselves, whoever calls them, and the database's policies ask again: those rules
/// are the package's, and a request does not spell them.
/// </para>
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
