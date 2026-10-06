using Examples.Tenancy.Tenants.Application.Organization;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Directory.Queries;

/// <summary>
/// What the units with these ids are called, each by its path from the root and with its kind, whichever of them the
/// caller is placed under. What a screen asks once another module answered it unit ids.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant, and the package's directory answers such a caller about any unit
/// of that tenant. That is wider than <see cref="OrganizationUnits"/>, which lists the units a seat is placed under:
/// a crew member works on a project at a unit it is not placed under, and still reads what that unit is called. An
/// id of another tenant, or of no unit, is left out of the answer without a word; more ids than one question takes
/// are refused.
/// </remarks>
/// <param name="Ids">The units asked about.</param>
public sealed record OrganizationUnitsById(IReadOnlyList<OrganizationUnitId> Ids) : IQuery<IReadOnlyList<UnitListing>>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.InTenant();
}

/// <summary>
/// Answers <see cref="OrganizationUnitsById"/> from the Tenancy package's directory: the module's own units, each
/// selected with the kind its class keeps and the path the package put beside it.
/// </summary>
/// <param name="reads">Where Tenancy is read: the directory, in a scope of this query's own.</param>
public sealed class OrganizationUnitsByIdHandler(ITenancyReads reads) : IQueryHandler<OrganizationUnitsById, IReadOnlyList<UnitListing>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody, or <c>tenancy.too-many-ids</c>.
    /// </exception>
    public async ValueTask<IReadOnlyList<UnitListing>> Handle(OrganizationUnitsById query, CancellationToken cancellationToken)
    {
        var units = await reads.AskDirectoryAsync(directory => directory.UnitsByIdAsync(query.Ids, cancellationToken));
        return [.. units.Select(found => new UnitListing(found.Unit.Id, found.Unit.ParentId, found.Unit.Name, found.Unit.Kind, found.Unit.Status, found.Path, found.Depth))];
    }
}
