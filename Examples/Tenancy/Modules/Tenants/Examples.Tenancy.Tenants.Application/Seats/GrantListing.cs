namespace Examples.Tenancy.Tenants.Application.Seats;

/// <summary>A role a seat holds at a unit, for a period, as this application shows it.</summary>
/// <param name="RoleId">The role.</param>
/// <param name="Role">Its name.</param>
/// <param name="StartsAt">The first moment the grant applies.</param>
/// <param name="EndsAt">The first moment it no longer applies, or <see langword="null"/> when it has no end.</param>
/// <param name="AppliesNow">Whether it applies at the moment the overview holds for.</param>
public sealed record GrantListing(RoleId RoleId, string Role, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool AppliesNow);
