namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// The two things about a role that the seat's rules depend on: whether the role is active, and its permissions.
/// Seats and roles are separate aggregates; the manager reads the role and passes this snapshot to the seat.
/// </summary>
/// <param name="IsActive">Whether the role is active; an archived role grants nothing.</param>
/// <param name="Permissions">The role's permissions, expanded.</param>
public sealed record RoleFacts(bool IsActive, IReadOnlyCollection<string> Permissions);
