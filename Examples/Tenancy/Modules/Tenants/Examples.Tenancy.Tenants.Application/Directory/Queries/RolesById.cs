using Examples.Tenancy.Tenants.Application.Access;
using Examples.Tenancy.Tenants.Application.Roles;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Directory.Queries;

/// <summary>
/// What the roles with these ids are called, the active ones first, each by name, with whether it manages access.
/// What a screen asks once another module answered it role ids.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant, and the package's directory answers such a caller about any role
/// of that tenant. An id of another tenant, or of no role, is left out of the answer without a word; more ids than
/// one question takes are refused. A role's keys are answered only to a caller who manages the tenant's roles
/// (<see cref="RoleListing.KeysKey"/>), as in the list of them.
/// </remarks>
/// <param name="Ids">The roles asked about.</param>
public sealed record RolesById(IReadOnlyList<RoleId> Ids) : IQuery<IReadOnlyList<RoleListing>>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.InTenant();
}

/// <summary>
/// Answers <see cref="RolesById"/> from the Tenancy package's directory, and whether the caller is answered the
/// keys asked beside it.
/// </summary>
/// <param name="reads">Where Tenancy is read: the directory in a scope of this query's own, the key on a context of its own.</param>
/// <param name="answers">Tenancy's answers about the current caller.</param>
public sealed class RolesByIdHandler(ITenancyReads reads, SampleAnswers answers) : IQueryHandler<RolesById, IReadOnlyList<RoleListing>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody, or <c>tenancy.too-many-ids</c>.
    /// </exception>
    public async ValueTask<IReadOnlyList<RoleListing>> Handle(RolesById query, CancellationToken cancellationToken)
    {
        // The directory first: it refuses a caller who may not ask, and a question with too many ids.
        var roles = await reads.AskDirectoryAsync(directory => directory.RolesByIdAsync(query.Ids, cancellationToken));
        return roles.Count == 0
            ? []
            : RoleListing.Of(roles, withKeys: await TenantWideKey.IsHeldAsync(answers, reads, RoleListing.KeysKey, cancellationToken));
    }
}
