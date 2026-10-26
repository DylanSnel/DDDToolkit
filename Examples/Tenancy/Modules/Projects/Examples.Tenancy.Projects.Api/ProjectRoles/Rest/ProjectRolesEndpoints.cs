using System.Text.Json.Serialization;
using Examples.Tenancy.Projects.Application.ProjectRoles;
using Examples.Tenancy.Projects.Application.ProjectRoles.Commands;
using Examples.Tenancy.Projects.Application.ProjectRoles.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Projects.Api.ProjectRoles.Rest;

/// <summary>
/// The tenant's project roles over HTTP: listing them, making one, renaming one, setting its keys and archiving it.
/// Each route sends one command or query of the application project's <c>ProjectRoles</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="ProjectsModule"/> says what all of them keep to. Internal: the module's entry maps them into the
/// group the host hands it.
/// </remarks>
internal static class ProjectRolesEndpoints
{
    /// <summary>
    /// Maps <c>GET /project-roles</c> and <c>POST /project-roles</c>, <c>PUT /project-roles/{id}</c> to rename one,
    /// <c>PUT /project-roles/{id}/keys</c> and <c>POST /project-roles/{id}/archive</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapProjectRolesEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Every project role of the tenant, archived ones too, for anybody who works in it: a crew's roles are
        // shown by these names. With its keys for whoever manages the tenant's roles.
        group.MapGet("/project-roles", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok((await sender.Send(new TenantProjectRoles(), cancellationToken)).Select(Describe)));

        group.MapPost("/project-roles", async (RoleToMake body, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new MakeProjectRole(body.Name ?? string.Empty, body.Description, body.Keys ?? []), cancellationToken);
            return Results.Created((string?)null, new { id });
        });

        // The name and what the role is for, both as they are to be: a description left out is cleared.
        group.MapPut("/project-roles/{id}", async (ProjectRoleId id, RoleToRename body, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new RenameProjectRole(id, body.Name ?? string.Empty, body.Description), cancellationToken);
            return Results.NoContent();
        });

        // The role's keys become exactly the list: an empty one takes every key off it. So the list must be there,
        // since a body that misspelt or left out the field would otherwise empty the role without a word.
        group.MapPut("/project-roles/{id}/keys", async (ProjectRoleId id, KeysToSet body, ISender sender, CancellationToken cancellationToken) =>
        {
            var keys = body.Keys ?? throw new BadHttpRequestException("The body's \"keys\" is null: give the role's keys as a list, an empty one to take them all off.");
            await sender.Send(new SetProjectRoleKeys(id, keys), cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/project-roles/{id}/archive", async (ProjectRoleId id, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new ArchiveProjectRole(id), cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>
    /// A project role as every answer of this project writes it. <c>keys</c> is a list for whoever manages the
    /// tenant's roles, and <c>null</c> for anybody else: the query left them out.
    /// </summary>
    internal static object Describe(ProjectRoleListing role) => new
    {
        role.Id,
        role.Name,
        role.Description,
        role.MadeFrom,
        role.Status,
        role.Keys,
    };

    // The bodies.

    /// <summary>A project role to make: its name, what it is for, and the keys it gives.</summary>
    public sealed record RoleToMake(string? Name, string? Description, IReadOnlyList<string>? Keys);

    /// <summary>A project role's name and description, as they are to be.</summary>
    public sealed record RoleToRename(string? Name, string? Description);

    /// <summary>Every key a project role is to give.</summary>
    public sealed record KeysToSet([property: JsonRequired] IReadOnlyList<string>? Keys);
}
