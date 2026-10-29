using DDDToolkit.Supporting.Membership;

namespace Examples.Tenancy.Tests.Projects.Crew;

/// <summary>
/// A project's crew as the crew manager changes it: the way to the members the Membership package's generator writes onto
/// the project, explicitly. Each change calls the project's guard, then the member list, then the hook that raises the
/// project's event, as it does when the manager makes it; a test makes it here with the periods and moments it chooses.
/// </summary>
internal static class ManagedCrew
{
    /// <summary>The project's crew, changed as the crew manager changes it.</summary>
    public static IMembershipResource<CrewMember, SeatId, ProjectRoleId> Managed(this Project project) => project;
}
