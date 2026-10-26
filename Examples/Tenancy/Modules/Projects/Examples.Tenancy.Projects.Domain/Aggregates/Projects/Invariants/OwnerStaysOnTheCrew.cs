using DDDToolkit.Invariants;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects;

public sealed partial class Project
{
    /// <summary>
    /// The owner is on the crew with no end, holding no project role: the owner holds what an owner does by owning
    /// the project. The member list refuses what would break it before anything changes; this is the same rule after
    /// the fact, as the Membership package checks it.
    /// </summary>
    public sealed class OwnerStaysOnTheCrew : IInvariant<Project>
    {
        /// <inheritdoc />
        public string Code => ProjectRefusals.OwnerProtected;

        /// <inheritdoc />
        public InvariantFailure? Check(Project entity) => entity.Members.OwnerStays();
    }
}
