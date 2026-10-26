namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// A project role a crew member holds, by its id, with the period it holds it for. It counts while its own period applies
/// and the membership's does (<see cref="AppliesNow"/>).
/// </summary>
public sealed record CrewRoleInfo(Guid RoleId, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool AppliesNow);
