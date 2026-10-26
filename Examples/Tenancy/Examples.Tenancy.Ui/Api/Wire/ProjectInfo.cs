namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// A project as the caller sees it (<c>GET /projects</c> and <c>GET /projects/{id}</c>): how it sees it
/// (<see cref="Via"/>), the project roles it holds on it now, the crew, the days it is planned for as two dates,
/// both empty when it is not planned, what it may do to it, and who changed it last.
/// Its unit, its seats and their project roles are ids: the screen asks what they are called
/// (<see cref="DirectoryNames"/>).
/// </summary>
public sealed record ProjectInfo(
    Guid Id,
    string Number,
    string Name,
    Guid UnitId,
    string State,
    Guid OwnerSeat,
    IReadOnlyList<Guid>? MyRoleIds,
    string? Via,
    IReadOnlyList<CrewInfo> Crew,
    ProjectAbilitiesInfo? Can,
    DateOnly? PlannedFrom = null,
    DateOnly? PlannedUntil = null,
    ActorInfo? ChangedBy = null)
{
    /// <summary>Whether it is closed, which refuses every change until it is reopened.</summary>
    public bool IsClosed => string.Equals(State, "closed", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the caller sees it by being on its crew, rather than through the organization.</summary>
    public bool IsSeenViaCrew => string.Equals(Via, "crew", StringComparison.OrdinalIgnoreCase);

    /// <summary>The project roles the caller holds on it now; none for a caller who is not on the crew, or is on it with no role.</summary>
    public IReadOnlyList<Guid> MyRoles => MyRoleIds ?? [];

    /// <summary>Every project role id the project names: the caller's, and each crew member's.</summary>
    public IEnumerable<Guid> RoleIds => MyRoles.Concat(Crew.SelectMany(member => member.RolesHeld).Select(held => held.RoleId));
}
