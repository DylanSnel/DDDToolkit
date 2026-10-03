using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Access.Queries;

/// <summary>
/// Which of some keys the caller holds for the whole tenant, at its root: what a client fills its navigation
/// from, such as whether to show "open a project" at all.
/// </summary>
/// <remarks>
/// It asks about the caller only, so it requires nothing but a caller that works in a tenant, and refuses nobody
/// for a key: holding none is the answer. A key held at a unit below the root is not in it: where a key is held
/// is another question, and a key on a project another one still (<see cref="KeysOnProjects"/>).
/// </remarks>
/// <param name="Keys">Permission keys, as the request names them.</param>
public sealed record KeysHeldAtRoot(IReadOnlyCollection<string> Keys) : IQuery<IReadOnlyList<string>>, IProjectsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new TenancyRequirement.InTenant();

    /// <summary>
    /// <paramref name="keys"/>, each once and without the retired ones, which are held nowhere; refused when the
    /// catalogue does not know one. The questions treat an unknown key as a mistake in the code that asks, and
    /// here it is only a wrong request.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.unknown-permission</c>, with the <c>Keys</c> that are unknown.</exception>
    internal static IReadOnlyList<string> Known(IReadOnlyCollection<string> keys, TenancyCatalogue catalogue)
    {
        var asked = keys.Distinct(StringComparer.Ordinal).ToList();
        var unknown = asked.Where(key => !catalogue.Knows(key)).ToList();

        return unknown.Count > 0
            ? throw TenancyRefusals.Of(TenancyRefusals.UnknownPermission, ("Keys", string.Join(", ", unknown)))
            : [.. asked.Where(catalogue.IsLive)];
    }
}

/// <summary>Answers <see cref="KeysHeldAtRoot"/> in one statement, on a reading of this query's own.</summary>
/// <param name="reads">Where Tenancy's rows are read: a context per query.</param>
/// <param name="access">Which keys the caller holds where.</param>
/// <param name="catalogue">The permission keys the application knows, for keys that come from outside.</param>
public sealed class KeysHeldAtRootHandler(IProjectReads reads, ProjectAccess access, TenancyCatalogue catalogue) : IQueryHandler<KeysHeldAtRoot, IReadOnlyList<string>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.unknown-permission</c>, with the <c>Keys</c> the catalogue does not know.</exception>
    public async ValueTask<IReadOnlyList<string>> Handle(KeysHeldAtRoot query, CancellationToken cancellationToken)
    {
        var keys = KeysHeldAtRoot.Known(query.Keys, catalogue);

        await using var reading = reads.Open();
        return await access.HeldTenantWideAsync(reading, keys, cancellationToken);
    }
}
