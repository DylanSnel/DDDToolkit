using Examples.Tenancy.Projects.Api.Overview.GraphQL;
using Examples.Tenancy.Projects.Application.Overview;
using Examples.Tenancy.Projects.Application.Ownership.Commands;
using HotChocolate;
using HotChocolate.Types.Relay;
using Mediator;

namespace Examples.Tenancy.Projects.Api.Ownership.GraphQL;

/// <summary>
/// What names a project's owner. The mutation sends the command its route sends, then reads the project and
/// answers that; a refusal arrives in the payload's <c>errors</c>.
/// </summary>
internal static class OwnershipMutations
{
    /// <summary>Names another seat the owner. Like every change to a project it takes the version its caller read, as <c>expectedVersion</c>.</summary>
    [Mutation]
    public static async Task<ProjectOverview?> ProjectOwnerChangeAsync([ID("Project")] ProjectId id, SeatId seatId, long? expectedVersion, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new ChangeProjectOwner(id, seatId, expectedVersion), cancellationToken);
        return await ChangedProject.ReadAsync(id, sender, cancellationToken);
    }
}
