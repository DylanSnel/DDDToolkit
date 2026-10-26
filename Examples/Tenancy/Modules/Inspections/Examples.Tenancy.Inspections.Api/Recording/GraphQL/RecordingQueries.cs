using Examples.Tenancy.Inspections.Api.GraphQL;
using HotChocolate;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;

namespace Examples.Tenancy.Inspections.Api.Recording.GraphQL;

/// <summary>
/// The one field of the <c>Query</c> type of Inspections' schema, and it is not a client's: the way in for the
/// gateway. What a client asks about a project's inspections it asks of the project,
/// <c>project { inspections }</c>, which <see cref="ProjectType"/> answers.
/// </summary>
internal static class RecordingQueries
{
    /// <summary>
    /// A project by its id, for the gateway alone: how it gets from a project Projects answered to the fields
    /// Inspections adds to it. Internal, so it is no field of the schema a client is offered.
    /// </summary>
    /// <remarks>
    /// It answers the key it was given and reads nothing, so it says nothing about whether the project exists or
    /// is the caller's to see. That is decided where the inspections are read: the field's query passes the
    /// module's access check, and answers nothing for a project out of the caller's reach.
    /// </remarks>
    [Query]
    [Lookup]
    [Internal]
    public static ReferencedProject? GetProjectById([ID("Project")] ProjectId id) => new(id);
}
