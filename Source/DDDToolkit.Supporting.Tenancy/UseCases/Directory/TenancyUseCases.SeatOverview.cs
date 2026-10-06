namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// A seat and what it may do where: the application's own seat, whole, with only what is not on the seat itself
    /// beside it. The seat carries its id, its identity, its status, where it is placed with the roles granted there
    /// for which period, and every field the application added; Tenancy adds the tenant it is in, the path of each
    /// unit it is placed at, the roles its grants name, and every key it holds at <see cref="AsOf"/> with where that
    /// key reaches (<see cref="TenancyDirectory.WhoAmIAsync"/>).
    /// <para>
    /// Nothing of it is the seat a second time, so an application shows the seat by what it chose, a name its seat
    /// class keeps or a profile found by the identity, from the seat itself, and puts its placements beside the paths
    /// and role names with <see cref="UnitOf"/> and <see cref="RoleOf"/>. What leaves is what the application selects:
    /// the seat has its identity.
    /// </para>
    /// <para>
    /// The seat was read for the overview and is tracked by nobody: nothing done to it is saved, by this question or
    /// by a save later in the same unit of work. Nothing in the record is the caller's own but the question that made
    /// it, so it shows any seat the same way.
    /// </para>
    /// </summary>
    /// <param name="Tenant">The tenant the seat is in.</param>
    /// <param name="Seat">The seat, the application's own class, with its placements and their grants.</param>
    /// <param name="Units">Each unit the seat is placed at, once, by its path from the root.</param>
    /// <param name="Roles">
    /// Each role the seat's grants name, once, by name, whether or not the grant applies now; a role the store does
    /// not answer, one a filter of the application's own hides, is left out rather than made up.
    /// </param>
    /// <param name="Keys">Every key the seat holds at <paramref name="AsOf"/>, where it is granted and every unit it reaches.</param>
    /// <param name="AsOf">
    /// The moment the overview holds for: the keys are those held then, and a grant of the seat applies then when
    /// <c>grant.AppliesAt(AsOf)</c>.
    /// </param>
    public sealed record SeatOverview(
        TenantSummary Tenant,
        TSeat Seat,
        IReadOnlyList<UnitRef> Units,
        IReadOnlyList<RoleSummary> Roles,
        IReadOnlyList<KeyReach> Keys,
        DateTimeOffset AsOf)
    {
        /// <summary>A unit the seat is placed at, by its path: what <see cref="Units"/> holds for it.</summary>
        /// <param name="unit">The unit of one of the seat's placements.</param>
        /// <exception cref="KeyNotFoundException">The seat is placed at no such unit.</exception>
        public UnitRef UnitOf(TUnitId unit)
            => Units.FirstOrDefault(found => found.Id.Equals(unit))
               ?? throw new KeyNotFoundException($"The seat is placed at no unit {unit}: an overview names the units of the seat's own placements.");

        /// <summary>A role the seat's grants name: what <see cref="Roles"/> holds for it.</summary>
        /// <param name="role">The role of one of the seat's grants.</param>
        /// <returns>
        /// The role; or <see langword="null"/> when a grant of the seat names it and the store answered no such role,
        /// as a filter of the application's own on its role class does, a soft delete say. The grant is the seat's
        /// own all the same, so a screen shows it with what the application chooses for a role it cannot name.
        /// </returns>
        /// <exception cref="KeyNotFoundException">No grant of the seat names such a role.</exception>
        public RoleSummary? RoleOf(TRoleId role)
            => Roles.FirstOrDefault(found => found.Id.Equals(role))
               ?? (Seat.Placements.Any(placement => placement.Grants.Any(grant => grant.RoleId.Equals(role)))
                   ? null
                   : throw new KeyNotFoundException($"No grant of the seat names a role {role}: an overview names the roles of the seat's own grants."));
    }
}
