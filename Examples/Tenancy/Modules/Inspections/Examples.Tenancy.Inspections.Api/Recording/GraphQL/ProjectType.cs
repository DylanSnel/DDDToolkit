using Examples.Tenancy.Inspections.Api.GraphQL;
using Examples.Tenancy.Inspections.Application.Recording.Queries;
using GreenDonut.Data;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Pagination;

namespace Examples.Tenancy.Inspections.Api.Recording.GraphQL;

/// <summary>
/// The type <c>Project</c> in Inspections' schema: the project's key, and the one field Inspections has to add
/// to a project, its inspections. Projects owns the type; the gateway puts the two together, so a client asks a
/// project for its inspections where it asks for its name.
/// </summary>
/// <remarks>
/// The field is this module's own answer about another module's entity, which is how a relation across modules
/// is declared: no root field joins the two, and no resolver of Projects sends a query of Inspections. Like every
/// field it only loads: the loader sends <see cref="InspectionsOfProjects"/> once for the projects of a batch,
/// as a rule every project of the answer, and that query's access check asks Projects' gate once about all of
/// them.
/// <para>
/// The sizes of a page are HotChocolate's own here, ten when a client names none and fifty at most, as for the
/// list of projects this field hangs under, and smaller than the route's; the field says them itself, so they
/// are in the schema and do not move with a default somebody changes elsewhere. What a request may cost is
/// estimated from the largest page a list may hold, and HotChocolate's limits are made for its own sizes: with
/// the route's two hundred, a page of inspections asked for by its nodes and by its edges cost more than a
/// request may before a single row was read. The query itself refuses a page larger than its own cap, and one
/// asked for from both ends, whoever sends it.
/// </para>
/// </remarks>
[ObjectType<ReferencedProject>]
[EntityKey("id")]
internal static partial class ProjectType
{
    /// <summary>How many rows a page of this field holds when a client names no size: HotChocolate's own default, said out loud.</summary>
    private const int DefaultPage = 10;

    /// <summary>The most rows a page of this field holds: HotChocolate's own limit, said out loud. The schema refuses a larger one before the query is sent.</summary>
    private const int LargestPage = 50;

    static partial void Configure(IObjectTypeDescriptor<ReferencedProject> descriptor) => descriptor.Name("Project");

    /// <summary>
    /// The project's inspections, newest first, a page at a time, or nothing for a project the caller may not
    /// see: such a project says nothing about itself, not even that nothing was recorded on it.
    /// </summary>
    [UseConnection(IncludeTotalCount = true, DefaultPageSize = DefaultPage, MaxPageSize = LargestPage)]
    [Cost(InspectionsConnection.LoadedForThePage)]
    public static async Task<InspectionsConnection?> GetInspectionsAsync(
        [Parent] ReferencedProject project,
        PagingArguments paging,
        IInspectionsByProjectIdDataLoader inspections,
        CancellationToken cancellationToken)
        => await inspections.With(paging).LoadAsync(project.Id, cancellationToken) is { } page
            ? new InspectionsConnection(page, project.Id)
            : null;
}
