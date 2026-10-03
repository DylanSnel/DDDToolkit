using System.Linq.Expressions;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections.ValueObjects;
using Examples.Tenancy.Shared.Infrastructure.Paging;
using GreenDonut.Data;
using Microsoft.EntityFrameworkCore;

namespace Examples.Tenancy.Inspections.Infrastructure.Persistence;

/// <summary>
/// The application project's <see cref="IInspectionReads"/>: every read on a context of its own, never on the
/// request's context, which is the unit of work of its commands.
/// </summary>
/// <remarks>
/// A context runs one query at a time, and a request's queries may run side by side. So each read takes a context
/// from the factory the module registers next to its context, and disposes it when the read is done. The factory
/// builds it with the options the request's own context has, so the tenant filter and the caller it reads are the
/// same. It holds no context and no state of its own, so one instance serves every query of a request, whichever
/// run at once.
/// <para>
/// The lists are paged by HotChocolate's own paging over Entity Framework, <c>ToPageAsync</c> and
/// <c>ToBatchPageAsync</c>: it reads the order of the query, writes the keys of a row into that row's cursor,
/// and asks for the rows after or before a cursor by comparing those keys where the statement runs. Nothing is
/// counted and nothing is skipped by number. A marker that is not a cursor of this list is refused before the
/// statement, by the check every module's paged read makes (<see cref="ListCursors"/>).
/// </para>
/// <para>
/// Internal: the module's registration, in this project, is the only code that names it. Everything else asks for
/// the port.
/// </para>
/// </remarks>
/// <param name="contexts">Makes a context for one read.</param>
internal sealed class EfInspectionReads(IDbContextFactory<InspectionsContext> contexts) : IInspectionReads
{
    /// <inheritdoc />
    public async Task<Page<InspectionOverview>> PageForProjectAsync(ProjectId project, PagingArguments paging, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        var page = await NewestFirst(db.Inspections.Where(inspection => inspection.ProjectId == project))
            .TakingOnlyItsOwnCursors(paging, NotThisLists)
            .ToPageAsync(paging, cancellationToken);

        // The rows are paged as they are stored, since the cursor is made of what the list is ordered by, and
        // then shown as the port answers them. The page keeps making its cursors from the stored rows.
        return Page<InspectionOverview>.Create(
            [.. page.Items.Select(inspection => Overview(db, inspection))],
            page.Entries,
            page.HasNextPage,
            page.HasPreviousPage,
            (PageEntry<Inspection> entry) => page.CreateCursor(entry),
            page.TotalCount);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One statement for all the projects: each project's rows are ordered and cut within the statement, so the
    /// page of one project does not depend on how many the others have.
    /// </remarks>
    public async Task<IReadOnlyDictionary<ProjectId, Page<InspectionOverview>>> PagesForProjectsAsync(IReadOnlyCollection<ProjectId> projects, PagingArguments paging, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projects);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        var asked = projects.ToList();
        return await NewestFirst(db.Inspections.Where(inspection => asked.Contains(inspection.ProjectId)))
            .TakingOnlyItsOwnCursors(paging, NotThisLists)
            .ToBatchPageAsync(inspection => inspection.ProjectId, inspection => Overview(db, inspection), paging, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<InspectionOverview?> OneAsync(ProjectId project, InspectionId id, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        return await db.Inspections
            .Where(inspection => inspection.Id == id && inspection.ProjectId == project)
            .Select(Read)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Past the tenant filter, which answers an operator nothing since it works in no tenant, and kept to the one
    /// tenant asked about by the statement itself. A list and no page: it is one project of one tenant.
    /// </remarks>
    public async Task<IReadOnlyList<InspectionOverview>> OfTenantProjectAsync(TenantId tenant, ProjectId project, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        return await NewestFirst(db.Inspections
                .IgnoreQueryFilters([TenancyQueryFilter.Name])
                .Where(inspection => inspection.TenantId == tenant && inspection.ProjectId == project))
            .Select(Read)
            .ToListAsync(cancellationToken);
    }

    /// <summary>The refusal of a marker that is not a cursor a list of inspections gave.</summary>
    private static Exception NotThisLists() => InspectionRefusals.Of(InspectionRefusals.CursorInvalid);

    /// <summary>
    /// The order every list of inspections has: newest first, and those recorded at the same instant by their ids,
    /// so the order is whole and a cursor marks one place in it. The instants are stored as UTC, which the
    /// database orders and compares; the id is a paging key because the module's generated bindings registered it
    /// as one.
    /// </summary>
    private static IOrderedQueryable<Inspection> NewestFirst(IQueryable<Inspection> inspections)
        => inspections.OrderByDescending(inspection => inspection.RecordedAt).ThenBy(inspection => inspection.Id);

    /// <summary>
    /// What a read that pages nothing answers with, read where the statement runs: the inspection's own columns,
    /// and who the save that wrote the row ran as, which every save fills in from its caller.
    /// </summary>
    private static readonly Expression<Func<Inspection, InspectionOverview>> Read = inspection => new InspectionOverview(
        inspection.Id,
        inspection.ProjectId,
        inspection.Title,
        inspection.Days,
        inspection.RecordedBy,
        inspection.RecordedAt,
        new ChangedBy(
            EF.Property<string>(inspection, TenancyAttribution.ChangedByKind),
            EF.Property<SeatId?>(inspection, TenancyAttribution.ChangedBySeat)));

    /// <summary>
    /// The same answer for a row of a page. A page is made of the stored rows, so who wrote the row is taken from
    /// what <paramref name="db"/> read with it: the inspection has no property for it, and the context that read
    /// the row keeps those columns beside it. That is why the paged reads are tracked, on a context that lives
    /// for the one read.
    /// </summary>
    private static InspectionOverview Overview(InspectionsContext db, Inspection inspection)
    {
        var row = db.Entry(inspection);
        return new(
            inspection.Id,
            inspection.ProjectId,
            inspection.Title,
            inspection.Days,
            inspection.RecordedBy,
            inspection.RecordedAt,
            new ChangedBy(
                row.Property<string>(TenancyAttribution.ChangedByKind).CurrentValue,
                row.Property<SeatId?>(TenancyAttribution.ChangedBySeat).CurrentValue));
    }
}
