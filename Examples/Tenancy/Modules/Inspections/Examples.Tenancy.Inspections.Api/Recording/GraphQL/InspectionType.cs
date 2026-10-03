using Examples.Tenancy.Inspections.Api.GraphQL;
using Examples.Tenancy.Inspections.Application.Recording;
using HotChocolate;
using HotChocolate.Types;

namespace Examples.Tenancy.Inspections.Api.Recording.GraphQL;

/// <summary>
/// The type <c>Inspection</c>, declared over the record the application answers with: its fields are the
/// record's, so nothing is copied into an output of this project's own.
/// </summary>
/// <remarks>
/// What differs from the record is said here and nowhere else: the project and the recording seat are
/// references by key, since the rest of a project is Projects' to give and a seat's name is Tenancy's, and the
/// gateway asks them. The days it covers are the value object the modules share, <c>DateRange</c>, and who wrote
/// its row is <c>ChangedBy</c>: both are types of this schema as they are, and of Projects' too. A value object
/// is shareable by itself, so the gateway composes one of each.
/// </remarks>
[ObjectType<InspectionOverview>]
internal static partial class InspectionType
{
    static partial void Configure(IObjectTypeDescriptor<InspectionOverview> descriptor) => descriptor.Name("Inspection");

    // Named as the record's members, so each takes its member's place: the id becomes the reference.

    /// <summary>The project it is of.</summary>
    public static ReferencedProject? GetProject([Parent] InspectionOverview inspection) => new(inspection.Project);

    /// <summary>The seat that recorded it.</summary>
    public static ReferencedSeat? GetRecordedBy([Parent] InspectionOverview inspection) => new(inspection.RecordedBy);
}
