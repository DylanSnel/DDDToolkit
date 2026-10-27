using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// Who may see a project and act on it, for the current caller, as this module asks it: the Membership package's
/// questions about projects, put into this module's own statements, and Tenancy's, where a permission is asked at a unit.
/// It is also the <see cref="IProjectGate"/> other modules ask.
/// </summary>
/// <remarks>
/// A seat reaches a project in two ways, and the package asks both inside one statement: a role held at the
/// project's unit, or above it, which Tenancy answers; and the project's crew, where being on it lets a seat see
/// the project and a project role it holds there gives its permissions on that project alone. The owner holds every permission
/// of a lead by owning the project. The rules that say all of that are the projects' (<see cref="ProjectMembership"/>);
/// here they are only asked.
/// <para>
/// A project the caller reaches in neither way does not exist for them: the package's check answers
/// <c>projects.not-found</c> exactly as for a project of another tenant, so nobody learns a project is there. A
/// reach is a set not yet read, which a reading puts into its own statement; a permission the catalogue does not know is
/// a mistake in the code that asks, refused for every project whoever calls. It keeps nothing itself, so the one
/// instance a request has serves its queries whichever run at once.
/// </para>
/// </remarks>
/// <param name="members">The Membership package's questions about projects, for the current caller.</param>
/// <param name="reads">Where projects, and Tenancy's rows next to them, are read: a context per reading.</param>
/// <param name="tenancy">Tenancy, asked about the current caller.</param>
/// <param name="catalogue">The permissions the application knows.</param>
public sealed class ProjectAccess(IMemberQuestions<ProjectId> members, IProjectReads reads, SampleTenancy tenancy, TenancyCatalogue catalogue) : IProjectGate
{
    /// <summary>
    /// Tenancy's queries about the current caller, in a reading's rows. Everything in this project that asks
    /// Tenancy asks through here, so where this module reads Tenancy from is decided in one place.
    /// </summary>
    /// <remarks>
    /// The queries bind the caller, and the moment, when they are asked for, so they are asked for per use and
    /// never kept. Internal: the use cases of this project ask through here, and nothing outside it asks Tenancy
    /// through Projects.
    /// </remarks>
    /// <param name="reading">The reading whose rows are asked over, and whose statement the answers go into.</param>
    internal SampleTenancyQueries TenancyIn(IProjectReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return tenancy.In(reading.Tenancy, reading.Queries);
    }

    /// <summary>
    /// The projects the caller reaches for <paramref name="permission"/>, as a set a reading puts into its own statement:
    /// those whose crew it is on with a project role that gives the permission, or for seeing a project those whose crew
    /// it is on at all, those it owns, and those at a unit where it holds the permission, there or above. All of its
    /// tenant's for the application's own work there; none for nobody.
    /// </summary>
    /// <param name="permission">A permission of the catalogue.</param>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>.</exception>
    public MemberReach<ProjectId> ReachFor(string permission) => members.Reach(Known(permission));

    /// <summary>
    /// The two reaches a crew is read with: which projects the caller sees, and on which of them it holds
    /// <paramref name="permission"/>, the permission that decides what more of a crew it is answered. Both are of one caller at
    /// one moment, and both go into the one statement that reads the crews.
    /// </summary>
    /// <param name="permission">A permission of the catalogue, asked about on each project that is seen.</param>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>.</exception>
    public (MemberReach<ProjectId> See, MemberReach<ProjectId> Hold) SeenAndHeld(string permission)
    {
        // The permission that was asked about first, so an unknown one is refused before anything else is.
        var hold = ReachFor(permission);
        return (members.Reach(ProjectPermissionNames.View), hold);
    }

    /// <summary>
    /// Which of <paramref name="permissions"/> the caller holds on which projects, as sets a reading puts into one
    /// statement. Only projects the caller sees are answered about.
    /// </summary>
    /// <param name="permissions">Permissions of the catalogue. A retired one is held nowhere.</param>
    /// <exception cref="ArgumentException">The catalogue does not know one of <paramref name="permissions"/>.</exception>
    public MemberPermissionReach<ProjectId> PermissionsReachFor(IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return members.PermissionReach([.. permissions.Distinct(StringComparer.Ordinal).Select(Known)]);
    }

    /// <summary>
    /// Those of <paramref name="permissions"/> the caller holds for the whole tenant, at its root: what a client fills its
    /// navigation from. One statement, on the query's reading; none when no permission is asked about.
    /// </summary>
    /// <param name="reading">The reading of the query that asks.</param>
    /// <param name="permissions">Permissions of the catalogue.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The permissions held, each once, in the order of their text.</returns>
    /// <exception cref="ArgumentException">The catalogue does not know one of <paramref name="permissions"/>.</exception>
    public async Task<IReadOnlyList<string>> HeldAtTenantAsync(IProjectReading reading, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Count == 0)
        {
            return [];
        }

        // A permission reaches the root only when it is held there: nothing is above it. System work in the tenant holds
        // every permission at every unit, so the same statement answers all of them for it.
        var queries = TenancyIn(reading);
        var roots = queries.Units().Where(unit => !unit.ParentId.HasValue).Select(unit => unit.Id);
        var held = queries.WhereIHold(permissions).Where(pair => roots.Contains(pair.Unit)).Select(pair => pair.Permission).Distinct();

        return [.. (await reading.Queries.ListAsync(held, cancellationToken)).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Requires the caller to hold <paramref name="permission"/> at <paramref name="unit"/>, there or above it: the check
    /// a command asked at a unit passes before its handler, where no crew can give the permission, alone or as one case of
    /// several, the unit a project moves to beside the permission on the project. One statement, on a reading of its own.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <param name="permission">The permission the request needs there.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody; <c>projects.not-permitted</c>, with the <c>Permission</c> and the
    /// <c>Unit</c>, when it does not hold the permission there.
    /// </exception>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>.</exception>
    public async Task RequireAtAsync(OrganizationUnitId unit, string permission, CancellationToken cancellationToken)
    {
        // The caller before anything is opened: nobody is refused as nobody, without a reading made for it.
        tenancy.RequireTenant();

        await using var reading = reads.Open();
        if (!await TenancyIn(reading).HoldsAtAsync(permission, unit, cancellationToken))
        {
            throw ProjectRefusals.Refuse(ProjectRefusals.NotPermitted, ("Permission", permission), ("Unit", unit));
        }
    }

    /// <summary>
    /// How <paramref name="permission"/> is held by the caller on a project it may see, or <see langword="null"/> when it
    /// is not held: for a query that answers with what its caller may do. One statement.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="permission">A permission of the catalogue.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller's own refusal when it is nobody; <c>projects.not-found</c> when the caller may not see the project.
    /// </exception>
    /// <exception cref="ArgumentException">The catalogue does not know <paramref name="permission"/>.</exception>
    public async Task<ProjectVia?> ViaAsync(ProjectId project, string permission, CancellationToken cancellationToken)
        => Via(await members.ViaAsync(project, Known(permission), cancellationToken));

    /// <summary>The module's word for how a permission is held, from the package's.</summary>
    /// <param name="via">How the package answered it, or <see langword="null"/> for a permission that is not held.</param>
    public static ProjectVia? Via(MemberVia? via) => via switch
    {
        MemberVia.Members => ProjectVia.Crew,
        MemberVia.Above => ProjectVia.Organization,
        MemberVia.System => ProjectVia.System,
        _ => null,
    };

    /// <inheritdoc />
    /// <remarks>
    /// A project the caller may not see is not visible, and then nothing else is said about it. The answer does
    /// not say whether a closed project would otherwise be allowed: an asking module refuses a closed project
    /// whatever the caller holds. A project the caller sees is answered with its planned range, whatever the permission:
    /// seeing the project is what it takes to know its plan. One statement, on a reading of its own, so a module
    /// may ask while its own queries run.
    /// </remarks>
    public async Task<ProjectAnswer> AskAsync(ProjectId project, string permission, CancellationToken cancellationToken)
    {
        // The reaches first, whatever the project: a permission the catalogue does not know is a mistake in the asking
        // module's code, and must fail for every project, not only for the ones the caller happens to see.
        var (see, act) = SeenAndHeld(permission);

        await using var reading = reads.Open();
        var answered = await reading.AnswersAsync([project], see, act, cancellationToken);

        return answered.TryGetValue(project, out var answer) ? answer : new ProjectAnswer(Visible: false, Allowed: false, Closed: false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The same answer as for one project, for each project the caller sees, and one statement whatever their
    /// number, on a reading of its own. A project the caller may not see is left out rather than answered as not
    /// visible: both say nothing about it, and a module that asked about a page of projects passes it over.
    /// </remarks>
    public async Task<IReadOnlyDictionary<ProjectId, ProjectAnswer>> AskAsync(IReadOnlyCollection<ProjectId> projects, string permission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var asked = projects.Distinct().ToList();
        if (asked.Count > IProjectGate.MostProjects)
        {
            throw new ArgumentException($"One question is about at most {IProjectGate.MostProjects} projects; this one names {asked.Count}. Ask in parts.", nameof(projects));
        }

        // The reaches first, whatever the projects, and even for none: an unknown permission fails for every question.
        var (see, act) = SeenAndHeld(permission);
        if (asked.Count == 0)
        {
            return new Dictionary<ProjectId, ProjectAnswer>();
        }

        await using var reading = reads.Open();
        return await reading.AnswersAsync(asked, see, act, cancellationToken);
    }

    /// <summary><paramref name="permission"/>, when the catalogue knows it: a permission it does not know is a mistake in the code that asks.</summary>
    private string Known(string permission)
        => catalogue.Knows(permission)
            ? permission
            : throw new ArgumentException("The catalogue knows no permission '" + permission + "'. A permission a module asks about is one of the catalogue's: a typo, or a module whose permissions were never added.", nameof(permission));
}
