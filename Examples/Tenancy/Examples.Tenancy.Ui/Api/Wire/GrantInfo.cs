namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>A role held at a unit for a period, and whether that period applies now.</summary>
public sealed record GrantInfo(Guid RoleId, string Role, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool AppliesNow);
