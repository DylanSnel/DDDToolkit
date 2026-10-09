namespace Examples.Tenancy.Inspections.Application.Access;

/// <summary>
/// Decides the requirements that are Inspections' own (<see cref="InspectionsRequirement"/>) before a handler of
/// this module runs: registered for <see cref="IInspectionsRequest"/>, next to Tenancy's check for the case that
/// is Tenancy's, and asked by the module's access behavior.
/// </summary>
/// <remarks>
/// Who may do what to a project is Projects' to answer, so every case about one project asks its gate,
/// <see cref="IProjectGate"/>, and maps the answer (the case about several asks no gate: its handler does, once):
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
/// It keeps nothing for the handler. A request that passes is handled on the project it names, which is the one
/// the gate answered for; what the handler needs of the project besides, the days it is planned for, it asks the
/// gate itself, as it is when the inspection is recorded, and holds that answer to this check's rule
/// (<see cref="Require"/>): the database writes an inspection for a seat that holds the key and knows nothing of a
/// closed project, so the handler says that too.
/// </para>
/// <para>
/// A request about several projects (<see cref="InspectionsRequirement.OnProjectsInReach"/>) is refused for none
/// of them, so the check asks only that the caller works in a tenant: the handler asks the gate, once, about all
/// of them, and acts on those it answers for, as a query that declares <c>MemberAccess.SeenWith</c> is filtered
/// by its own statement. The projects of one request cost one question, not one each.
/// </para>
/// <para>
/// The gate answers each question on storage of its own. So two queries sent side by side within one scope are
/// checked independently, though both go through the one instance the scope has.
/// </para>
/// <para>
/// It fails closed: a case added to <see cref="InspectionsRequirement"/> without its branch here stops every
/// request that declares it, rather than letting it through.
/// </para>
/// </remarks>
/// <param name="projects">Projects' answer to "may the caller do this to that project".</param>
/// <param name="answers">Tenancy's answers about the current caller.</param>
public sealed class InspectionsAccessCheck(IProjectGate projects, SampleAnswers answers) : IAccessCheck
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
                break;
            }

            case InspectionsRequirement.OnOpenProject required:
            {
                // The caller first, then whom it acts for, and only then the project: work that acts for no seat
                // is a mistake whatever the project is.
                var scope = answers.RequireTenant();
                if (required.BySeat)
                {
                    _ = ActingSeat.From(scope);
                }

                var answer = await projects.AskAsync(required.Project, required.Key, cancellationToken);
                Require(answer, required.Key, open: true);
                break;
            }

            case InspectionsRequirement.OnProjectsInReach:
                // Nothing is refused for a project: the handler asks the gate about all of them at once, and acts on
                // those it answers for. The others are not there for this caller.
                answers.RequireTenant();
                break;

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
    /// <remarks>
    /// A handler that asks the gate again holds its fresh answer to this same rule, so a handler called directly
    /// refuses what the check would have, and a project closed since the check takes nothing.
    /// </remarks>
    /// <param name="answer">The gate's answer about the caller and the project.</param>
    /// <param name="key">The key the request requires on the project.</param>
    /// <param name="open">Whether the request adds to the project, which a closed project refuses.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.not-found</c>, <c>projects.closed</c> or <c>projects.not-permitted</c>, in that order.
    /// </exception>
    internal static void Require(ProjectAnswer answer, string key, bool open)
    {
        if (!answer.Visible)
        {
            throw InspectionRefusals.Refuse(InspectionRefusals.ProjectNotFound);
        }

        // Closed before not permitted: a closed project refuses every addition, whoever asks.
        if (open && answer.Closed)
        {
            throw InspectionRefusals.Refuse(InspectionRefusals.ProjectClosed);
        }

        if (!answer.Allowed)
        {
            throw InspectionRefusals.Refuse(InspectionRefusals.ProjectNotPermitted, ("Key", key));
        }
    }
}
