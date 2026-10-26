using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Authorization;
using Examples.Tenancy.Projects.Api.Access.GraphQL;
using Examples.Tenancy.Projects.Application.Crew;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Examples.Tenancy.Projects.Api.Crew.GraphQL;

/// <summary>
/// Answers a rule on a field of a crew member: whether the caller holds the permission the field names on the project
/// the member belongs to.
/// </summary>
/// <remarks>
/// A field that carries <c>[Authorize("&lt;permission&gt;")]</c> is asked about here before it is resolved, for every
/// member of an answer side by side. They all wait on one loader, keyed by the project, so the projects of one
/// batch, as a rule a page with its crews, ask which permissions the caller holds once. The refusal is the one the
/// access check of a command gives for the same permission, so a client reads one code whether a request or a field
/// was refused.
/// <para>
/// This is a rule a client can read in the schema, on a field of something the caller already sees. It is not
/// what keeps a caller from a project: every query still declares its access and answers only what the caller
/// may see.
/// </para>
/// </remarks>
internal sealed class CrewFieldPermissions : IFieldPermissions<CrewOverview>
{
    /// <summary>Every permission a field of a crew member names: what the loader asks about, for all of them at once.</summary>
    public static IReadOnlyList<string> Permissions { get; } = [CrewOverview.RolesPermission];

    /// <inheritdoc />
    public async ValueTask<RefusalException?> RefusedAsync(CrewOverview parent, string permission, IResolverContext context, CancellationToken cancellationToken)
        => await context.DataLoader<IHeldPermissionsByProjectIdDataLoader>().LoadAsync(parent.ProjectId, cancellationToken) is { } held && held.Permissions.Contains(permission, StringComparer.Ordinal)
            ? null
            : ProjectRefusals.Refuse(ProjectRefusals.NotPermitted, ("Permission", permission));
}
