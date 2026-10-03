using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Authorization;
using DDDToolkit.Supporting.Tenancy;
using Examples.Tenancy.Projects.Application.ProjectRoles;
using HotChocolate.Resolvers;

namespace Examples.Tenancy.Projects.Api.ProjectRoles.GraphQL;

/// <summary>
/// Answers the rule on a field of a project role: whether the caller holds the key the field names for the whole
/// tenant, which the query that read the role answered already.
/// </summary>
/// <remarks>
/// The query leaves a role's keys out for a caller who does not hold <see cref="ProjectRoleListing.KeysKey"/> for
/// the whole tenant, so a role read without them is the answer, and nothing is read again. The refusal is the one a
/// command that needs the key gives, so a client reads one code whether a request or a field was refused.
/// </remarks>
internal sealed class ProjectRoleFieldKeys : IFieldKeys<ProjectRoleListing>
{
    /// <inheritdoc />
    public ValueTask<RefusalException?> RefusedAsync(ProjectRoleListing parent, string key, IResolverContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parent);

        return ValueTask.FromResult(
            string.Equals(key, ProjectRoleListing.KeysKey, StringComparison.Ordinal) && parent.Keys is not null
                ? null
                : TenancyRefusals.Of(TenancyRefusals.NotPermitted, ("Key", key)));
    }
}
