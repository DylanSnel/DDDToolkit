namespace DDDToolkit.Supporting.Tenancy.Access;

public abstract partial record TenancyRequirement
{
    /// <summary>
    /// The caller works in the tenant the request was sent for: a seat there, or system work there. Nobody is
    /// refused with its own reason, such as <c>tenancy.not-seated</c>. A request declares it with
    /// <see cref="TenancyAccess.InTenant"/>.
    /// </summary>
    /// <remarks>
    /// For a request that answers only what the caller itself holds or may see, where holding nothing is an
    /// answer and not a refusal; for one whose handler asks for whatever else it takes in plain sight; and for
    /// one handed to a use case of the package whose first rule needs something read, such as the unit an
    /// invitation is for, or the parent a unit hangs under now. That use case asks the rest itself.
    /// </remarks>
    public sealed record InTenant : TenancyRequirement
    {
        /// <summary>Made by <see cref="TenancyAccess.InTenant"/>, the one spelling a request writes.</summary>
        internal InTenant()
        {
        }
    }
}
