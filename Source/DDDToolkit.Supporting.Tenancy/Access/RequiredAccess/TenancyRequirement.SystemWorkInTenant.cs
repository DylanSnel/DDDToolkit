namespace DDDToolkit.Supporting.Tenancy.Access;

public abstract partial record TenancyRequirement
{
    /// <summary>
    /// The caller is system work in a tenant: the application's own work there, begun with
    /// <c>TenancyWork.BeginSystemIn</c>. A seat is refused with <c>tenancy.system-only</c>, whatever it holds,
    /// and nobody with its own reason.
    /// </summary>
    /// <remarks>For what nobody in a tenant decides: what seeds it, imports into it or corrects it.</remarks>
    public sealed record SystemWorkInTenant : TenancyRequirement;
}
