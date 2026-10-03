using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.EntityFramework;
using Examples.Tenancy.Projects.Infrastructure.Access;
using Microsoft.EntityFrameworkCore;

[assembly: RowAccessContribution(typeof(CrewSeatsOfTheProjectsTenant))]

namespace Examples.Tenancy.Projects.Infrastructure.Access;

/// <summary>
/// What Postgres holds about a crew beyond the Membership package's lock: a seat put on a project's crew is a seat
/// of the project's tenant. A restrictive policy on the crew's table, written into the exported access file of this
/// module.
/// </summary>
/// <remarks>
/// The lock holds a crew's rows to the keys that change the crew, and Tenancy's policy keeps every row to a project
/// of the caller's tenant; neither asks whose the seat is. In C# the crew takes only an active seat of the tenant
/// (<c>MemberAdmission.RequireMemberAsync</c>). A statement that went round the application could put a seat of
/// another tenant on the crew, and that seat, acting in its own tenant, would then be answered the project by the
/// projects' functions and could record on it. So a row is added only for a seat the caller reads in the project's
/// own tenant, asked of Tenancy's function as the caller, under Tenancy's rules on its seats. A row's seat is fixed
/// once it is there, so only adding one is asked. It is the seat's side of what the package holds of a role on a
/// crew: a role the caller sees.
/// <para>
/// Only a signed-in user's statement is held: system work in the tenant is kept to its tenant by Tenancy's own
/// policies. The project that exports uses it with
/// <c>[assembly: UseRowAccessContribution(typeof(CrewSeatsOfTheProjectsTenant))]</c>.
/// </para>
/// </remarks>
public sealed class CrewSeatsOfTheProjectsTenant : IRowAccessContribution
{
    /// <summary>What the policy is about, which its name starts with.</summary>
    public const string Policy = "Crews hold seats of their tenant";

    /// <inheritdoc />
    /// <remarks>It defines no function a rule could ask, so the owner only names it.</remarks>
    public string Owner => "projects-crew";

    /// <inheritdoc />
    public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(export);

        if (context.Model.FindEntityType(typeof(Project)) is not { } projects || MembershipModel.MembersOf(projects) is not { } crew)
        {
            return null;
        }

        var members = crew.Members;
        var row = RowAccessModel.Table(members);
        var check = $"EXISTS (SELECT 1 FROM {{fn:tenancy/tenant_seats}}() s JOIN {RowAccessModel.Table(projects)} r ON r.{RowAccessModel.Column(projects, nameof(Project.TenantId))} = s.\"TenantId\" "
                    + $"WHERE s.\"Id\" = {row}.{RowAccessModel.Column(members, "MemberId")} AND r.{RowAccessModel.Column(projects, nameof(Project.Id))} = {row}.{RowAccessModel.Column(members, "ProjectId")})";

        return new RowAccessContributionResult(
            [],
            [new ContributedPolicy(members, Policy, "INSERT", RowAccessRoles.User, null, check, Restrictive: true)],
            []);
    }
}
