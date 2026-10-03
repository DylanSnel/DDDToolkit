using Examples.Tenancy.Projects.Api.Overview.GraphQL;
using Examples.Tenancy.Projects.Application.Overview;
using Examples.Tenancy.Projects.Application.Crew.Commands;
using HotChocolate;
using HotChocolate.Types.Relay;
using Mediator;

namespace Examples.Tenancy.Projects.Api.Crew.GraphQL;

/// <summary>
/// What changes a project's crew. A mutation sends the command its route sends, then reads the project and
/// answers that, with its new version; a refusal is not caught here, and arrives in the payload's <c>errors</c>.
/// Each takes the version its caller read as <c>expectedVersion</c>, and is refused with a conflict when the
/// project has changed since; without it the last change wins.
/// </summary>
internal static class CrewMutations
{
    /// <summary>Puts a seat on the crew, with no project role unless one is named, until <paramref name="until"/> or for good.</summary>
    [Mutation]
    public static async Task<ProjectOverview?> CrewMemberAddAsync(
        [ID("Project")] ProjectId id,
        SeatId seatId,
        ProjectRoleId? roleId,
        DateTimeOffset? until,
        long? expectedVersion,
        [Service] ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new AddCrewMember(id, seatId, roleId, until, expectedVersion), cancellationToken);
        return await ChangedProject.ReadAsync(id, sender, cancellationToken);
    }

    /// <summary>Gives a crew member a project role, next to the roles it holds already, for a period of its own.</summary>
    [Mutation]
    public static async Task<ProjectOverview?> CrewRoleGiveAsync(
        [ID("Project")] ProjectId id,
        SeatId seatId,
        ProjectRoleId roleId,
        DateTimeOffset? until,
        long? expectedVersion,
        [Service] ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new GiveCrewRole(id, seatId, roleId, until, expectedVersion), cancellationToken);
        return await ChangedProject.ReadAsync(id, sender, cancellationToken);
    }

    /// <summary>Takes a project role from a member, who stays on the crew.</summary>
    [Mutation]
    public static async Task<ProjectOverview?> CrewRoleTakeAsync(
        [ID("Project")] ProjectId id,
        SeatId seatId,
        ProjectRoleId roleId,
        long? expectedVersion,
        [Service] ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new TakeCrewRole(id, seatId, roleId, expectedVersion), cancellationToken);
        return await ChangedProject.ReadAsync(id, sender, cancellationToken);
    }

    /// <summary>
    /// Takes a seat off the crew, with every role it held there. Whoever removes themselves may no longer see the
    /// project: the answer is then nothing, and no error, since the command succeeded.
    /// </summary>
    [Mutation]
    public static async Task<ProjectOverview?> CrewMemberRemoveAsync(
        [ID("Project")] ProjectId id,
        SeatId seatId,
        long? expectedVersion,
        [Service] ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new RemoveCrewMember(id, seatId, expectedVersion), cancellationToken);
        return await ChangedProject.ReadAsync(id, sender, cancellationToken);
    }
}
