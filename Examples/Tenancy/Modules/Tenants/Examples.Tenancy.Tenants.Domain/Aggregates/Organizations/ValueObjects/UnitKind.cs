namespace Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;

/// <summary>
/// What kind of unit a unit of the organization is: the application's own label, shown next to its name.
/// </summary>
/// <remarks>
/// The Tenancy package keeps no kind of unit, because nothing about access reads one: a key held at a unit reaches
/// every unit below it, whatever either is called. An application that tells its units apart says so itself, as
/// this one does with this enum on <see cref="Entities.OrganizationUnit"/>, set when the unit is made.
/// </remarks>
public enum UnitKind
{
    /// <summary>The company itself: the root of a demonstration tenant.</summary>
    Company,

    /// <summary>A region of the company.</summary>
    Region,

    /// <summary>An area within a region.</summary>
    Area,

    /// <summary>A site, where the work is done.</summary>
    Site,
}
