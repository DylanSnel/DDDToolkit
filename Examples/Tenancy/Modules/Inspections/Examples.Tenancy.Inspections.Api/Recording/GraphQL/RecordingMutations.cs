using Examples.Tenancy.Inspections.Application.Recording;
using Examples.Tenancy.Inspections.Application.Recording.Commands;
using Examples.Tenancy.Inspections.Application.Recording.Queries;
using HotChocolate;
using HotChocolate.Types.Relay;
using Mediator;

namespace Examples.Tenancy.Inspections.Api.Recording.GraphQL;

/// <summary>
/// What records an inspection. The mutation sends the command its route sends, then reads the inspection it
/// recorded and answers that; a refusal is not caught here, and arrives in the payload's <c>errors</c>.
/// </summary>
internal static class RecordingMutations
{
    /// <summary>
    /// Records an inspection of an open project, by the calling seat. <paramref name="days"/> are the days it
    /// covers, a first and a last; left out, the day it is recorded, in UTC. On a planned project they lie within
    /// its planned range.
    /// </summary>
    [Mutation]
    public static async Task<InspectionOverview?> InspectionRecordAsync([ID("Project")] ProjectId project, string title, DateRange? days, [Service] ISender sender, CancellationToken cancellationToken)
    {
        var id = await sender.Send(new RecordInspection(project, title, days), cancellationToken);
        return await sender.Send(new InspectionDetail(project, id), cancellationToken);
    }
}
