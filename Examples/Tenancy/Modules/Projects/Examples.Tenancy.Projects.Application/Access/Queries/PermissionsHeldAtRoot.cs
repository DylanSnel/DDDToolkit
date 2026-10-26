using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Access.Queries;

/// <summary>
/// Which of some permissions the caller holds for the whole tenant, at its root: what a client fills its navigation
/// from, such as whether to show "open a project" at all.
/// </summary>
/// <remarks>
/// It asks about the caller only, so it requires nothing but a caller that works in a tenant, the module's default,
/// and refuses nobody for a permission: holding none is the answer. A permission held at a unit below the root is not in it: where a
/// permission is held is another question, and a permission on a project another one still (<see cref="PermissionsOnProjects"/>).
/// </remarks>
/// <param name="Permissions">Permissions, as the request names them.</param>
public sealed record PermissionsHeldAtRoot(IReadOnlyCollection<string> Permissions) : IQuery<IReadOnlyList<string>>, IProjectsRequest
{
    /// <summary>
    /// <paramref name="permissions"/>, each once and without the retired ones, which are held nowhere; refused when the
    /// catalogue does not know one. The questions treat an unknown permission as a mistake in the code that asks, and
    /// here it is only a wrong request.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.unknown-permission</c>, with the <c>Permissions</c> that are unknown.</exception>
    internal static IReadOnlyList<string> Known(IReadOnlyCollection<string> permissions, TenancyCatalogue catalogue)
    {
        var asked = permissions.Distinct(StringComparer.Ordinal).ToList();
        var unknown = asked.Where(permission => !catalogue.Knows(permission)).ToList();

        return unknown.Count > 0
            ? throw TenancyRefusals.Refuse(TenancyRefusals.UnknownPermission, ("Permissions", string.Join(", ", unknown)))
            : [.. asked.Where(catalogue.IsLive)];
    }
}

/// <summary>Answers <see cref="PermissionsHeldAtRoot"/> in one statement, on a reading of this query's own.</summary>
/// <param name="reads">Where Tenancy's rows are read: a context per query.</param>
/// <param name="access">Which permissions the caller holds where.</param>
/// <param name="catalogue">The permissions the application knows, for permissions that come from outside.</param>
public sealed class PermissionsHeldAtRootHandler(IProjectReads reads, ProjectAccess access, TenancyCatalogue catalogue) : IQueryHandler<PermissionsHeldAtRoot, IReadOnlyList<string>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.unknown-permission</c>, with the <c>Permissions</c> the catalogue does not know.</exception>
    public async ValueTask<IReadOnlyList<string>> Handle(PermissionsHeldAtRoot query, CancellationToken cancellationToken)
    {
        var permissions = PermissionsHeldAtRoot.Known(query.Permissions, catalogue);

        await using var reading = reads.Open();
        return await access.HeldAtTenantAsync(reading, permissions, cancellationToken);
    }
}
