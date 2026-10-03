using DDDToolkit.Supporting.Tenancy.Access;

namespace Examples.Tenancy.Inspections.Application.Access;

/// <summary>
/// The seat something is done by: the caller's own, or the one system work in the tenant acts for.
/// </summary>
/// <remarks>
/// Read in one place, because two ask. The access check asks first, so work that acts for no seat is stopped
/// before the project is asked about; the handler asks again, for the seat it records. Both read the caller where
/// Tenancy keeps it, so they get the same seat, and the same mistake is the same exception whichever finds it.
/// </remarks>
internal static class ActingSeat
{
    /// <summary>The seat the caller in <paramref name="scope"/> acts as, or for.</summary>
    /// <param name="scope">The tenant the caller works in, as Tenancy answers it.</param>
    /// <exception cref="InvalidOperationException">
    /// System work acting for no seat: a mistake in the calling code, not a refusal a client could act on.
    /// </exception>
    public static SeatId Of(TenantInScope<TenantId, SeatId> scope)
        => scope.Seat
            ?? throw new InvalidOperationException(
                "System work records an inspection for the seat it acts for: begin it with TenancyWork.BeginSystemIn(tenant, actingSeat).");
}
