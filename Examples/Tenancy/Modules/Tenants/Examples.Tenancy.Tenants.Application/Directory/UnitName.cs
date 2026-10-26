namespace Examples.Tenancy.Tenants.Application.Directory;

/// <summary>
/// What a unit is called, and nothing more: what a screen shows for a unit another module answered by id, such as
/// the unit of a project.
/// </summary>
/// <remarks>
/// The package's directory answers a unit by id with its name and path alone, whatever the caller reads of the rest
/// of the organization: a crew member on a project at a unit it is not placed under, or a seat the application keeps
/// out of the organization, still reads what the unit of its project is called, and nothing else of it. The kind and
/// the status are in <see cref="Organization.UnitListing"/>, which the list of the units the caller reads answers.
/// </remarks>
/// <param name="Id">The unit.</param>
/// <param name="Name">Its name.</param>
/// <param name="Path">Its path from the root, the names joined with <c>" / "</c>.</param>
/// <param name="Depth">How deep it is, the root being 1.</param>
public sealed record UnitName(OrganizationUnitId Id, string Name, string Path, int Depth);
