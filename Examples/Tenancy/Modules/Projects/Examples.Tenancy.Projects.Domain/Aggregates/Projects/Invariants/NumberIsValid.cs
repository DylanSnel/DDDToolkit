using DDDToolkit.Invariants;

namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects;

// One of the project's rules stated after the fact. Each method of Project refuses its precondition before it
// changes anything; the rules in this folder run on every save, and catch what code that went round the methods
// left behind. They carry the refusals' codes, so a client reads the same code either way. Each is nested in
// Project, as a partial of it, because the generator finds an aggregate's rules among its own nested types, which
// also lets a rule ask the member list, which is the project's own and private.
public sealed partial class Project
{
    /// <summary>A number is trimmed and 1 to <see cref="LongestNumber"/> characters.</summary>
    public sealed class NumberIsValid : IInvariant<Project>
    {
        /// <inheritdoc />
        public string Code => ProjectRefusals.NumberInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(Project entity)
            => entity.Number is { Length: > 0 and <= LongestNumber } number && number == number.Trim()
                ? null
                : ProjectRefusals.Failure(ProjectRefusals.NumberInvalid, ("Max", LongestNumber));
    }
}
