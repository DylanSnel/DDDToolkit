using Examples.Tenancy.Projects.Api.Overview.GraphQL;
using Examples.Tenancy.Projects.Application.Overview;
using Examples.Tenancy.Projects.Application.Lifecycle.Commands;
using HotChocolate;
using HotChocolate.Types.Relay;
using Mediator;

namespace Examples.Tenancy.Projects.Api.Lifecycle.GraphQL;

/// <summary>
/// What opens a project and changes its life. A mutation sends the command its route sends, then reads the
/// project and answers that, with its new version; a refusal is not caught here, and arrives in the payload's
/// <c>errors</c>. Each change takes the version its caller read as <c>expectedVersion</c>, and is refused with a
/// conflict when the project has changed since; without it the last change wins.
/// </summary>
/// <remarks>
/// No argument is a command: what a client may say is what a mutation has an argument for, so a field a command
/// keeps for the application's own use, such as a project's id for an import, stays out of reach. A range of
/// days is one argument, the value object the modules share: the schema has it as the input <c>DateRangeInput</c>
/// beside the type <c>DateRange</c> the answers show.
/// </remarks>
internal static class LifecycleMutations
{
    /// <summary>
    /// Opens a project at a unit. The owner is the caller unless <paramref name="ownerSeat"/> names somebody else.
    /// <paramref name="planned"/> plans it for a range of days, a first and a last; left out, it is not planned.
    /// </summary>
    [Mutation]
    public static async Task<ProjectOverview?> ProjectOpenAtUnitAsync(
        string number,
        string name,
        OrganizationUnitId unitId,
        SeatId? ownerSeat,
        DateRange? planned,
        [Service] ISender sender,
        CancellationToken cancellationToken)
    {
        var id = await sender.Send(new OpenProject(number, name, unitId, ownerSeat, Planned: planned), cancellationToken);
        return await ChangedProject.ReadAsync(id, sender, cancellationToken);
    }

    /// <summary>Renames a project.</summary>
    [Mutation]
    public static async Task<ProjectOverview?> ProjectRenameAsync([ID("Project")] ProjectId id, string name, long? expectedVersion, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new ChangeProjectName(id, name, expectedVersion), cancellationToken);
        return await ChangedProject.ReadAsync(id, sender, cancellationToken);
    }

    /// <summary>
    /// Plans a project for a range of days, <paramref name="planned"/>, its first day and its last; left out, its
    /// planned range is taken away.
    /// </summary>
    [Mutation]
    public static async Task<ProjectOverview?> ProjectPlanAsync([ID("Project")] ProjectId id, DateRange? planned, long? expectedVersion, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new PlanProject(id, planned, expectedVersion), cancellationToken);
        return await ChangedProject.ReadAsync(id, sender, cancellationToken);
    }

    /// <summary>
    /// Moves a project to another unit. Whoever sees it only through the unit it leaves may no longer see it:
    /// the answer is then nothing, and no error, since the command succeeded.
    /// </summary>
    [Mutation]
    public static async Task<ProjectOverview?> ProjectMoveAsync([ID("Project")] ProjectId id, OrganizationUnitId unitId, long? expectedVersion, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new MoveProjectToUnit(id, unitId, expectedVersion), cancellationToken);
        return await ChangedProject.ReadAsync(id, sender, cancellationToken);
    }

    /// <summary>Closes a project: nothing about it changes until it is reopened.</summary>
    [Mutation]
    public static async Task<ProjectOverview?> ProjectCloseAsync([ID("Project")] ProjectId id, long? expectedVersion, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new CloseProject(id, expectedVersion), cancellationToken);
        return await ChangedProject.ReadAsync(id, sender, cancellationToken);
    }

    /// <summary>Reopens a closed project. Whoever may close a project may reopen it.</summary>
    [Mutation]
    public static async Task<ProjectOverview?> ProjectReopenAsync([ID("Project")] ProjectId id, long? expectedVersion, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new ReopenProject(id, expectedVersion), cancellationToken);
        return await ChangedProject.ReadAsync(id, sender, cancellationToken);
    }
}
