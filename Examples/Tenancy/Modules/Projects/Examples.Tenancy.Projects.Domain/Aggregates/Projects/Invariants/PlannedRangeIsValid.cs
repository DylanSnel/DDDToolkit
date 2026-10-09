using DDDToolkit.Invariants;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects;

public sealed partial class Project
{
    /// <summary>A planned range, when the project has one, has a last day that is not before its first.</summary>
    public sealed class PlannedRangeIsValid : IInvariant<Project>
    {
        /// <inheritdoc />
        public string Code => ProjectRefusals.PlannedRangeInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(Project entity)
            => entity.Planned is null || entity.Planned.IsValid
                ? null
                : ProjectRefusals.Failure(ProjectRefusals.PlannedRangeInvalid);
    }
}
