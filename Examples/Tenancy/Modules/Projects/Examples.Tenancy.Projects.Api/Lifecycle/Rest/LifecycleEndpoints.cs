using System.Text.Json.Serialization;
using Examples.Tenancy.Projects.Api.Rest;
using Examples.Tenancy.Projects.Application.Lifecycle.Commands;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Projects.Api.Lifecycle.Rest;

/// <summary>
/// The life of a project over HTTP: opening one, renaming it, planning it, moving it to another unit, closing it
/// and reopening it. Each route sends one command of the application project's <c>Lifecycle</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="ProjectsModule"/> says what all of them keep to. Internal: the module's entry maps them into the
/// group the host hands it.
/// </remarks>
internal static class LifecycleEndpoints
{
    /// <summary>
    /// Maps <c>POST /projects</c> to open a project, and <c>PUT /projects/{id}/name</c>,
    /// <c>PUT /projects/{id}/planned-range</c>, <c>PUT /projects/{id}/unit</c>, <c>POST /projects/{id}/close</c>
    /// and <c>POST /projects/{id}/reopen</c> to rename, plan, move, close and reopen one.
    /// </summary>
    public static IEndpointRouteBuilder MapLifecycleEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // A missing number or name is passed on empty, so the project refuses it by its code rather than by a
        // null reference. The owner is the caller unless the body names somebody else. The planned range is two
        // ISO dates, both or neither: one alone is a range that does not hold, which the project refuses.
        group.MapPost("/projects", async (ProjectToOpen body, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(
                new OpenProject(body.Number ?? string.Empty, body.Name ?? string.Empty, body.UnitId, body.OwnerSeat, Planned: DateRange.FromOptional(body.PlannedFrom, body.PlannedUntil)),
                cancellationToken);
            return Results.Created($"/projects/{id.Value}", new { id });
        });

        // Each change below takes the version its caller read, as If-Match, and is refused with 409
        // concurrency-conflict when the project has changed since. Without the header the last change wins.
        group.MapPut("/projects/{id}/name", async (ProjectId id, NewName body, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new ChangeProjectName(id, body.Name ?? string.Empty, ProjectVersions.Expected(request)), cancellationToken);
            return Results.NoContent();
        });

        // Two ISO dates plan the project; both left out, or null, take its planned range away.
        group.MapPut("/projects/{id}/planned-range", async (ProjectId id, NewPlannedRange body, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new PlanProject(id, DateRange.FromOptional(body.From, body.Until), ProjectVersions.Expected(request)), cancellationToken);
            return Results.NoContent();
        });

        group.MapPut("/projects/{id}/unit", async (ProjectId id, NewUnit body, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new MoveProjectToUnit(id, body.UnitId, ProjectVersions.Expected(request)), cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/projects/{id}/close", async (ProjectId id, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new CloseProject(id, ProjectVersions.Expected(request)), cancellationToken);
            return Results.NoContent();
        });

        // Whoever may close a project may reopen it: the same key.
        group.MapPost("/projects/{id}/reopen", async (ProjectId id, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new ReopenProject(id, ProjectVersions.Expected(request)), cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    // The bodies. An id a command cannot do without is required: left out, it would bind as an empty id and come
    // back as a refusal about a unit nobody named, where a 400 invalid-request says what is wrong.

    public sealed record ProjectToOpen(
        string? Number,
        string? Name,
        [property: JsonRequired] OrganizationUnitId UnitId,
        SeatId? OwnerSeat,
        DateOnly? PlannedFrom = null,
        DateOnly? PlannedUntil = null);

    public sealed record NewName(string? Name);

    public sealed record NewPlannedRange(DateOnly? From, DateOnly? Until);

    public sealed record NewUnit([property: JsonRequired] OrganizationUnitId UnitId);
}
