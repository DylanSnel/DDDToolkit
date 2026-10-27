namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>A role held at a unit, by id, for a period.</summary>
public sealed record GrantInfo(Guid RoleId, DateTimeOffset StartsAt, DateTimeOffset? EndsAt);
