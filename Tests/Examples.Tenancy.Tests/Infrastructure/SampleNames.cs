using System.Net.Http.Json;
using System.Text.Json;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// Reading names the way a client does. A project names its unit, its crew and their project roles by id; a client
/// asks Tenancy's directory what the unit and the seats are called, and the Projects module's list of project roles
/// what the roles are, as the same caller, and so do the scenarios, which say what they expect in names.
/// </summary>
public static class SampleNames
{
    /// <summary>The names the seats with these ids are shown by, from <c>POST /tenancy/directory/seats</c>.</summary>
    public static Task<IReadOnlyDictionary<Guid, string>> SeatNamesAsync(this HttpClient client, IEnumerable<Guid> ids)
        => AskAsync(client, "/tenancy/directory/seats", ids, "displayName");

    /// <summary>The names of the roles with these ids, from <c>POST /tenancy/directory/roles</c>.</summary>
    public static Task<IReadOnlyDictionary<Guid, string>> RoleNamesAsync(this HttpClient client, IEnumerable<Guid> ids)
        => AskAsync(client, "/tenancy/directory/roles", ids, "name");

    /// <summary>The names of the tenant's project roles, every one of them, from <c>GET /project-roles</c>.</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ProjectRoleNamesAsync(this HttpClient client)
        => (await client.ProjectRolesAsync()).ToDictionary(row => row.GetProperty("id").GetGuid(), row => row.GetProperty("name").GetString()!);

    /// <summary>The paths, from the root, of the units with these ids, from <c>POST /tenancy/directory/units</c>.</summary>
    public static Task<IReadOnlyDictionary<Guid, string>> UnitPathsAsync(this HttpClient client, IEnumerable<Guid> ids)
        => AskAsync(client, "/tenancy/directory/units", ids, "path");

    /// <summary>
    /// The names behind the ids of <paramref name="project"/>, as <c>GET /projects</c> or
    /// <c>GET /projects/{id}</c> answered it: each id with what the directory and the project roles answer the
    /// same client it is called. The crew comes the owner first, then by name, and each member's roles by name.
    /// </summary>
    public static async Task<NamedProject> WithNamesAsync(this HttpClient client, JsonElement project)
    {
        var crew = project.GetProperty("crew").EnumerateArray().ToList();
        var mine = project.GetProperty("myRoleIds").EnumerateArray().Select(role => role.GetGuid()).ToList();

        var seats = await client.SeatNamesAsync(crew.Select(member => member.GetProperty("seatId").GetGuid()));
        var roles = await client.ProjectRoleNamesAsync();
        var unit = project.GetProperty("unitId").GetGuid();
        var units = await client.UnitPathsAsync([unit]);

        return new NamedProject(
            project.GetProperty("name").GetString()!,
            units[unit],
            [.. mine.Select(role => roles[role]).Order(StringComparer.OrdinalIgnoreCase)],
            project.Text("via"),
            [.. crew
                .Select(member => new NamedCrewMember(
                    seats[member.GetProperty("seatId").GetGuid()],
                    [.. RolesOf(member)
                        .Select(held => new NamedCrewRole(
                            roles[held.GetProperty("roleId").GetGuid()],
                            held.GetProperty("endsAt") is { ValueKind: JsonValueKind.String } ends ? ends.GetDateTimeOffset() : null,
                            held.GetProperty("appliesNow").GetBoolean()))
                        .OrderBy(held => held.Name, StringComparer.OrdinalIgnoreCase)],
                    member.GetProperty("isOwner").GetBoolean(),
                    member.GetProperty("appliesNow").GetBoolean(),
                    RolesShown: member.GetProperty("roles").ValueKind == JsonValueKind.Array))
                .OrderByDescending(member => member.IsOwner)
                .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// The roles a crew member holds, as the API answered them: to whoever manages the crew. Anybody else is
    /// answered <c>null</c> for them, which reads as none here and is told apart by
    /// <see cref="NamedCrewMember.RolesShown"/>.
    /// </summary>
    private static IEnumerable<JsonElement> RolesOf(JsonElement member)
        => member.GetProperty("roles") is { ValueKind: JsonValueKind.Array } roles ? roles.EnumerateArray() : [];

    private static async Task<IReadOnlyDictionary<Guid, string>> AskAsync(HttpClient client, string route, IEnumerable<Guid> ids, string named)
    {
        var asked = ids.Distinct().ToArray();
        if (asked.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        using var response = await client.PostAsJsonAsync(route, new { ids = asked }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var answered = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return answered.EnumerateArray().ToDictionary(row => row.GetProperty("id").GetGuid(), row => row.GetProperty(named).GetString()!);
    }
}

/// <summary>A project with the names a screen shows for its ids.</summary>
/// <param name="Name">The project's own name.</param>
/// <param name="UnitPath">The path of its unit, from the root.</param>
/// <param name="MyRoles">The names of the project roles the caller holds on it now, by name; empty for none.</param>
/// <param name="Via">How the caller sees it.</param>
/// <param name="Crew">Its crew: the owner first, then by name.</param>
public sealed record NamedProject(string Name, string UnitPath, IReadOnlyList<string> MyRoles, string? Via, IReadOnlyList<NamedCrewMember> Crew)
{
    /// <summary>
    /// The caller's project roles on it, as one text: the one name, several joined with a comma, or
    /// <see langword="null"/> for none. What most scenarios say they expect.
    /// </summary>
    public string? MyRole => MyRoles.Count == 0 ? null : string.Join(", ", MyRoles);

    /// <summary>The crew member shown by <paramref name="name"/>.</summary>
    public NamedCrewMember Member(string name) => Crew.Single(member => member.Name == name);
}

/// <summary>A crew member with the names a screen shows for its seat and its roles.</summary>
/// <param name="Name">The name its seat is shown by.</param>
/// <param name="Roles">The project roles it holds on the crew, by name, each with whether it counts now.</param>
/// <param name="IsOwner">Whether it owns the project.</param>
/// <param name="AppliesNow">Whether its membership counts now.</param>
/// <param name="RolesShown">Whether the API answered its roles: it does to a caller who manages the crew, and to nobody else.</param>
public sealed record NamedCrewMember(string Name, IReadOnlyList<NamedCrewRole> Roles, bool IsOwner, bool AppliesNow, bool RolesShown = true)
{
    /// <summary>
    /// Every role it holds on the crew, ended ones included, as one text: the one name, several joined with a
    /// comma, or <see langword="null"/> for none.
    /// </summary>
    public string? Role => Roles.Count == 0 ? null : string.Join(", ", Roles.Select(held => held.Name));

    /// <summary>Its grant of the role called <paramref name="name"/>.</summary>
    public NamedCrewRole Holding(string name) => Roles.Single(held => held.Name == name);
}

/// <summary>A project role a crew member holds, by the name a screen shows for it.</summary>
/// <param name="Name">The role's name.</param>
/// <param name="EndsAt">When it stops counting, or <see langword="null"/> for no end.</param>
/// <param name="AppliesNow">Whether it counts now: its own period applies, and its membership's.</param>
public sealed record NamedCrewRole(string Name, DateTimeOffset? EndsAt, bool AppliesNow);
