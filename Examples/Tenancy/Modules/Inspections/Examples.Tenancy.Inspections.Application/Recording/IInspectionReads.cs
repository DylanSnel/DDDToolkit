using GreenDonut.Data;

namespace Examples.Tenancy.Inspections.Application.Recording;

/// <summary>
/// Where Inspections' queries read. The application declares it; the infrastructure project implements it and
/// registers it, scoped.
/// </summary>
/// <remarks>
/// Every read gets storage of its own, and none uses the unit of work the request's commands save through. A
/// request may send several queries side by side, as the resolvers of one GraphQL request do, and one unit of
/// work runs one query at a time: two queries on it would meet.
/// <para>
/// It says what the queries need and no more, unlike Projects' read port, which opens a reading for the
/// application to compose over. That one does so because who may see a project is asked inside the statement
/// that lists the projects, so the question and the list must meet on one context. Inspections composes nothing
/// with Tenancy: whether the caller may see a project, or record on it, is Projects' answer, asked through its
/// gate before anything is read here. So the port takes no reach and hands out nothing to compose: it answers
/// "these projects' inspections" whole, as data, and the statement that answers it stays the infrastructure
/// project's.
/// </para>
/// <para>
/// A list is a page: the port takes HotChocolate's <see cref="PagingArguments"/> and answers its
/// <see cref="Page{T}"/>, which knows whether more follows and makes the cursor of each of its rows. The module
/// has no cursor and no page of its own, and refuses a marker that is not a cursor of the list asked for
/// (<c>inspections.cursor-invalid</c>). What a page holds is declared here, next to the port
/// (<see cref="InspectionOverview"/>), so the adapter that implements it names nothing of the application but
/// this folder.
/// </para>
/// <para>
/// The inspections keep to the current caller's tenant, as the storage's tenant filter does: an inspection of
/// another tenant is simply not there.
/// </para>
/// <para>
/// Public, because the infrastructure project implements it and a handler's constructor names it. No route
/// names it: a route only sends a request.
/// </para>
/// </remarks>
public interface IInspectionReads
{
    /// <summary>
    /// A page of the inspections of <paramref name="project"/> in the caller's tenant, newest first, and those
    /// recorded at one moment by their ids. One statement, on a context this one read has to itself.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="paging">How many, and after or before which cursor.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="DDDToolkit.Exceptions.RefusalException">
    /// <c>inspections.cursor-invalid</c>: the place <paramref name="paging"/> names is not a cursor this list gave.
    /// </exception>
    Task<Page<InspectionOverview>> PageForProjectAsync(ProjectId project, PagingArguments paging, CancellationToken cancellationToken);

    /// <summary>
    /// The same page of each of <paramref name="projects"/>, read together: one statement whatever their number,
    /// and one more when <paramref name="paging"/> asks for the totals. A project without inspections is not in
    /// the answer.
    /// </summary>
    /// <param name="projects">The projects.</param>
    /// <param name="paging">How many of each project, and after or before which cursor.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="DDDToolkit.Exceptions.RefusalException">
    /// <c>inspections.cursor-invalid</c>: the place <paramref name="paging"/> names is not a cursor this list gave.
    /// </exception>
    Task<IReadOnlyDictionary<ProjectId, Page<InspectionOverview>>> PagesForProjectsAsync(IReadOnlyCollection<ProjectId> projects, PagingArguments paging, CancellationToken cancellationToken);

    /// <summary>
    /// One inspection of <paramref name="project"/> in the caller's tenant, or <see langword="null"/> when the
    /// project has no inspection with that id. One statement, on a context this one read has to itself.
    /// </summary>
    /// <param name="project">The project the inspection is of.</param>
    /// <param name="id">The inspection.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<InspectionOverview?> OneAsync(ProjectId project, InspectionId id, CancellationToken cancellationToken);

    /// <summary>
    /// Every inspection of <paramref name="project"/> in <paramref name="tenant"/>, newest first, whoever is
    /// calling: for the application's operators, who work in no tenant. The one read here that looks past the
    /// caller's tenant, so it names the tenant itself; whoever calls it has decided first that the caller is an
    /// operator. One statement, on a context this one read has to itself.
    /// </summary>
    /// <param name="tenant">The tenant the project is of.</param>
    /// <param name="project">The project.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<InspectionOverview>> OfTenantProjectAsync(TenantId tenant, ProjectId project, CancellationToken cancellationToken);
}
