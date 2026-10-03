namespace DDDToolkit.Supporting.Tenancy.Access;

public abstract partial record TenancyRequirement
{
    /// <summary>
    /// The caller works in a tenant: a seat there, or system work there. Nobody is refused with its own reason,
    /// such as <c>tenancy.not-seated</c>.
    /// </summary>
    /// <remarks>
    /// For a request that answers only what the caller itself holds or may see, where holding nothing is an
    /// answer and not a refusal, and for one whose handler asks for whatever else it takes in plain sight.
    /// </remarks>
    public sealed record InTenant : TenancyRequirement;
}
