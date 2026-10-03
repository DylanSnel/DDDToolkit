using System.Text.Json.Serialization;
using Examples.Tenancy.Projects.Application.Access.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Projects.Api.Access.Rest;

/// <summary>
/// What the caller holds, over HTTP: a probe for one key on one project, and the key sets a client fills its
/// navigation and its buttons from. Each route sends one query of the application project's <c>Access</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, it decides nothing and takes only the sender;
/// <see cref="ProjectsModule"/> says what all of them keep to. Internal: the module's entry maps it into the
/// group the host hands it.
/// </remarks>
internal static class AccessEndpoints
{
    /// <summary>
    /// Maps <c>GET /access/projects/{id}?key=</c>, <c>GET /access/keys?keys=</c> and
    /// <c>POST /access/projects/keys</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Whether the caller holds a key on a project, and through what. The key comes from outside, so the
        // query checks it against the catalogue first; a project the caller cannot see is not found, as
        // everywhere.
        group.MapGet("/access/projects/{id}", async (ProjectId id, string key, ISender sender, CancellationToken cancellationToken) =>
        {
            var held = await sender.Send(new KeyOnProject(id, key), cancellationToken);
            return Results.Ok(new { allowed = held.Allowed, via = held.Via });
        });

        // Which of the keys named, separated by commas, the caller holds for the whole tenant: what a client shows
        // or hides its navigation by. A key the catalogue does not know is refused; one that is not held is left out.
        group.MapGet("/access/keys", async (string? keys, ISender sender, CancellationToken cancellationToken) =>
        {
            var asked = (keys ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Results.Ok(new { heldAtRoot = await sender.Send(new KeysHeldAtRoot(asked), cancellationToken) });
        });

        // Which of the keys the caller holds on each of the projects, for a page of them at once. It reads and
        // changes nothing: it is a POST only because the ids travel in the body. A project the caller does not see,
        // or holds none of the keys on, is not in the answer.
        group.MapPost("/access/projects/keys", async (KeysAsked body, ISender sender, CancellationToken cancellationToken) =>
        {
            var sets = await sender.Send(
                new KeysOnProjects(
                    body.Projects ?? throw new BadHttpRequestException("The body's \"projects\" is null: give the projects to ask about as a list."),
                    body.Keys ?? throw new BadHttpRequestException("The body's \"keys\" is null: give the keys to ask about as a list.")),
                cancellationToken);

            return Results.Ok(new
            {
                sets.HeldAtRoot,
                onProjects = sets.OnProjects.ToDictionary(held => held.Project.Value.ToString(), held => held.Keys),
            });
        });

        return group;
    }

    /// <summary>
    /// The projects and the keys asked about. Both are required: left out, the question would read as one about
    /// nothing and be answered with nothing, where a 400 invalid-request says what is wrong.
    /// </summary>
    public sealed record KeysAsked(
        [property: JsonRequired] IReadOnlyList<ProjectId>? Projects,
        [property: JsonRequired] IReadOnlyList<string>? Keys);
}
