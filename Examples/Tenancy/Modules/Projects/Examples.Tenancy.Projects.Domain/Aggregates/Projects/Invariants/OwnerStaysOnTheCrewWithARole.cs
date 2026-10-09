using DDDToolkit.Invariants;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects;

public sealed partial class Project
{
    /// <summary>
    /// The owner is on the crew with no end, and holds a role there with no end. Which role that is, the crew
    /// lead's, the commands see to; what can be told from the project alone is that the owner's place and a role
    /// of theirs do not run out. The member list refuses what would break it before anything changes; this is the
    /// same rule after the fact, as the Membership package checks it.
    /// </summary>
    public sealed class OwnerStaysOnTheCrewWithARole : IInvariant<Project>
    {
        /// <inheritdoc />
        public string Code => ProjectRefusals.OwnerProtected;

        /// <inheritdoc />
        public InvariantFailure? Check(Project entity) => entity.Members.OwnerStays();
    }
}
