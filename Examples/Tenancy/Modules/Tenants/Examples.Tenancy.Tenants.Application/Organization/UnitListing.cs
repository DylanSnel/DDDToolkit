using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;

namespace Examples.Tenancy.Tenants.Application.Organization;

/// <summary>
/// A unit as this application lists it: its own fields, with what kind of unit it is, and where it sits in the tree.
/// </summary>
/// <remarks>
/// The kind is the application's own field on its unit class. The package's directory answers that class whole, with
/// the unit's path and depth beside it, which are not on the unit; so the queries that list units select this from
/// what the directory read, with no read more. Every way a unit is answered, a route, a GraphQL field or the answer
/// of a mutation, carries the kind and the path.
/// </remarks>
/// <param name="Id">The unit.</param>
/// <param name="ParentId">The unit it hangs under, or <see langword="null"/> for the root.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind">What kind of unit it is, or <see langword="null"/> when whoever added it did not say.</param>
/// <param name="Status">Whether it is in use.</param>
/// <param name="Path">Its path from the root, the names joined with <c>" / "</c>.</param>
/// <param name="Depth">How deep it is, the root being 1.</param>
public sealed record UnitListing(
    OrganizationUnitId Id,
    OrganizationUnitId? ParentId,
    string Name,
    UnitKind? Kind,
    UnitStatus Status,
    string Path,
    int Depth);
