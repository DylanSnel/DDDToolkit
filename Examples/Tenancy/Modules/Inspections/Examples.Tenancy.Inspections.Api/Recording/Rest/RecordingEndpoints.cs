using Examples.Tenancy.Inspections.Application.Recording.Commands;
using Examples.Tenancy.Inspections.Application.Recording.Queries;
using GreenDonut.Data;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Inspections.Api.Recording.Rest;

/// <summary>
/// Recording inspections over HTTP: a project's inspections, with whether the caller may record another, and
/// recording one. Each route sends the command or the query of the application project's <c>Recording</c>
/// feature.
/// </summary>
/// <remarks>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="InspectionsModule"/> says what all of them keep to. Internal: the module's entry maps them into
/// the group the host hands it.
/// </remarks>
internal static class RecordingEndpoints
{
    /// <summary>
    /// Maps <c>GET /projects/{id}/inspections</c> and <c>POST /projects/{id}/inspections</c> into
    /// <paramref name="group"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapRecordingEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // A project's inspections, and whether the caller may add one: the screen fills its button from that.
        // Newest first and a page at a time: "next" is the marker to send as "after" for the following page. The
        // page is HotChocolate's, asked for with its paging arguments, so the marker is the cursor of the page's
        // last inspection: the one a GraphQL client is given for the same row.
        group.MapGet("/projects/{id}/inspections", async (ProjectId id, string? after, int? size, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(Describe(await sender.Send(new ProjectInspections(id, new PagingArguments(first: size ?? ProjectInspections.DefaultPage, after: after)), cancellationToken))));

        // A missing title is passed on empty, so the inspection refuses it by its code rather than by a null
        // reference. The days are two ISO dates, both or neither: neither is the day it is recorded, and one alone
        // is a range that does not hold, which the inspection refuses. There is no route for a single inspection,
        // so the answer names the new one without a Location.
        group.MapPost("/projects/{id}/inspections", async (ProjectId id, InspectionToRecord body, ISender sender, CancellationToken cancellationToken) =>
        {
            var recorded = await sender.Send(new RecordInspection(id, body.Title ?? string.Empty, DateRange.Of(body.From, body.Until)), cancellationToken);
            return Results.Created((string?)null, new { id = recorded });
        });

        return group;
    }

    private static object Describe(InspectionList list) => new
    {
        items = list.Items.Items.Select(item => new
        {
            item.Id,
            item.Title,
            // The days it covers, as two ISO dates.
            from = item.Days.From,
            until = item.Days.Until,
            item.RecordedBy,
            item.RecordedAt,
            // Who the save that wrote the row ran as: the kind of actor and, for a seat, its id.
            changedBy = new { kind = item.ChangedBy.Kind, seatId = item.ChangedBy.Seat },
        }),
        list.CanRecord,
        next = list.Items is { HasNextPage: true, Last: { } last } ? list.Items.CreateCursor(last) : null,
    };

    /// <summary>An inspection to record.</summary>
    /// <param name="Title">What was found.</param>
    /// <param name="From">The first day it covers.</param>
    /// <param name="Until">The last day it covers.</param>
    public sealed record InspectionToRecord(string? Title, DateOnly? From = null, DateOnly? Until = null);
}
