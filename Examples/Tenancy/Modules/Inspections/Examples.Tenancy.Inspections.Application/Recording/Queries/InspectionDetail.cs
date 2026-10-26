using Mediator;

namespace Examples.Tenancy.Inspections.Application.Recording.Queries;

/// <summary>One inspection of a project, as the project's list shows it, or nothing when the project has no such inspection.</summary>
/// <remarks>
/// For a caller who may see the project: the caller holds <see cref="RequiredKey"/> on it, which Projects' gate
/// answers before the handler runs. A project the caller may not see is not found, as everywhere. It is what
/// answers "what did I just record": whoever sent the command asks for the inspection by the id it was given.
/// </remarks>
/// <param name="Project">The project the inspection is of.</param>
/// <param name="Id">The inspection.</param>
public sealed record InspectionDetail(ProjectId Project, InspectionId Id) : IQuery<InspectionOverview?>, IInspectionsRequest
{
    /// <summary>The key the caller holds on the project.</summary>
    public const string RequiredKey = ProjectKeys.View;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new InspectionsRequirement.OnProject(RequiredKey, Project);
}

/// <summary>Answers <see cref="InspectionDetail"/>: the inspection in one statement, on a context of this query's own.</summary>
/// <param name="reads">Where inspections are read: a context per query.</param>
public sealed class InspectionDetailHandler(IInspectionReads reads) : IQueryHandler<InspectionDetail, InspectionOverview?>
{
    /// <inheritdoc />
    public async ValueTask<InspectionOverview?> Handle(InspectionDetail query, CancellationToken cancellationToken)
    {
        // The project the query names, which the gate answered for, and only an inspection of that project: an id
        // of another project's inspection finds nothing here.
        return await reads.OneAsync(query.Project, query.Id, cancellationToken);
    }
}
