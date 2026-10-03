using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Examples.Tenancy.Projects.Application.Crew;
using Examples.Tenancy.Projects.Application.Operators;
using Examples.Tenancy.Projects.Application.Overview;
using Examples.Tenancy.Projects.Application.ProjectRoles;
using Examples.Tenancy.Projects.Contracts.Gate;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects.ValueObjects;
using Examples.Tenancy.Shared.Domain.ValueObjects;
using Examples.Tenancy.Shared.Infrastructure.Paging;
using GreenDonut.Data;
using Microsoft.EntityFrameworkCore;

namespace Examples.Tenancy.Projects.Infrastructure.Persistence;

/// <summary>
/// The application project's <see cref="IProjectReads"/>: every reading on a context of its own, never on the
/// request's context, which is the unit of work of its commands.
/// </summary>
/// <remarks>
/// A context runs one query at a time, and a request's queries may run side by side. So a reading takes a context
/// from the factory the module registers next to its context, and disposes it with the reading. The factory
/// builds it with the options the request's own context has, so the tenant filter and the caller it reads are the
/// same. It holds no context and no state of its own, so one instance serves every query of a request, whichever
/// run at once.
/// <para>
/// Internal: the module's registration, in this project, is the only code that names it. Everything else asks for
/// the port.
/// </para>
/// </remarks>
/// <param name="contexts">Makes a context for one reading.</param>
internal sealed class EfProjectReads(IDbContextFactory<ProjectsContext> contexts) : IProjectReads
{
    /// <inheritdoc />
    public IProjectReading Open() => new Reading(contexts.CreateDbContext());

    /// <summary>
    /// The projects, their project roles and Tenancy's read model on one context, which the reading owns: what makes
    /// a reach and the projects one statement.
    /// </summary>
    /// <remarks>
    /// A reach is put into a query with the Membership package's own extensions (<c>Within</c>, <c>Reached</c>,
    /// <c>KeysOn</c>), which add its conditions as subqueries over the crews, the project roles and Tenancy's rows.
    /// This context is one made for the reading, not the request's, so each is handed it: the rows of the project
    /// roles and what Tenancy answers are queries of the context the statement runs on.
    /// </remarks>
    /// <param name="db">A context of the reading's own, from the factory.</param>
    private sealed class Reading(ProjectsContext db) : IProjectReading
    {
        /// <summary>The mark of the row that says a project is seen, in the place of a key. No key starts with it.</summary>
        private const string SeenMark = "#seen";

        /// <summary>The mark of the row that says the caller holds the key asked at a unit somewhere, in the place of a key.</summary>
        private const string UnitMark = "#at-a-unit";

        /// <inheritdoc />
        public ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId> Tenancy { get; } =
            new EfTenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>(db);

        /// <inheritdoc />
        /// <remarks>
        /// The reading's, not one from the container: whoever runs a query built over this context must run it
        /// with this context's provider.
        /// </remarks>
        public IQueryExecutor Queries => EfQueryExecutor.Instance;

        /// <summary>
        /// The projects of the caller's tenant, for reading: nothing a reading finds is tracked, and what a
        /// statement reads of a crew, it reads in that one statement.
        /// </summary>
        /// <remarks>
        /// One statement on purpose, and said so. Left unsaid, Entity Framework reads a crew with its roles the same
        /// way and warns that nobody chose.
        /// </remarks>
        private IQueryable<Project> Projects => db.Projects.AsNoTracking().AsSingleQuery();

        /// <inheritdoc />
        /// <remarks>
        /// By number, which the unique index on a tenant's numbers makes an order of its own. The rows are made
        /// first and paged after: a row holds the project's own columns and how it was reached, so the statement
        /// reads no crew, and the page compares, orders and cuts by the number where the statement runs. The page
        /// is of rows; what is handed back is the same page of what a caller is shown, with the rows' cursors.
        /// <para>
        /// A marker that is not a cursor of this list is refused before the statement, by the check every module's
        /// paged read makes (<see cref="ListCursors"/>).
        /// </para>
        /// </remarks>
        public async Task<Page<ProjectOverview>> PageAsync(MemberReach<ProjectId> reach, ProjectListFilter filter, PagingArguments paging, CancellationToken cancellationToken)
        {
            var page = await RowsOf(Listed(reach, filter), reach)
                .OrderBy(row => row.Number)
                .TakingOnlyItsOwnCursors(paging, () => ProjectRefusals.Of(ProjectRefusals.CursorInvalid))
                .ToPageAsync(paging, cancellationToken);

            return Page<ProjectOverview>.Create(
                [.. page.Items.Select(row => Overview(row, reach))],
                page.Entries,
                page.HasNextPage,
                page.HasPreviousPage,
                (PageEntry<Row> entry) => page.CreateCursor(entry),
                page.TotalCount);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ProjectOverview>> ByIdsAsync(IReadOnlyCollection<ProjectId> ids, MemberReach<ProjectId> reach, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ids);

            var asked = ids.ToList();
            var rows = await RowsOf(Projects.Where(project => asked.Contains(project.Id)).Within(reach, db), reach).ToListAsync(cancellationToken);

            return [.. rows.Select(row => Overview(row, reach))];
        }

        /// <inheritdoc />
        /// <remarks>
        /// One statement on purpose: a crew's roles hang under its members, so the rows read are one per role and
        /// not members times roles, and what is read is of one moment. Whether the caller manages a crew is read
        /// with it, as the access check reads how a key is held: the projects seen are the filter, and how
        /// <paramref name="manage"/> reaches each of them is what is read with it.
        /// </remarks>
        public async Task<IReadOnlyList<ProjectCrewData>> CrewsOfAsync(IReadOnlyCollection<ProjectId> projects, MemberReach<ProjectId> reach, MemberReach<ProjectId> manage, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(projects);
            ArgumentNullException.ThrowIfNull(manage);

            var asked = projects.ToList();
            var seen = Projects.Where(project => asked.Contains(project.Id)).Within(reach, db);

            // The application's own work in the tenant manages every crew of it, by neither way.
            var everyCrew = manage.Everything;
            return await seen.Reached(manage, db)
                .Select(found => new ProjectCrewData(
                    found.Resource.Id,
                    found.Resource.OwnerSeatId,
                    everyCrew || found.AsMember || found.FromAbove,
                    found.Resource.Crew
                        .Select(member => new CrewMemberData(
                            member.MemberId,
                            member.StartsAt,
                            member.EndsAt,
                            member.Roles.Select(held => new CrewRoleData(held.RoleId, held.StartsAt, held.EndsAt)).ToList()))
                        .ToList()))
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        /// <remarks>
        /// The statement of <see cref="KeysOnAsync"/>, with two parts more: a row for every project seen, so one
        /// the caller holds nothing on is answered too, and a row for each when <paramref name="units"/> is not
        /// empty. Both carry a mark in the place of a key, which no key can be; the project's state is read with
        /// every row.
        /// </remarks>
        public async Task<IReadOnlyDictionary<ProjectId, ProjectStanding>> StandingOnAsync(
            IReadOnlyCollection<ProjectId> projects,
            MemberKeyReach<ProjectId> reach,
            IQueryable<OrganizationUnitId> units,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(projects);
            ArgumentNullException.ThrowIfNull(reach);
            ArgumentNullException.ThrowIfNull(units);

            var ids = projects.ToList();
            var asked = Projects.Where(project => ids.Contains(project.Id));
            var seen = asked.Within(reach.See, db);

            if (reach.See.Everything)
            {
                // The application's own work in the tenant holds every key on every project of it, so only which
                // exist is read.
                var all = await seen.Select(project => new { project.Id, project.State, AtAUnit = units.Any() }).ToListAsync(cancellationToken);
                return all.ToDictionary(project => project.Id, project => new ProjectStanding(project.State, reach.Keys.ToHashSet(StringComparer.Ordinal), project.AtAUnit));
            }

            var isSeen = SeenMark;
            var atAUnit = UnitMark;
            var marked = asked.KeysOn(reach, db)
                .Concat(seen.Select(project => new MemberKeyOn<ProjectId> { Resource = project.Id, Key = isSeen }))
                .Concat(seen.Where(project => units.Any()).Select(project => new MemberKeyOn<ProjectId> { Resource = project.Id, Key = atAUnit }));
            var rows = await (from row in marked
                              join project in Projects on row.Resource equals project.Id
                              select new { row.Resource, project.State, row.Key })
                .ToListAsync(cancellationToken);

            return rows
                .GroupBy(row => row.Resource)
                .ToDictionary(
                    held => held.Key,
                    held => new ProjectStanding(
                        held.First().State,
                        held.Select(row => row.Key).Where(key => key != SeenMark && key != UnitMark).ToHashSet(StringComparer.Ordinal),
                        held.Any(row => row.Key == UnitMark)));
        }

        /// <inheritdoc />
        /// <remarks>
        /// One statement of a part for each key asked about, each over the projects asked about that the caller
        /// sees: the package's, which reads the key held as a member, by owning the project and at its unit. The
        /// parts are put together where the statement runs and grouped here.
        /// </remarks>
        public async Task<IReadOnlyDictionary<ProjectId, IReadOnlySet<string>>> KeysOnAsync(
            IReadOnlyCollection<ProjectId> projects,
            MemberKeyReach<ProjectId> reach,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(projects);
            ArgumentNullException.ThrowIfNull(reach);

            var ids = projects.ToList();
            var asked = Projects.Where(project => ids.Contains(project.Id));

            if (reach.See.Everything)
            {
                // The application's own work in the tenant holds every key on every project of it, so only which
                // exist is read.
                var all = await asked.Select(project => project.Id).ToListAsync(cancellationToken);
                return all.ToDictionary(project => project, _ => (IReadOnlySet<string>)reach.Keys.ToHashSet(StringComparer.Ordinal));
            }

            return (await asked.KeysOn(reach, db).ToListAsync(cancellationToken))
                .GroupBy(pair => pair.Resource)
                .ToDictionary(pairs => pairs.Key, pairs => (IReadOnlySet<string>)pairs.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<ProjectId, ProjectAnswer>> AnswersAsync(
            IReadOnlyCollection<ProjectId> ids,
            MemberReach<ProjectId> see,
            MemberReach<ProjectId> act,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(act);

            // Whether it is there for the caller is the filter; how the caller holds the key on it is what is read.
            var asked = ids.ToList();
            var answered = await Projects.Where(project => asked.Contains(project.Id))
                .Within(see, db)
                .Reached(act, db)
                .Select(found => new { found.Resource.Id, found.Resource.State, found.Resource.Planned, found.AsMember, found.FromAbove })
                .ToListAsync(cancellationToken);

            return answered.ToDictionary(
                project => project.Id,
                project => new ProjectAnswer(
                    Visible: true,
                    Allowed: act.Via(project.AsMember, project.FromAbove) is not null,
                    Closed: project.State == ProjectState.Closed,
                    Planned: project.Planned));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Past the tenant filter, which answers an operator nothing since it works in no tenant, and kept to the
        /// one tenant asked about by the statement itself.
        /// </remarks>
        public async Task<IReadOnlyList<TenantProject>> OfTenantAsync(TenantId tenant, CancellationToken cancellationToken)
            => await db.Projects.AsNoTracking()
                .IgnoreQueryFilters([TenancyQueryFilter.Name])
                .Where(project => project.TenantId == tenant)
                .OrderBy(project => project.Number)
                .Select(project => new TenantProject(
                    project.Id,
                    project.Number,
                    project.Name,
                    project.UnitId,
                    project.State,
                    project.OwnerSeatId,
                    project.Planned,
                    new ChangedBy(
                        EF.Property<string>(project, TenancyAttribution.ChangedByKind),
                        EF.Property<SeatId?>(project, TenancyAttribution.ChangedBySeat))))
                .ToListAsync(cancellationToken);

        /// <inheritdoc />
        /// <remarks>Under the tenant filter: a role of another tenant is not there to be read.</remarks>
        public async Task<IReadOnlyList<ProjectRoleListing>> ProjectRolesAsync(IReadOnlyCollection<ProjectRoleId>? ids, CancellationToken cancellationToken)
        {
            var roles = db.ProjectRoles.AsNoTracking();
            if (ids is not null)
            {
                var asked = ids.ToList();
                roles = roles.Where(role => asked.Contains(role.Id));
            }

            return await roles
                .OrderBy(role => role.Name)
                .ThenBy(role => role.Id)
                .Select(role => new ProjectRoleListing(role.Id, role.Name, role.Description, role.MadeFrom, role.Status, role.Keys))
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => db.DisposeAsync();

        /// <summary>
        /// The projects within <paramref name="reach"/> that <paramref name="filter"/> keeps: what a page lists and a
        /// count counts, so the two never differ.
        /// </summary>
        private IQueryable<Project> Listed(MemberReach<ProjectId> reach, ProjectListFilter filter)
        {
            ArgumentNullException.ThrowIfNull(filter);

            var projects = Projects;
            if (filter.State is { } state)
            {
                projects = projects.Where(project => project.State == state);
            }

            if (filter.Units is { } units)
            {
                projects = projects.Where(project => units.Contains(project.UnitId));
            }

            if (filter.Text is { Length: > 0 } text)
            {
                // The text is matched as text: what LIKE reads as a wildcard is escaped, so "100%" finds a name with
                // a percent sign in it and not every name. Lowered on both sides, so the case does not matter. The
                // name is lowered by the database, and Postgres folds every letter: a name written with a capital
                // outside A to Z ("Écluse") is found by that letter in either case.
                var escaped = text.ToLowerInvariant().Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);
                var starts = escaped + "%";
                var contains = "%" + escaped + "%";
                projects = projects.Where(project =>
                    EF.Functions.Like(project.Number.ToLower(), starts, @"\")
                    || EF.Functions.Like(project.Name.ToLower(), contains, @"\"));
            }

            return projects.Within(reach, db);
        }

        /// <summary>
        /// What a list and a lookup read of each of <paramref name="projects"/>: its own columns, who changed its row
        /// last and how <paramref name="reach"/> reaches it, and nothing of its crew. Made by setting members, so a
        /// page can compare and order by one of them where the statement runs.
        /// </summary>
        /// <remarks>
        /// Who changed the row is two columns the project has no property for, which every save fills in from the
        /// caller it runs as. They are read here, in the statement, so a page of rows carries them and nothing
        /// needs to be tracked to show them.
        /// </remarks>
        private IQueryable<Row> RowsOf(IQueryable<Project> projects, MemberReach<ProjectId> reach)
            => projects.Reached(reach, db).Select(found => new Row
            {
                Id = found.Resource.Id,
                Version = found.Resource.Version,
                Name = found.Resource.Name,
                Number = found.Resource.Number,
                UnitId = found.Resource.UnitId,
                State = found.Resource.State,
                OwnerSeatId = found.Resource.OwnerSeatId,
                Planned = found.Resource.Planned,
                AsMember = found.AsMember,
                FromAbove = found.FromAbove,
                ChangedByKind = EF.Property<string>(found.Resource, TenancyAttribution.ChangedByKind),
                ChangedBySeat = EF.Property<SeatId?>(found.Resource, TenancyAttribution.ChangedBySeat),
            });

        /// <summary>The project of <paramref name="row"/> as its caller is shown it: the row, and how <paramref name="reach"/> reaches it.</summary>
        private static ProjectOverview Overview(Row row, MemberReach<ProjectId> reach)
            => new(
                row.Id,
                row.Version,
                row.Name,
                row.Number,
                row.UnitId,
                row.State,
                row.OwnerSeatId,
                row.Planned,
                ProjectAccess.Via(reach.Via(row.AsMember, row.FromAbove)),
                new ChangedBy(row.ChangedByKind, row.ChangedBySeat));

        /// <summary>A project's own columns, who changed its row last and how a reach reaches it: what a page is made of.</summary>
        private sealed class Row
        {
            public required ProjectId Id { get; init; }

            public required long Version { get; init; }

            public required string Name { get; init; }

            public required string Number { get; init; }

            public required OrganizationUnitId UnitId { get; init; }

            public required ProjectState State { get; init; }

            public required SeatId OwnerSeatId { get; init; }

            public required DateRange? Planned { get; init; }

            public required bool AsMember { get; init; }

            public required bool FromAbove { get; init; }

            public required string ChangedByKind { get; init; }

            public required SeatId? ChangedBySeat { get; init; }
        }
    }
}
