using Examples.Tenancy.Projects.Application.Crew.Commands;
using Examples.Tenancy.Projects.Application.Lifecycle.Commands;
using Examples.Tenancy.Projects.Application.Ownership.Commands;

namespace Examples.Tenancy.Projects.Application.Overview;

/// <summary>
/// What the caller may do to one project, as the commands would decide it, for a screen to show which actions
/// are open. On a closed project, which refuses every change until it is reopened, only <see cref="Reopen"/> can
/// be <see langword="true"/>; on an open one, <see cref="Reopen"/> is <see langword="false"/>.
/// </summary>
/// <param name="Rename">Holds the key of <see cref="ChangeProjectName"/> on it.</param>
/// <param name="Plan">Holds the key of <see cref="PlanProject"/> on it.</param>
/// <param name="Move">Holds the key of <see cref="MoveProjectToUnit"/> on it, and its destination key at some unit to move it to.</param>
/// <param name="Close">Holds the key of <see cref="CloseProject"/> on it, and it is open.</param>
/// <param name="Reopen">Holds the key of <see cref="ReopenProject"/> on it, and it is closed.</param>
/// <param name="ManageCrew">Holds the key of <see cref="AddCrewMember"/> on it.</param>
/// <param name="ChangeOwner">Holds the key of <see cref="ChangeProjectOwner"/> at its unit: the organization names an owner, never the crew.</param>
public sealed record ProjectAbilities(bool Rename, bool Plan, bool Move, bool Close, bool Reopen, bool ManageCrew, bool ChangeOwner)
{
    /// <summary>
    /// The keys the abilities are decided from: the <c>RequiredKey</c> of each command an ability stands for, each
    /// once. A command whose key changes takes its ability with it.
    /// </summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        .. new[]
        {
            ChangeProjectName.RequiredKey,
            PlanProject.RequiredKey,
            MoveProjectToUnit.RequiredKey,
            CloseProject.RequiredKey,
            ReopenProject.RequiredKey,
            AddCrewMember.RequiredKey,
            ChangeProjectOwner.RequiredKey,
        }.Distinct(StringComparer.Ordinal),
    ];

    /// <summary>
    /// What a caller may do to a project in <paramref name="state"/> on which it holds <paramref name="held"/>:
    /// the one place that decides it, for a single project and for every row of a list alike.
    /// </summary>
    /// <param name="state">Open or closed.</param>
    /// <param name="held">Those of <see cref="Keys"/> the caller holds on the project.</param>
    /// <param name="mayOpenSomewhere">
    /// Whether the caller holds the destination key of <see cref="MoveProjectToUnit"/> at any unit at all: moving
    /// needs somewhere to move to.
    /// </param>
    public static ProjectAbilities For(ProjectState state, IReadOnlySet<string> held, bool mayOpenSomewhere)
    {
        ArgumentNullException.ThrowIfNull(held);

        return state == ProjectState.Closed
            ? new ProjectAbilities(Rename: false, Plan: false, Move: false, Close: false, Reopen: held.Contains(ReopenProject.RequiredKey), ManageCrew: false, ChangeOwner: false)
            : new ProjectAbilities(
                Rename: held.Contains(ChangeProjectName.RequiredKey),
                Plan: held.Contains(PlanProject.RequiredKey),
                Move: held.Contains(MoveProjectToUnit.RequiredKey) && mayOpenSomewhere,
                Close: held.Contains(CloseProject.RequiredKey),
                Reopen: false,
                ManageCrew: held.Contains(AddCrewMember.RequiredKey),
                ChangeOwner: held.Contains(ChangeProjectOwner.RequiredKey));
    }
}
