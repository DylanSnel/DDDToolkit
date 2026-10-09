using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>Reading projects, and changing their crews, the way a client does: over HTTP, with the JSON the API answers.</summary>
public static class SampleProjects
{
    /// <summary>
    /// The projects the client's caller may see, from <c>GET /projects</c>: its first page, which holds every
    /// project of the demonstration, so the page must be the last one.
    /// </summary>
    public static async Task<IReadOnlyList<JsonElement>> VisibleProjectsAsync(this HttpClient client)
    {
        var page = await client.ProjectPageAsync();
        page.GetProperty("next").ValueKind.Should().Be(JsonValueKind.Null, "the demonstration's projects fit on one page");
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    /// <summary>One page of the projects the client's caller may see, from <c>GET /projects</c> with <paramref name="query"/>, such as <c>?size=2</c>.</summary>
    public static Task<JsonElement> ProjectPageAsync(this HttpClient client, string query = "")
        => client.GetFromJsonAsync<JsonElement>("/projects" + query, TestContext.Current.CancellationToken);

    /// <summary>
    /// An enum value as the API writes and reads it: by name, in lower snake case, such as <c>archived</c> or
    /// <c>not_permitted</c>.
    /// </summary>
    public static string OnTheWire(this Enum value) => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    /// <summary>One project, with what the client's caller may do to it, from <c>GET /projects/{id}</c>.</summary>
    public static Task<JsonElement> ProjectDetailAsync(this HttpClient client, DemoProject project)
        => client.GetFromJsonAsync<JsonElement>($"/projects/{project.Id.Value}", TestContext.Current.CancellationToken);

    /// <summary>The crew of one project, on its own, from <c>GET /projects/{id}/crew</c>.</summary>
    public static Task<JsonElement> CrewAsync(this HttpClient client, DemoProject project)
        => client.GetFromJsonAsync<JsonElement>($"/projects/{project.Id.Value}/crew", TestContext.Current.CancellationToken);

    /// <summary>
    /// Gives the seat a project role on the project's crew, until <paramref name="until"/> or for good:
    /// <c>POST /projects/{id}/crew/{seatId}/roles</c>. The response is the caller's to read and dispose.
    /// </summary>
    public static Task<HttpResponseMessage> GiveCrewRoleAsync(this HttpClient client, DemoProject project, SeatId seat, ProjectRoleId role, DateTimeOffset? until = null)
        => client.GiveCrewRoleAsync(project, seat, role.Value, until);

    /// <summary>
    /// Gives the seat the role with id <paramref name="role"/> on the project's crew, whatever kind of role the id
    /// is of: <c>POST /projects/{id}/crew/{seatId}/roles</c>. The response is the caller's to read and dispose.
    /// </summary>
    public static Task<HttpResponseMessage> GiveCrewRoleAsync(this HttpClient client, DemoProject project, SeatId seat, Guid role, DateTimeOffset? until = null)
        => client.PostAsJsonAsync(
            $"/projects/{project.Id.Value}/crew/{seat.Value}/roles",
            new { roleId = role, until },
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Takes a project role from the seat on the project's crew:
    /// <c>DELETE /projects/{id}/crew/{seatId}/roles/{roleId}</c>. The response is the caller's to read and dispose.
    /// </summary>
    public static Task<HttpResponseMessage> TakeCrewRoleAsync(this HttpClient client, DemoProject project, SeatId seat, ProjectRoleId role)
        => client.DeleteAsync($"/projects/{project.Id.Value}/crew/{seat.Value}/roles/{role.Value}", TestContext.Current.CancellationToken);

    /// <summary>The tenant's project roles, from <c>GET /project-roles</c>.</summary>
    public static async Task<IReadOnlyList<JsonElement>> ProjectRolesAsync(this HttpClient client)
        => [.. (await client.GetFromJsonAsync<JsonElement>("/project-roles", TestContext.Current.CancellationToken)).EnumerateArray()];

    /// <summary>The names of <paramref name="projects"/>.</summary>
    public static IEnumerable<string> Names(this IEnumerable<JsonElement> projects)
        => projects.Select(project => project.GetProperty("name").GetString()!);

    /// <summary>The project named <paramref name="name"/> among <paramref name="projects"/>.</summary>
    public static JsonElement Named(this IEnumerable<JsonElement> projects, string name)
        => projects.Single(project => project.GetProperty("name").GetString() == name);

    /// <summary>A string property, or <see langword="null"/> when it is JSON null.</summary>
    public static string? Text(this JsonElement element, string property)
        => element.GetProperty(property) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
