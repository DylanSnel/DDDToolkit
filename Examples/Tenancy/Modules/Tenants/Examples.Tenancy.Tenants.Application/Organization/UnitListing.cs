using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;

namespace Examples.Tenancy.Tenants.Application.Organization;

/// <summary>
/// A unit as this application lists it: what the Tenancy package says of it, with what kind of unit it is.
/// </summary>
/// <remarks>
/// The kind is the application's own field on its unit class, and the package's <c>UnitSummary</c> carries nothing
/// the application added. So the queries that list units ask the directory with a view, <see cref="Of"/>: the
/// directory decides which units the caller reads, and hands the view each one's summary next to the unit itself,
/// which it read anyway. Every way a unit is answered, a route, a GraphQL field or the answer of a mutation, carries
/// both, with no read more.
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
    int Depth)
{
    /// <summary>The unit as the directory answered it, with the kind its own class keeps.</summary>
    /// <param name="unit">What the package's directory says of the unit.</param>
    /// <param name="own">The unit itself, this application's class, as the directory read it.</param>
    internal static UnitListing Of(SampleTenancy.UnitSummary unit, OrganizationUnit own)
        => new(unit.Id, unit.ParentId, unit.Name, own.Kind, unit.Status, unit.Path, unit.Depth);
}
