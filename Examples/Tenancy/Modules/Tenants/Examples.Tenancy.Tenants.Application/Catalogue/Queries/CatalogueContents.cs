using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Catalogue.Queries;

/// <summary>
/// Everything the application's catalogue lists: every permission key, every role pack and every kind of unit,
/// for the screens that make roles and add units.
/// </summary>
/// <remarks>
/// Every tenant has the same catalogue, and it holds nothing of any of them, so whoever works in a tenant may
/// read it: no key is asked for. A caller in no tenant has no screen to show it on, and is refused as everywhere.
/// </remarks>
public sealed record CatalogueContents : IQuery<CatalogueOverview>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.InTenant();
}

/// <summary>The catalogue as a client reads it.</summary>
/// <param name="Permissions">Every permission key, retired ones included, in the catalogue's order.</param>
/// <param name="Packs">Every role pack a tenant's roles are copied from.</param>
/// <param name="UnitKinds">Every kind a unit of the organization can be.</param>
public sealed record CatalogueOverview(
    IReadOnlyList<PermissionOverview> Permissions,
    IReadOnlyList<PackOverview> Packs,
    IReadOnlyList<UnitKindOverview> UnitKinds);

/// <summary>A permission key.</summary>
/// <param name="Key">The key, such as <c>projects.edit</c>.</param>
/// <param name="Module">The module that declares it.</param>
/// <param name="Description">What it lets a seat do.</param>
/// <param name="Implies">The keys it brings with it; empty when none.</param>
/// <param name="Retired">Whether it is retired: known, and holding nowhere.</param>
/// <param name="ManagesAccess">Whether it manages access, so a role that holds it is given only by someone who holds it too.</param>
public sealed record PermissionOverview(
    string Key,
    string Module,
    string Description,
    IReadOnlyList<string> Implies,
    bool Retired,
    bool ManagesAccess);

/// <summary>A role pack.</summary>
/// <param name="Key">The pack's key.</param>
/// <param name="Keys">The keys a role copied from it holds.</param>
/// <param name="Name">What a role copied from it is called.</param>
/// <param name="Description">What the role is for.</param>
/// <param name="Shape">The shape of tenant it is for, or <see langword="null"/> for every shape.</param>
/// <param name="Administers">Whether it is the administrators' pack.</param>
public sealed record PackOverview(
    string Key,
    IReadOnlyList<string> Keys,
    string Name,
    string Description,
    TenantShape? Shape,
    bool Administers);

/// <summary>A kind of unit.</summary>
/// <param name="Key">The kind's key, such as <c>region</c>.</param>
/// <param name="Name">What it is called on screen.</param>
public sealed record UnitKindOverview(string Key, string Name);

/// <summary>
/// Answers <see cref="CatalogueContents"/> from the catalogue, which is built once when the host starts: there
/// is nothing to read from storage.
/// </summary>
/// <param name="catalogue">The application's catalogue.</param>
public sealed class CatalogueContentsHandler(TenancyCatalogue catalogue) : IQueryHandler<CatalogueContents, CatalogueOverview>
{
    /// <inheritdoc />
    public ValueTask<CatalogueOverview> Handle(CatalogueContents query, CancellationToken cancellationToken)
        => ValueTask.FromResult(new CatalogueOverview(
            [.. catalogue.Permissions.Select(permission => new PermissionOverview(
                permission.Key,
                permission.Module,
                permission.Description,
                permission.Implies ?? [],
                permission.Retired,
                permission.ManagesAccess))],
            [.. catalogue.Packs.Select(pack => new PackOverview(pack.Key, pack.Keys, pack.Name, pack.Description, pack.Shape, pack.Administers))],
            [.. catalogue.UnitKinds.Select(kind => new UnitKindOverview(kind.Key, kind.Name))]));
}
