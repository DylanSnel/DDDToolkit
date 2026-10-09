using Examples.Tenancy.Projects.Api.GraphQL;
using Examples.Tenancy.Projects.Api.ProjectRoles.GraphQL;
using Examples.Tenancy.Projects.Application.Crew;
using Examples.Tenancy.Projects.Application.Overview;
using Examples.Tenancy.Projects.Application.ProjectRoles;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;

namespace Examples.Tenancy.Projects.Api.Overview.GraphQL;

/// <summary>
/// A project, with its crew and what the caller may do to it: the GraphQL type <c>Project</c>, declared over the
/// answer the application's queries give, so nothing is copied into a record of this project's own.
/// </summary>
/// <remarks>
/// The fields of the project's own row are the record's properties. Two of them are value objects, and are types
/// of the schema as they are: the range of days it is planned for, <c>DateRange</c>, which the modules share, and
/// who changed it last, <c>ChangedBy</c>. Inspections' schema shows the same two types, and a value object is
/// shareable by itself, so the gateway composes one of each without either schema saying more. What takes
/// another read is a resolver here, behind a loader: its crew, the caller's own roles on it and what the caller
/// may do to it are asked for every project of an answer in one question each, and only when a client selects
/// them.
/// <para>
/// Its unit, its owner and its crew's seats and roles are named by id; what they are called comes from Tenancy,
/// through the gateway. Inspections names a project by its id in turn, and the gateway fills in the rest from
/// here. It is an entity with the key <c>id</c>, so the host's conventions make every other field nullable: a
/// project that is not there, or not the caller's to see, arrives as its id with nothing else, and without an error.
/// </para>
/// <para>
/// A project is the one node type: its id is a node id, and <c>node(id:)</c> finds it through the loader
/// <c>project(id:)</c> uses, so neither goes round what the caller may see.
/// </para>
/// </remarks>
[ObjectType<ProjectOverview>]
[EntityKey("id")]
internal static partial class ProjectType
{
    /// <summary>
    /// What a field that loads through a data loader weighs for each project of a list. HotChocolate weighs a field
    /// with a resolver that reads as if every row read on its own, and a page of projects with their crews would
    /// then cost more than a request may. These read once for a batch, as a rule the whole page, so a row weighs
    /// little. Every field of this schema that takes a loader says so, the lookup included.
    /// </summary>
    internal const double LoadedForThePage = 1;

    static partial void Configure(IObjectTypeDescriptor<ProjectOverview> descriptor) => descriptor.Name("Project");

    /// <summary>What <c>node(id:)</c> answers a project by: the read the lookup uses, and nothing for a project out of the caller's reach.</summary>
    [NodeResolver]
    public static async Task<ProjectOverview?> GetProjectByIdAsync(ProjectId id, IProjectByIdDataLoader projects, CancellationToken cancellationToken)
        => await projects.LoadAsync(id, cancellationToken);

    /// <summary>The unit it belongs to.</summary>
    [BindMember(nameof(ProjectOverview.UnitId))]
    public static ReferencedOrganizationUnit GetUnit([Parent] ProjectOverview project) => new(project.UnitId);

    /// <summary>The seat that owns it.</summary>
    [BindMember(nameof(ProjectOverview.OwnerSeat))]
    public static ReferencedSeat GetOwner([Parent] ProjectOverview project) => new(project.OwnerSeat);

    /// <summary>Its crew, the owner first.</summary>
    [Cost(LoadedForThePage)]
    public static async Task<IReadOnlyList<CrewOverview>?> GetCrewAsync([Parent] ProjectOverview project, ICrewByProjectIdDataLoader crews, CancellationToken cancellationToken)
        => (await crews.LoadAsync(project.Id, cancellationToken))?.Members;

    /// <summary>
    /// The project roles the caller holds on its crew now, each read, for every project of the page at once, when
    /// anything of it but its id is asked.
    /// </summary>
    [Cost(LoadedForThePage)]
    public static async Task<IReadOnlyList<ProjectRoleListing>?> GetMyRolesAsync(
        [Parent] ProjectOverview project,
        IResolverContext context,
        ICrewByProjectIdDataLoader crews,
        IProjectRoleByIdDataLoader roles,
        CancellationToken cancellationToken)
        => await crews.LoadAsync(project.Id, cancellationToken) is { } crew
            ? await ProjectRolesDataLoaders.NamedAsync(context, crew.MyRoleIds, roles, cancellationToken)
            : null;

    /// <summary>What the caller may do to it.</summary>
    [Cost(LoadedForThePage)]
    public static async Task<ProjectAbilities?> GetCanAsync([Parent] ProjectOverview project, IAbilitiesByProjectIdDataLoader abilities, CancellationToken cancellationToken)
        => await abilities.LoadAsync(project.Id, cancellationToken);
}
