namespace Examples.Tenancy.Inspections.Application.Access;

/// <summary>
/// What a request of this module requires of its caller where it takes Projects' gate to say whether the caller
/// meets it: a key held on a project, or on the projects of a list. A command or query says which, through
/// <see cref="IInspectionsRequest"/>, and <see cref="InspectionsAccessCheck"/> holds the caller to it before the
/// handler runs. What Tenancy can say, that the caller is an operator, a request declares with Tenancy's own
/// case, which the package's check decides.
/// </summary>
/// <remarks>
/// A closed set: the constructor is private, so the cases below are all there are, and each is a record, so two
/// requirements that say the same are equal. Each names a permission key and the project, or the projects, it is
/// held on, taken from the request. Whether the caller holds it there is not this module's to know: Projects
/// answers, through its gate, and the check maps the answer. A request that needs a check no case makes gets a
/// case of its own here, with its branch in the check, rather than a check in its route.
/// </remarks>
public abstract record InspectionsRequirement : AccessRequirement
{
    private InspectionsRequirement()
    {
    }

    /// <summary>
    /// <paramref name="Key"/> is held by the caller on <paramref name="Project"/>, open or closed. A project the
    /// caller may not see is <c>projects.not-found</c>, exactly as one that does not exist; one it may see without
    /// holding the key is <c>projects.not-permitted</c>, naming the key.
    /// </summary>
    /// <param name="Key">The key the request needs on the project.</param>
    /// <param name="Project">The project, from the request.</param>
    public sealed record OnProject(string Key, ProjectId Project) : InspectionsRequirement;

    /// <summary>
    /// <paramref name="Key"/> is held by the caller on <paramref name="Project"/>, and the project is open: for
    /// what adds to a project. In this order: <c>projects.not-found</c> for a project the caller may not see,
    /// <c>projects.closed</c> for a closed one whatever the caller holds, <c>projects.not-permitted</c>, naming the
    /// key, for an open one it does not hold the key on.
    /// </summary>
    /// <param name="Key">The key the request needs on the project.</param>
    /// <param name="Project">The project, from the request.</param>
    /// <param name="BySeat">
    /// Whether what the request does is done by a seat, and recorded as that seat's. A seat is its own; system work
    /// in the tenant must act for one, and work that acts for none is a mistake in the calling code, found before
    /// the project is asked about.
    /// </param>
    public sealed record OnOpenProject(string Key, ProjectId Project, bool BySeat) : InspectionsRequirement;

    /// <summary>
    /// The request is about several projects and is refused for none of them: it acts on those of
    /// <paramref name="Projects"/> the caller may see, and passes over the others in silence, one out of reach
    /// exactly as one that does not exist. The check asks only that the caller works in a tenant; the handler asks
    /// the gate once, about all of them, and what it answers about <paramref name="Key"/> on each project the
    /// caller sees is what the handler acts on.
    /// </summary>
    /// <param name="Key">The key asked about on each project.</param>
    /// <param name="Projects">The projects, from the request: the same list, so two requirements of one request are equal.</param>
    public sealed record OnProjectsInReach(string Key, IReadOnlyList<ProjectId> Projects) : InspectionsRequirement;
}
