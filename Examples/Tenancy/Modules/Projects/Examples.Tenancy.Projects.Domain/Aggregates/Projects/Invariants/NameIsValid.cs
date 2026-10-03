using DDDToolkit.Invariants;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects;

public sealed partial class Project
{
    /// <summary>A name is trimmed and 1 to <see cref="LongestName"/> characters.</summary>
    public sealed class NameIsValid : IInvariant<Project>
    {
        /// <inheritdoc />
        public string Code => ProjectRefusals.NameInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(Project entity)
            => entity.Name is { Length: > 0 and <= LongestName } name && name == name.Trim()
                ? null
                : ProjectRefusals.Failure(ProjectRefusals.NameInvalid, ("Max", LongestName));
    }
}
