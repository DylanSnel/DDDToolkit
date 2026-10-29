using System.Text.Json;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// Reading a seat's overview the way a client does, from the JSON of <c>GET /me</c>. Its placements name units and
/// roles by id, and the overview's <c>units</c> and <c>roles</c> say what each id is called.
/// </summary>
public static class SeatOverviews
{
    /// <summary>The path of the unit with id <paramref name="unit"/>, from the overview's <c>units</c>.</summary>
    public static string? PathOf(this JsonElement overview, Guid unit)
        => overview.GetProperty("units").EnumerateArray().Single(found => found.GetProperty("id").GetGuid() == unit).Text("path");

    /// <summary>The name of the role with id <paramref name="role"/>, from the overview's <c>roles</c>.</summary>
    public static string? RoleNameOf(this JsonElement overview, Guid role)
        => overview.GetProperty("roles").EnumerateArray().Single(found => found.GetProperty("id").GetGuid() == role).Text("name");

    /// <summary>
    /// Whether <paramref name="grant"/>, a grant of the overview's seat, applies at the moment the overview holds for:
    /// it starts at or before <c>asOf</c>, and ends after it or not at all.
    /// </summary>
    public static bool AppliesNow(this JsonElement overview, JsonElement grant)
    {
        var asOf = overview.GetProperty("asOf").GetDateTimeOffset();
        return grant.GetProperty("startsAt").GetDateTimeOffset() <= asOf
               && (grant.GetProperty("endsAt") is not { ValueKind: JsonValueKind.String } ends || ends.GetDateTimeOffset() > asOf);
    }
}
