using DDDToolkit.Invariants;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects;

public sealed partial class Project
{
    /// <summary>
    /// A seat is on the crew once: one membership, which holds its roles. The member list refuses a second one
    /// before anything changes; this is the same rule after the fact, as the Membership package checks it.
    /// </summary>
    public sealed class OneMembershipPerSeat : IInvariant<Project>
    {
        /// <inheritdoc />
        public string Code => ProjectRefusals.AlreadyOnCrew;

        /// <inheritdoc />
        public InvariantFailure? Check(Project entity) => entity.Members.OneMembershipPerMember();
    }
}
