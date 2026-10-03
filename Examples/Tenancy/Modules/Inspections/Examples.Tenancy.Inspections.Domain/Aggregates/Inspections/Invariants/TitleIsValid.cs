using DDDToolkit.Invariants;

namespace Examples.Tenancy.Inspections.Domain.Aggregates.Inspections;

public sealed partial class Inspection
{
    /// <summary>A title is trimmed and 1 to <see cref="LongestTitle"/> characters.</summary>
    /// <remarks>
    /// Nested in the inspection, as a partial of it, because the generator finds an aggregate's rules among its
    /// own nested types. It carries the refusal's code, so a client reads one code whether the constructor
    /// refused or the save's check caught code that went round it.
    /// </remarks>
    public sealed class TitleIsValid : IInvariant<Inspection>
    {
        /// <inheritdoc />
        public string Code => InspectionRefusals.TitleInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(Inspection entity)
            => entity.Title is { Length: > 0 and <= LongestTitle } title && title == title.Trim()
                ? null
                : InspectionRefusals.Failure(InspectionRefusals.TitleInvalid, ("Max", LongestTitle));
    }
}
