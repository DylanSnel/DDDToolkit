namespace Examples.Tenancy.Inspections.Application.Access;

/// <summary>
/// Decides the requirements that are Inspections' own (<see cref="InspectionsRequirement"/>) before a handler of
/// this module runs: registered for <see cref="IInspectionsRequest"/>, next to Tenancy's check for the case that
/// is Tenancy's, and asked by the module's access behavior.
/// </summary>
/// <remarks>
/// Who may do what to a project is Projects' to answer, so every case asks its gate,
/// <see cref="IProjectGate"/>, and maps the answer:
/// <list type="table">
/// <listheader><term>Answer</term><description>Refusal</description></listheader>
/// <item><term>not visible</term><description><c>projects.not-found</c>: for a project of another tenant, one out of
/// reach and a missing one alike.</description></item>
/// <item><term>closed</term><description><c>projects.closed</c>, for a request that adds to the project: nothing more
/// is recorded on it until it is reopened.</description></item>
/// <item><term>not allowed</term><description><c>projects.not-permitted</c>, naming the key.</description></item>
/// </list>
/// <para>
/// The module never learns whether the caller reached the project through its crew or its unit, and keeps no
/// copy of the project's state: the gate answers from the project as it is, so a project that is reopened takes
/// inspections again at once. The refusals are made by <see cref="InspectionRefusals"/>, under Projects' codes and
/// with this module's own texts.
/// </para>
/// <para>
/// A request that passes is noted with its project and the planned range the gate answered
/// (<see cref="GatedProject"/>, kept in <see cref="Checked{T}"/>), for its handler to act on: what is recorded
/// on, or listed, is the project the gate answered for, and the days an inspection may cover are the ones it
/// answered then.
/// </para>
/// <para>
/// A request about several projects (<see cref="InspectionsRequirement.OnProjectsInReach"/>) is refused for none
/// of them. The gate is asked once, about all of them, and answers for those the caller may see
/// (<see cref="GatedProjects"/>); the handler acts on those and passes over the rest, so the projects of one
/// request cost one question and not one each.
/// </para>
/// <para>
/// The gate answers each question on storage of its own, and what is noted is noted per request. So two queries
/// sent side by side within one scope are checked independently, though both go through the one instance the
/// scope has.
/// </para>
/// <para>
/// It fails closed: a case added to <see cref="InspectionsRequirement"/> without its branch here stops every
/// request that declares it, rather than letting it through.
/// </para>
/// </remarks>
/// <param name="projects">Projects' answer to "may the caller do this to that project".</param>
/// <param name="gated">Where the project a request passed the gate for is kept for its handler.</param>
/// <param name="gatedSeveral">Where what the gate answered about the projects of a request about several is kept for its handler.</param>
/// <param name="answers">Tenancy's answers about the current caller.</param>
public sealed class InspectionsAccessCheck(IProjectGate projects, Checked<GatedProject> gated, Checked<GatedProjects> gatedSeveral, SampleAnswers answers) : IAccessCheck
{
    /// <inheritdoc />
    public bool Decides(AccessRequirement requirement) => requirement is InspectionsRequirement;

    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">The caller is not who the request requires.</exception>
    /// <exception cref="InvalidOperationException">
    /// The requirement is one this check has no branch for; or it requires a tenant and the caller is system work
    /// outside any; or it is done by a seat and the caller is system work acting for none.
    /// </exception>
    public async ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(request);

        switch (requirement)
        {
            case InspectionsRequirement.OnProject required:
            {
                answers.RequireTenant();

                var answer = await projects.AskAsync(required.Project, required.Key, cancellationToken);
                Require(answer, required.Key, open: false);
                gated.KeepFor(request, new GatedProject(required.Project, answer.Planned));
                break;
            }

            case InspectionsRequirement.OnOpenProject required:
            {
                // The caller first, then whom it acts for, and only then the project: work that acts for no seat
                // is a mistake whatever the project is.
                var scope = answers.RequireTenant();
                if (required.BySeat)
                {
                    _ = ActingSeat.Of(scope);
                }

                var answer = await projects.AskAsync(required.Project, required.Key, cancellationToken);
                Require(answer, required.Key, open: true);
                gated.KeepFor(request, new GatedProject(required.Project, answer.Planned));
                break;
            }

            case InspectionsRequirement.OnProjectsInReach required:
            {
                answers.RequireTenant();

                // One question for all of them. Nothing is refused: the handler acts on the projects the gate
                // answered for, and the others are not there for this caller.
                gatedSeveral.KeepFor(request, new GatedProjects(await projects.AskAsync(required.Projects, required.Key, cancellationToken)));
                break;
            }

            default:
                throw new InvalidOperationException(
                    $"{request.GetType().Name} declares '{requirement}', which the Inspections module's access check has no branch for. "
                    + "A requirement nothing checks lets nobody through: give it a branch in InspectionsAccessCheck.");
        }
    }

    /// <summary>
    /// Refuses unless the answer lets the caller do what <paramref name="key"/> stands for; on an open project only,
    /// when <paramref name="open"/> says the request adds to it.
    /// </summary>
    private static void Require(ProjectAnswer answer, string key, bool open)
    {
        if (!answer.Visible)
        {
            throw InspectionRefusals.Of(InspectionRefusals.ProjectNotFound);
        }

        // Closed before not permitted: a closed project refuses every addition, whoever asks.
        if (open && answer.Closed)
        {
            throw InspectionRefusals.Of(InspectionRefusals.ProjectClosed);
        }

        if (!answer.Allowed)
        {
            throw InspectionRefusals.Of(InspectionRefusals.ProjectNotPermitted, ("Key", key));
        }
    }
}
