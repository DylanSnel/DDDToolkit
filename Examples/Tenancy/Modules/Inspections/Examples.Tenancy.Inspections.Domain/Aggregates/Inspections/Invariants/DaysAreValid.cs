using DDDToolkit.Invariants;

namespace Examples.Tenancy.Inspections.Domain.Aggregates.Inspections;

public sealed partial class Inspection
{
    /// <summary>The days an inspection covers are a range whose last day is not before its first.</summary>
    /// <remarks>
    /// Whether they lie within the project's planned range is not stated here: the plan is Projects', asked when
    /// the inspection is recorded, and the inspection keeps no copy of it to check against afterwards.
    /// </remarks>
    public sealed class DaysAreValid : IInvariant<Inspection>
    {
        /// <inheritdoc />
        public string Code => InspectionRefusals.DaysInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(Inspection entity)
            => entity.Days is { IsValid: true }
                ? null
                : InspectionRefusals.Failure(InspectionRefusals.DaysInvalid);
    }
}
