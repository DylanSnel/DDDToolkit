using Examples.Tenancy.Projects.Application.Crew.Commands;
using Examples.Tenancy.Projects.Application.Lifecycle.Commands;
using Examples.Tenancy.Projects.Application.Ownership.Commands;

namespace Examples.Tenancy.Projects.Application.Overview;

/// <summary>
/// What the caller may do to one project, as the commands would decide it, for a screen to show which actions
/// are open. On a closed project, which refuses every change until it is reopened, only <see cref="Reopen"/> can
/// be <see langword="true"/>; on an open one, <see cref="Reopen"/> is <see langword="false"/>.
/// </summary>
/// <param name="Rename">Holds the permission of <see cref="ChangeProjectName"/> on it.</param>
/// <param name="Plan">Holds the permission of <see cref="PlanProject"/> on it.</param>
/// <param name="Move">Holds the permission of <see cref="MoveProjectToUnit"/> on it, and its destination permission at some unit to move it to.</param>
/// <param name="Close">Holds the permission of <see cref="CloseProject"/> on it, and it is open.</param>
/// <param name="Reopen">Holds the permission of <see cref="ReopenProject"/> on it, and it is closed.</param>
/// <param name="ManageCrew">Holds the permission of <see cref="AddCrewMember"/> on it.</param>
/// <param name="ChangeOwner">Holds the permission of <see cref="ChangeProjectOwner"/> at its unit: the organization names an owner, never the crew.</param>
public sealed record ProjectAbilities(bool Rename, bool Plan, bool Move, bool Close, bool Reopen, bool ManageCrew, bool ChangeOwner)
{
    /// <summary>
    /// The permissions that <see cref="For"/> reads, each once. Each one is the permission that the marker of a command
    /// names: <see cref="ProjectPermissionNames.Edit"/> for renaming, planning and moving, <see cref="ProjectPermissionNames.Close"/>
    /// for closing and reopening, <see cref="ProjectPermissionNames.ManageCrew"/> for the crew, and
    /// <see cref="ProjectPermissionNames.ChangeOwner"/> for the owner.
    /// </summary>
    public static IReadOnlyList<string> Permissions { get; } =
    [
        ProjectPermissionNames.Edit,
        ProjectPermissionNames.Close,
        ProjectPermissionNames.ManageCrew,
        ProjectPermissionNames.ChangeOwner,
    ];

    /// <summary>
    /// What a caller may do to a project in <paramref name="state"/> on which it holds <paramref name="held"/>:
    /// the one place that decides it, for a single project and for every row of a list alike.
    /// </summary>
    /// <param name="state">Open or closed.</param>
    /// <param name="held">Those of <see cref="Permissions"/> the caller holds on the project.</param>
    /// <param name="mayOpenSomewhere">
    /// Whether the caller holds <see cref="ProjectPermissionNames.Open"/> at any unit. <see cref="MoveProjectToUnit"/>
    /// requires that permission at the unit the project moves to, so moving needs such a unit.
    /// </param>
    public static ProjectAbilities For(ProjectState state, IReadOnlySet<string> held, bool mayOpenSomewhere)
    {
        ArgumentNullException.ThrowIfNull(held);

        return state == ProjectState.Closed
            ? new ProjectAbilities(Rename: false, Plan: false, Move: false, Close: false, Reopen: held.Contains(ProjectPermissionNames.Close), ManageCrew: false, ChangeOwner: false)
            : new ProjectAbilities(
                Rename: held.Contains(ProjectPermissionNames.Edit),
                Plan: held.Contains(ProjectPermissionNames.Edit),
                Move: held.Contains(ProjectPermissionNames.Edit) && mayOpenSomewhere,
                Close: held.Contains(ProjectPermissionNames.Close),
                Reopen: false,
                ManageCrew: held.Contains(ProjectPermissionNames.ManageCrew),
                ChangeOwner: held.Contains(ProjectPermissionNames.ChangeOwner));
    }
}
