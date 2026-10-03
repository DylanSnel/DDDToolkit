using System.Text.Json.Serialization;
using Examples.Tenancy.Projects.Api.Rest;
using Examples.Tenancy.Projects.Application.Crew;
using Examples.Tenancy.Projects.Application.Crew.Commands;
using Examples.Tenancy.Projects.Application.Crew.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Projects.Api.Crew.Rest;

/// <summary>
/// A project's crew over HTTP: who is on it, putting a seat on it and taking one off, and giving and taking the
/// roles a member holds there. Each route sends one command or query of the application project's <c>Crew</c>
/// feature.
/// </summary>
/// <remarks>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="ProjectsModule"/> says what all of them keep to. Internal: the module's entry maps them into the
/// group the host hands it.
/// </remarks>
internal static class CrewEndpoints
{
    /// <summary>
    /// Maps <c>GET /projects/{id}/crew</c>; <c>POST /projects/{id}/crew</c> and
    /// <c>DELETE /projects/{id}/crew/{seatId}</c> to put a seat on the crew and take it off; and
    /// <c>POST /projects/{id}/crew/{seatId}/roles</c> and <c>DELETE /projects/{id}/crew/{seatId}/roles/{roleId}</c>
    /// to give and take a member's crew roles.
    /// </summary>
    public static IEndpointRouteBuilder MapCrewEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // The crew on its own, for whoever may see the project: the owner first, and for a caller who manages the
        // crew each member with its roles and their dates. The same members, in the same shape, as the project's
        // own answer carries.
        group.MapGet("/projects/{id}/crew", async (ProjectId id, ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new AllCrewMembers(id), cancellationToken)).Select(Describe)));

        // A seat goes on the crew for a period, and is on it with no role unless the body names one, which it is
        // then given for the same period. On the crew it sees the project; anything more comes with a role.
        // Like every change to a project, a change to its crew takes the version its caller read, as If-Match.
        group.MapPost("/projects/{id}/crew", async (ProjectId id, MemberToAdd body, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new AddCrewMember(id, body.SeatId, body.RoleId, body.EndsAt, ProjectVersions.Expected(request)), cancellationToken);
            return Results.NoContent();
        });

        // A project role is given by whoever manages the crew, to anyone on it, themselves included, whether or not
        // they hold the role's keys, next to the roles the member holds already and for a period of its own. On a
        // crew a role gives only what acts on the project: opening projects and naming the owner never come
        // through a crew, so the owner is named by the organization alone.
        group.MapPost("/projects/{id}/crew/{seatId}/roles", async (ProjectId id, SeatId seatId, RoleToGive body, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new GiveCrewRole(id, seatId, body.RoleId, body.EndsAt, ProjectVersions.Expected(request)), cancellationToken);
            return Results.NoContent();
        });

        // The member stays on the crew, with whatever else it holds there.
        group.MapDelete("/projects/{id}/crew/{seatId}/roles/{roleId}", async (ProjectId id, SeatId seatId, ProjectRoleId roleId, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new TakeCrewRole(id, seatId, roleId, ProjectVersions.Expected(request)), cancellationToken);
            return Results.NoContent();
        });

        // Off the crew, with every role held there.
        group.MapDelete("/projects/{id}/crew/{seatId}", async (ProjectId id, SeatId seatId, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new RemoveCrewMember(id, seatId, ProjectVersions.Expected(request)), cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>
    /// A crew member as every answer of this project writes it: in the crew's own list, and in a project's. The
    /// seat and the roles are ids; what a seat is called is Tenancy's to answer, and what a project role is called,
    /// this module's (<c>GET /project-roles</c>).
    /// </summary>
    /// <remarks>
    /// <c>roles</c> is a list for a caller who manages the crew, an empty one for a member with no role, and
    /// <c>null</c> for anybody else: the query left them out, and nothing here could put them back.
    /// </remarks>
    internal static object Describe(CrewOverview member) => new
    {
        member.SeatId,
        member.IsOwner,
        member.StartsAt,
        member.EndsAt,
        member.AppliesNow,
        roles = member.Roles?.Select(held => new { held.RoleId, held.StartsAt, held.EndsAt, held.AppliesNow }),
    };

    // The bodies. An id a command cannot do without is required: left out, it would bind as an empty id and come
    // back as a refusal about a seat or role nobody named, where a 400 invalid-request says what is wrong.

    /// <summary>
    /// A seat to put on the crew until <paramref name="EndsAt"/> (<c>until</c>) or for good, with a project role
    /// for the same period or none.
    /// </summary>
    public sealed record MemberToAdd(
        [property: JsonRequired] SeatId SeatId,
        ProjectRoleId? RoleId,
        [property: JsonPropertyName("until")] DateTimeOffset? EndsAt);

    /// <summary>A project role to give a crew member, from now until <paramref name="EndsAt"/> (<c>until</c>) or for good.</summary>
    public sealed record RoleToGive(
        [property: JsonRequired] ProjectRoleId RoleId,
        [property: JsonPropertyName("until")] DateTimeOffset? EndsAt);
}
