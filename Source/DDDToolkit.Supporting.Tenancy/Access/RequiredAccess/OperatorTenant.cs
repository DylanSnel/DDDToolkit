namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Where the operator work of a command marked <see cref="OperatorAttribute"/> acts. Each command says which, so a client
/// that always sends a tenant cannot move a command that acts in no tenant into one.
/// </summary>
public enum OperatorTenant
{
    /// <summary>
    /// The tenant the command names with a <see cref="ForTenantAttribute"/> member, or no tenant where it names none. A tenant
    /// the request names beside a command without such a member is ignored. The default.
    /// </summary>
    FromCommand,

    /// <summary>
    /// The tenant the request names beside the command, as tenant selection reads it, the <c>Tenant</c> header in the sample
    /// host. The operator gets no seat there. A request that names no tenant is refused with <c>tenancy.tenant-required</c>,
    /// and one that names a tenant nobody has with <c>tenancy.not-seated</c>, as for a seat.
    /// </summary>
    FromRequest,
}
