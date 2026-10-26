namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// The two things about a role that the seat's rules depend on: whether the role is active, and its keys.
/// Seats and roles are separate aggregates; the use case reads the role and passes this snapshot to the seat.
/// </summary>
/// <param name="IsActive">Whether the role is active; an archived role grants nothing.</param>
/// <param name="Keys">The role's permission keys, expanded.</param>
public sealed record RoleFacts(bool IsActive, IReadOnlyCollection<string> Keys);
