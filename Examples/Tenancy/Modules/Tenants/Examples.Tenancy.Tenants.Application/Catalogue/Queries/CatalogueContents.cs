using DDDToolkit.Localization;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Catalogue.Queries;

/// <summary>
/// Everything the application's catalogue lists: every permission and every starter role, for the screens that
/// make roles.
/// </summary>
/// <remarks>
/// Every tenant has the same catalogue, and it holds nothing of any of them, so whoever works in a tenant may
/// read it: no permission is asked for. A caller in no tenant has no screen to show it on, and is refused as everywhere.
/// </remarks>
[InTenant]
public sealed record CatalogueContents : IQuery<CatalogueOverview>, ITenantsRequest;

/// <summary>The catalogue as a client reads it.</summary>
/// <param name="Permissions">Every permission, retired ones included, in the catalogue's order.</param>
/// <param name="StarterRoles">Every starter role a tenant's roles are copied from.</param>
public sealed record CatalogueOverview(
    IReadOnlyList<PermissionOverview> Permissions,
    IReadOnlyList<StarterRoleOverview> StarterRoles);

/// <summary>A permission.</summary>
/// <param name="Name">The permission, such as <c>projects.edit</c>.</param>
/// <param name="Module">The module that declares it.</param>
/// <param name="Description">What it lets a seat do, in the reader's language where a module translated it.</param>
/// <param name="Implies">The permissions it brings with it; empty when none.</param>
/// <param name="Retired">Whether it is retired: known, and holding nowhere.</param>
/// <param name="ManagesAccess">Whether it manages access, so a role that holds it is given only by someone who holds it too.</param>
public sealed record PermissionOverview(
    string Name,
    string Module,
    string Description,
    IReadOnlyList<string> Implies,
    bool Retired,
    bool ManagesAccess);

/// <summary>A starter role.</summary>
/// <param name="Key">The starter role's key.</param>
/// <param name="Permissions">The permissions a role copied from it holds.</param>
/// <param name="Name">What a role copied from it is called.</param>
/// <param name="Description">What the role is for.</param>
/// <param name="SeededFor">
/// The one shape of tenant that is given a copy, or <see langword="null"/> for every shape. It says which tenants
/// get a role made from it, not a shape of the role.
/// </param>
/// <param name="Administers">Whether it is the administrators' starter role.</param>
public sealed record StarterRoleOverview(
    string Key,
    IReadOnlyList<string> Permissions,
    string Name,
    string Description,
    TenantShape? SeededFor,
    bool Administers);

/// <summary>
/// Answers <see cref="CatalogueContents"/> from the catalogue, which is built once when the host starts: there
/// is nothing to read from storage. A permission's description is the English its <c>[Permissions]</c> class gives
/// it, or what a module's resource file holds under the permission's name in the reader's language.
/// </summary>
/// <param name="catalogue">The application's catalogue.</param>
/// <param name="texts">The texts the modules registered, refusals' and permissions' alike.</param>
public sealed class CatalogueContentsHandler(TenancyCatalogue catalogue, IFailureLocalizer texts) : IQueryHandler<CatalogueContents, CatalogueOverview>
{
    /// <inheritdoc />
    public ValueTask<CatalogueOverview> Handle(CatalogueContents query, CancellationToken cancellationToken)
        => ValueTask.FromResult(new CatalogueOverview(
            [.. catalogue.Permissions.Select(permission => new PermissionOverview(
                permission.Name,
                permission.Module,
                texts.Text(permission.Name, permission.Description),
                permission.Implies ?? [],
                permission.Retired,
                permission.ManagesAccess))],
            [.. catalogue.StarterRoles.Select(starterRole => new StarterRoleOverview(starterRole.Key, starterRole.Permissions, starterRole.Name, starterRole.Description, starterRole.SeededFor, starterRole.Administers))]));
}
