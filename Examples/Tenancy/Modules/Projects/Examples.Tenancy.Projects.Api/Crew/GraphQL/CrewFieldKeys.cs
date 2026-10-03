using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Authorization;
using Examples.Tenancy.Projects.Api.Access.GraphQL;
using Examples.Tenancy.Projects.Application.Crew;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Examples.Tenancy.Projects.Api.Crew.GraphQL;

/// <summary>
/// Answers a rule on a field of a crew member: whether the caller holds the key the field names on the project
/// the member belongs to.
/// </summary>
/// <remarks>
/// A field that carries <c>[Authorize("&lt;key&gt;")]</c> is asked about here before it is resolved, for every
/// member of an answer side by side. They all wait on one loader, keyed by the project, so the projects of one
/// batch, as a rule a page with its crews, ask which keys the caller holds once. The refusal is the one the
/// access check of a command gives for the same key, so a client reads one code whether a request or a field
/// was refused.
/// <para>
/// This is a rule a client can read in the schema, on a field of something the caller already sees. It is not
/// what keeps a caller from a project: every query still declares its access and answers only what the caller
/// may see.
/// </para>
/// </remarks>
internal sealed class CrewFieldKeys : IFieldKeys<CrewOverview>
{
    /// <summary>Every key a field of a crew member names: what the loader asks about, for all of them at once.</summary>
    public static IReadOnlyList<string> Keys { get; } = [CrewOverview.RolesKey];

    /// <inheritdoc />
    public async ValueTask<RefusalException?> RefusedAsync(CrewOverview parent, string key, IResolverContext context, CancellationToken cancellationToken)
        => await context.DataLoader<IHeldKeysByProjectIdDataLoader>().LoadAsync(parent.ProjectId, cancellationToken) is { } held && held.Keys.Contains(key, StringComparer.Ordinal)
            ? null
            : ProjectRefusals.Of(ProjectRefusals.NotPermitted, ("Key", key));
}
