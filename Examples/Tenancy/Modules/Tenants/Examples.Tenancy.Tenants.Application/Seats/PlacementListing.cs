namespace Examples.Tenancy.Tenants.Application.Seats;

/// <summary>One of a seat's placements as this application shows it: the unit by its path, and the roles held there.</summary>
/// <param name="Unit">The unit, with its path from the root.</param>
/// <param name="IsPrimary">Whether it is the seat's primary placement.</param>
/// <param name="Grants">The roles held there, by name.</param>
public sealed record PlacementListing(TenancyUseCases.UnitRef Unit, bool IsPrimary, IReadOnlyList<GrantListing> Grants);
