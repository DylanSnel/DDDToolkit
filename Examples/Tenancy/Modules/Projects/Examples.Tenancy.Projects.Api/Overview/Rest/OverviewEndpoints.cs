using Examples.Tenancy.Projects.Api.Crew.Rest;
using Examples.Tenancy.Projects.Api.Rest;
using Examples.Tenancy.Projects.Application.Overview;
using Examples.Tenancy.Projects.Application.Overview.Queries;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects.ValueObjects;
using GreenDonut.Data;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Projects.Api.Overview.Rest;

/// <summary>
/// What the caller sees of the projects, over HTTP: the projects it may see, a page at a time, and one project
/// with its version. Both say what the caller may do to each project. Each route sends queries of the
/// application project's <c>Overview</c> feature: the projects, then their crews and what the caller may do to
/// them, each asked once for all the projects of the answer.
/// </summary>
/// <remarks>
/// An answer names a unit, a seat and a role by its id and never by a name: those are Tenancy's, and this module
/// has none to give. A client that shows them asks Tenancy's directory, <c>POST /tenancy/directory/seats</c>,
/// <c>/units</c> and <c>/roles</c>, with the ids it was answered here.
/// <para>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="ProjectsModule"/> says what all of them keep to. Internal: the module's entry maps them into the
/// group the host hands it.
/// </para>
/// </remarks>
internal static class OverviewEndpoints
{
    /// <summary>Maps <c>GET /projects</c> and <c>GET /projects/{id}</c>.</summary>
    public static IEndpointRouteBuilder MapOverviewEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // What the caller may see, by number, each with how: its crew, or its organization role at the project's
        // unit. A page at a time, through the query the GraphQL list sends: "next" is the cursor of the page's last
        // project, to send as "after" for the following page, and is null on the last. The filters narrow the
        // list, and "total" is there only when "count" asked for it.
        group.MapGet("/projects", async (
            string? after,
            int? size,
            string? state,
            OrganizationUnitId? unit,
            string? text,
            bool? count,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var page = await sender.Send(
                new VisibleProjects(
                    new PagingArguments(first: size ?? VisibleProjects.DefaultPage, after: after, includeTotalCount: count ?? false),
                    StateNamed(state),
                    unit,
                    text),
                cancellationToken);

            // A row shows its crew and what the caller may do to it: read for the whole page, once each.
            var ids = page.Items.Select(project => project.Id).ToList();
            var crews = await sender.Send(new CrewsOfProjects(ids), cancellationToken);
            var abilities = await sender.Send(new AbilitiesOnProjects(ids), cancellationToken);

            var items = page.Items.Select(project => Describe(project, crews.GetValueOrDefault(project.Id), abilities.GetValueOrDefault(project.Id)));
            var next = page is { HasNextPage: true, Last: { } last } ? page.CreateCursor(last) : null;

            return Results.Ok(page.TotalCount is { } total ? new { items, next, total } : (object)new { items, next });
        });

        // One project, with what the caller may do to it, so a screen shows each action as open or not. Its version
        // is in the body and, as a strong tag, in the ETag: a client sends it back as If-Match with a change.
        group.MapGet("/projects/{id}", async (ProjectId id, HttpResponse response, ISender sender, CancellationToken cancellationToken) =>
        {
            var project = await sender.Send(new ProjectDetail(id), cancellationToken);
            var crews = await sender.Send(new CrewsOfProjects([id]), cancellationToken);
            var abilities = await sender.Send(new AbilitiesOnProjects([id]), cancellationToken);

            ProjectVersions.WriteTo(response, project.Version);
            return Results.Ok(Describe(project, crews.GetValueOrDefault(id), abilities.GetValueOrDefault(id)));
        });

        return group;
    }

    /// <summary>
    /// The state a list is asked for, by its name as an answer spells it. A query string is not read with the host's
    /// JSON options, so the name is matched here whatever its case and with or without the underscores a host may
    /// put between words: <c>closed</c>, as the answers of this host say it, and <c>Closed</c> alike.
    /// </summary>
    /// <exception cref="BadHttpRequestException">The name is no state. The host answers it as 400 <c>invalid-request</c>.</exception>
    private static ProjectState? StateNamed(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        var asked = state.Trim().Replace("_", string.Empty, StringComparison.Ordinal);
        foreach (var known in Enum.GetValues<ProjectState>())
        {
            if (string.Equals(known.ToString(), asked, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        throw new BadHttpRequestException("The state to list is \"open\" or \"closed\".");
    }

    /// <summary>
    /// A project as both routes write it: its own row, with its crew and what the caller may do to it beside it.
    /// </summary>
    /// <remarks>
    /// The crew and the abilities were read after the project. One that went out of the caller's reach in between
    /// has neither, and is written with no crew and nothing the caller may do: what it would be answered next.
    /// </remarks>
    private static object Describe(ProjectOverview project, ProjectCrew? crew, ProjectAbilities? can) => new
    {
        project.Id,
        project.Version,
        project.Number,
        project.Name,
        project.UnitId,
        project.State,
        project.OwnerSeat,
        // The planned range as two ISO dates, both null for a project that is not planned.
        plannedFrom = project.Planned?.From,
        plannedUntil = project.Planned?.Until,
        myRoleIds = crew?.MyRoleIds ?? [],
        project.Via,
        // A crew member reads the same here as in the crew's own answer: one description, the crew feature's.
        crew = (crew?.Members ?? []).Select(CrewEndpoints.Describe),
        can = new
        {
            rename = can?.Rename ?? false,
            plan = can?.Plan ?? false,
            move = can?.Move ?? false,
            close = can?.Close ?? false,
            reopen = can?.Reopen ?? false,
            manageCrew = can?.ManageCrew ?? false,
            changeOwner = can?.ChangeOwner ?? false,
        },
        // Who changed the project last: the kind of actor and, for a seat, its id, which the directory names.
        changedBy = new { kind = project.ChangedBy.Kind, seatId = project.ChangedBy.Seat },
    };
}
