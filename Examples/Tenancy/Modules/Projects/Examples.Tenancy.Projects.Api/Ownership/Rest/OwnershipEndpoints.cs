using System.Text.Json.Serialization;
using Examples.Tenancy.Projects.Api.Rest;
using Examples.Tenancy.Projects.Application.Ownership.Commands;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Projects.Api.Ownership.Rest;

/// <summary>
/// Who owns a project, over HTTP: naming another owner. The route sends the one command of the application
/// project's <c>Ownership</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, it decides nothing and takes only the sender;
/// <see cref="ProjectsModule"/> says what all of them keep to. Internal: the module's entry maps it into the
/// group the host hands it.
/// </remarks>
internal static class OwnershipEndpoints
{
    /// <summary>Maps <c>PUT /projects/{id}/owner</c>.</summary>
    public static IEndpointRouteBuilder MapOwnershipEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Like every change to a project, naming an owner takes the version its caller read, as If-Match.
        group.MapPut("/projects/{id}/owner", async (ProjectId id, NewOwner body, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new ChangeProjectOwner(id, body.SeatId, ProjectVersions.Expected(request)), cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>
    /// The seat to name owner. Required: left out, it would bind as an empty id and come back as a refusal about a
    /// seat nobody named, where a 400 invalid-request says what is wrong.
    /// </summary>
    public sealed record NewOwner([property: JsonRequired] SeatId SeatId);
}
