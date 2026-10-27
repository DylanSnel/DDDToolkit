using Mediator;

namespace Examples.Tenancy.Tenants.Application.Directory.Queries;

/// <summary>
/// What the units with these ids are called, each by its name and its path from the root, whichever of them the
/// caller is placed under. What a screen asks once another module answered it unit ids.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant, and the package's directory answers such a caller about any unit
/// of that tenant, with its name and path and nothing more. That is wider than <see cref="Organization.Queries.OrganizationUnits"/>,
/// which lists the units a seat is placed under, whole: a crew member works on a project at a unit it is not placed
/// under, and still reads what that unit is called. An id of another tenant, or of no unit, is left out of the
/// answer without a word; more ids than one question takes are refused.
/// </remarks>
/// <param name="Ids">The units asked about.</param>
[InTenant]
public sealed record OrganizationUnitsById(IReadOnlyList<OrganizationUnitId> Ids) : IQuery<IReadOnlyList<UnitName>>, ITenantsRequest;

/// <summary>
/// Answers <see cref="OrganizationUnitsById"/> from the Tenancy package's directory, which names the units asked
/// about.
/// </summary>
/// <param name="reads">Where Tenancy is read: the directory, in a scope of this query's own.</param>
public sealed class OrganizationUnitsByIdHandler(ITenancyReads reads) : IQueryHandler<OrganizationUnitsById, IReadOnlyList<UnitName>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody, or <c>tenancy.too-many-ids</c>.
    /// </exception>
    public async ValueTask<IReadOnlyList<UnitName>> Handle(OrganizationUnitsById query, CancellationToken cancellationToken)
    {
        var units = await reads.AskAsync((OrganizationManager manager) => manager.UnitsByIdAsync(query.Ids, cancellationToken));
        return [.. units.Select(unit => new UnitName(unit.Id, unit.Name, unit.Path, unit.Depth))];
    }
}
