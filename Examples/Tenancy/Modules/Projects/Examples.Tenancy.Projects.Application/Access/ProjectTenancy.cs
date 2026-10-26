using DDDToolkit.Supporting.Tenancy;

namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// What Projects' commands need to know of the tenant's organization before they put a project somewhere: whether
/// a unit is active.
/// </summary>
/// <remarks>
/// Not a question of who may do what, which the access checks answer, but of what the tenant allows at all,
/// whoever asks, and Tenancy's to answer: it is put to Tenancy's questions (<see cref="ProjectAccess.Questions"/>),
/// over the rows this module reads next to its projects, in one statement on the reading it is given. What a
/// command asks about a seat and a project role, whether a seat may go on a crew, whether a role is one of the
/// tenant's project roles in use and which is the crew lead's, the Membership package's admission answers
/// (<c>MemberAdmission</c>), from the projects' rules.
/// </remarks>
/// <param name="access">Tenancy's questions, as this module asks them.</param>
public sealed class ProjectTenancy(ProjectAccess access)
{
    /// <summary>Requires <paramref name="unit"/> to be an active unit of the tenant. Nothing new goes to an archived unit; what is already there stays.</summary>
    /// <param name="reading">The reading of the command that asks.</param>
    /// <param name="unit">The unit.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="Exceptions.RefusalException"><c>projects.unit-not-active</c>, with the <c>Unit</c>.</exception>
    public async Task RequireActiveUnitAsync(IProjectReading reading, OrganizationUnitId unit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var active = access.Questions(reading).Units().Where(row => row.Id == unit && row.Status == UnitStatus.Active);
        if (!await reading.Queries.AnyAsync(active, cancellationToken))
        {
            throw ProjectRefusals.Refuse(ProjectRefusals.UnitNotActive, ("Unit", unit));
        }
    }
}
