using DDDToolkit.Supporting.Tenancy.Access;
using Examples.Tenancy.Projects.Application.Crew;
using Examples.Tenancy.Projects.Application.Operators;
using Examples.Tenancy.Projects.Application.Overview;
using Examples.Tenancy.Projects.Application.ProjectRoles;
using GreenDonut.Data;

namespace Examples.Tenancy.Projects.Application.StoredProjects;

/// <summary>
/// The projects of the caller's tenant, their project roles, and Tenancy's rows next to them, for one query, on a
/// context that query has to itself. Opened by <see cref="IProjectReads.Open"/> and disposed when the query has run.
/// </summary>
/// <remarks>
/// Who may see a project is asked inside the statement that reads the projects, so the two halves meet here. The
/// application asks the Membership package's questions about projects for a reach, a set not yet read
/// (<see cref="MemberReach{TResourceId}"/>), and the reading puts that into its own statement, where it becomes
/// subqueries over the crews, the project roles and Tenancy's rows. What the application depends on, though no
/// reference says so:
/// <list type="bullet">
/// <item><see cref="Tenancy"/>, <see cref="Queries"/> and the projects come from one provider and one context, so
/// a reach put into this reading is translated and sent with the projects as one statement.</item>
/// <item>Instants compare as instants where the statement runs, so a crew membership, or a role held in one,
/// that ended yesterday stops counting inside it.</item>
/// <item>Nothing is tracked, and what comes back is data: a reading only reads. A project a command changes is
/// loaded through <see cref="IProjectStore"/>.</item>
/// </list>
/// The division: the application decides who asks for which key, at which moment; the package says which crews,
/// roles and units that reaches; the reading writes the statement around it, with the projects' own columns.
/// <para>
/// The projects and the project roles keep to the current caller's tenant, as the storage's tenant filter does:
/// a row of another tenant is simply not there. Tenancy's questions keep what they read of <see cref="Tenancy"/>
/// to the caller's tenant themselves.
/// </para>
/// </remarks>
public interface IProjectReading : IAsyncDisposable
{
    /// <summary>
    /// Tenancy's rows the access questions read, on this reading's context, so a question such as
    /// <c>UnitsWhereIHold</c> becomes a subquery of a project statement. They are access facts: ids, keys,
    /// periods and statuses, with no name to read, so nothing this module answers can carry one.
    /// </summary>
    ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId> Tenancy { get; }

    /// <summary>Runs <see cref="Tenancy"/>'s questions, and anything composed of them, where they are stored.</summary>
    IQueryExecutor Queries { get; }

    /// <summary>
    /// A page of the projects within <paramref name="reach"/> that <paramref name="filter"/> keeps, by number,
    /// each with how it was reached and nothing of its crew. One statement, whatever the number of projects, and
    /// the size of the whole list is counted in it only when <paramref name="paging"/> asks for it.
    /// </summary>
    /// <remarks>
    /// A number is unique in its tenant, so it orders the list by itself and marks a place in it: the page after a
    /// number, or before one, holds the same projects whatever was added or closed elsewhere in the list
    /// meanwhile. The page makes the cursors itself, and a cursor carries no right: the page it asks for is
    /// filtered for the caller like the first one. A text that is no cursor of this list is refused, not read as
    /// a place.
    /// </remarks>
    /// <param name="reach">Who asks, for the key that sees a project.</param>
    /// <param name="filter">What of the reach to list.</param>
    /// <param name="paging">How many, after or before which place, and whether to count the whole list.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="DDDToolkit.Exceptions.RefusalException">
    /// <c>projects.cursor-invalid</c>: the place <paramref name="paging"/> names is not a cursor this list gave.
    /// </exception>
    Task<Page<ProjectOverview>> PageAsync(MemberReach<ProjectId> reach, ProjectListFilter filter, PagingArguments paging, CancellationToken cancellationToken);

    /// <summary>
    /// Those of <paramref name="ids"/> that are within <paramref name="reach"/>, each with how it was reached and
    /// nothing of its crew, in one statement. A project out of the caller's reach is missing from the answer,
    /// exactly as one that does not exist.
    /// </summary>
    /// <param name="ids">The projects asked about.</param>
    /// <param name="reach">Who asks, for the key that sees a project.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<ProjectOverview>> ByIdsAsync(IReadOnlyCollection<ProjectId> ids, MemberReach<ProjectId> reach, CancellationToken cancellationToken);

    /// <summary>
    /// The crews of those of <paramref name="projects"/> that are within <paramref name="reach"/>, each member with
    /// the roles it holds, and each crew with whether it is within <paramref name="manage"/>, in one statement. A
    /// project out of the caller's reach is missing from the answer.
    /// </summary>
    /// <param name="projects">The projects asked about.</param>
    /// <param name="reach">Who asks, for the key that sees a project: what decides which crews are answered.</param>
    /// <param name="manage">
    /// The reach for the key that manages a crew, of the same caller at the same moment: what decides, for each
    /// crew answered, whether its members' roles are the caller's to read.
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<ProjectCrewData>> CrewsOfAsync(IReadOnlyCollection<ProjectId> projects, MemberReach<ProjectId> reach, MemberReach<ProjectId> manage, CancellationToken cancellationToken);

    /// <summary>
    /// What decides what the caller may do to each of <paramref name="projects"/> it sees, in one statement: the
    /// project's state, the keys of <paramref name="reach"/> held on it, and whether <paramref name="units"/>
    /// holds any unit. A project the caller does not see is not in the answer; one it sees is, whatever it holds.
    /// </summary>
    /// <param name="projects">The projects asked about.</param>
    /// <param name="reach">Which keys are asked about, for the caller.</param>
    /// <param name="units">
    /// The units where the caller holds a key asked at a unit, as a set not yet read, over this reading's rows:
    /// whether it is empty is answered inside the statement.
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyDictionary<ProjectId, ProjectStanding>> StandingOnAsync(
        IReadOnlyCollection<ProjectId> projects,
        MemberKeyReach<ProjectId> reach,
        IQueryable<OrganizationUnitId> units,
        CancellationToken cancellationToken);

    /// <summary>
    /// The keys of <paramref name="reach"/> the caller holds on each of <paramref name="projects"/> it sees, through
    /// its crew or through the organization, in one statement. A project the caller does not see, or holds none of
    /// the keys on, is not in the answer.
    /// </summary>
    /// <param name="projects">The projects asked about.</param>
    /// <param name="reach">Which keys are asked about, for the caller.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyDictionary<ProjectId, IReadOnlySet<string>>> KeysOnAsync(
        IReadOnlyCollection<ProjectId> projects,
        MemberKeyReach<ProjectId> reach,
        CancellationToken cancellationToken);

    /// <summary>
    /// What Projects tells another module about each of <paramref name="ids"/> the caller sees, in one statement:
    /// that it sees it, whether it holds the key of <paramref name="act"/> on it, whether it is closed, and its
    /// planned range. A project the caller does not see is not in the answer.
    /// </summary>
    /// <param name="ids">The projects asked about.</param>
    /// <param name="see">The reach that decides whether a project is there for the caller.</param>
    /// <param name="act">The reach for the key the asking module needs, of the same caller at the same moment.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyDictionary<ProjectId, ProjectAnswer>> AnswersAsync(
        IReadOnlyCollection<ProjectId> ids,
        MemberReach<ProjectId> see,
        MemberReach<ProjectId> act,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every project of <paramref name="tenant"/>, by number, whoever is calling: for the application's operators,
    /// who work in no tenant. The one read here that looks past the caller's tenant, so it names the tenant itself
    /// and takes no reach; whoever calls it has decided first that the caller is an operator. One statement.
    /// </summary>
    /// <remarks>
    /// On a database that checks every row, the operators' role reads what the module's rule for it admits, which
    /// is every project, and any other caller reads nothing through here that its own policies do not show it.
    /// </remarks>
    /// <param name="tenant">The tenant whose projects are read.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<TenantProject>> OfTenantAsync(TenantId tenant, CancellationToken cancellationToken);

    /// <summary>
    /// The tenant's project roles, archived ones included, by name, each with the keys it gives: those with
    /// <paramref name="ids"/>, or every one when that is <see langword="null"/>. One statement.
    /// </summary>
    /// <param name="ids">The roles asked about, or <see langword="null"/> for all of them.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<ProjectRoleListing>> ProjectRolesAsync(IReadOnlyCollection<ProjectRoleId>? ids, CancellationToken cancellationToken);
}
