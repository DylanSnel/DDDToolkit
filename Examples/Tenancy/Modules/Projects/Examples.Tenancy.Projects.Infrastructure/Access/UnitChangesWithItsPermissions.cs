using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using Examples.Tenancy.Projects.Application.Access;
using Examples.Tenancy.Projects.Contracts.Permissions;
using Examples.Tenancy.Projects.Contracts.RowAccess;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects;
using Examples.Tenancy.Projects.Infrastructure.Access;
using Microsoft.EntityFrameworkCore;

// Offered, not written: Projects is a module, so this is the application's own SQL, which the program that exports
// lists. The offer is what tells that program, with a warning (DDD00069), when the line is missing there.
[assembly: RowAccessContribution(typeof(UnitChangesWithItsPermissions))]

namespace Examples.Tenancy.Projects.Infrastructure.Access;

/// <summary>
/// What Postgres holds about a project beyond the policy that lets a seat change it: the unit it is at changes only
/// with the permissions the application asks for a move. A trigger on the projects' table, written into the exported
/// access file of this module.
/// </summary>
/// <remarks>
/// The policy for changing a project is coarser than the application on purpose: a row knows no command, so it
/// asks whether the seat holds any permission that changes a project (<c>SeatsChangeTheProjectsTheyWorkOn</c>). The
/// columns whose commands ask a stricter permission are held closer, the name, the planned days and the state by column
/// rules (<see cref="NameAndPlanChangeWithTheEditPermission"/>, <see cref="StateChangesWithTheClosePermission"/>). The unit is
/// held closer still, because the unit decides who reaches the project at all: every organization role reaches a
/// project through its unit. A statement that went round the application could put a project at a unit of no
/// tenant, where no rule of the application finds it again, or move it to wherever the seat happens to hold some
/// permission. So the unit changes only to a unit of the project's own tenant, for a seat that may edit the project as it
/// was (<see cref="ProjectPermissionNames.Edit"/>, however it holds it) and may open projects at the unit it arrives at
/// (<see cref="ProjectPermissionNames.Open"/>): what <c>MoveProjectToUnit</c> asks.
/// <para>
/// The owner is held the same way, by the Membership package's lock: the projects' rules name the permission that names
/// an owner (<see cref="ProjectMembership"/>), and the package writes a trigger of its own from it. Only a signed-in
/// user's statement is held. System work in the tenant and the tables' owner act for the application, which has
/// checked already. The function runs as its caller and asks what the policies ask: Tenancy's functions and the
/// projects' own, so it reads no table of Tenancy's.
/// </para>
/// <para>
/// It is an access guard, holding who may, so it refuses the way the toolkit's access guards do, with
/// <see cref="RowAccessModel.Refusal"/>: SQLSTATE <c>42501</c>, the trigger's name, and the toolkit's hint. A save
/// it refuses, from a handler whose caller lost a permission after the check say, is then answered as a policy's refusal
/// is, <c>access.refused</c>, where a trigger that raised without the hint would end the request as a failure of
/// the server.
/// </para>
/// <para>
/// It lives beside the module's row rules because it is one more of them, one of the two the module writes as SQL of
/// its own, beside <see cref="CrewSeatsOfTheProjectsTenant"/>. A column rule asks one question of the row as it was and the same question of the row as it is about to
/// be; this one asks two different ones, the permission to edit where the project was and the permission to open where it goes,
/// so it is no column rule. It is the application's own SQL, not a package's: a module offers its contribution, and
/// what the module offers is written where the project that exports lists it, with
/// <c>[assembly: UseRowAccessContribution(typeof(UnitChangesWithItsPermissions))]</c>.
/// </para>
/// </remarks>
public sealed class UnitChangesWithItsPermissions : IRowAccessContribution
{
    /// <summary>The trigger function, in the module's schema.</summary>
    public const string Function = "unit_is_held";

    /// <summary>The trigger on the projects' table.</summary>
    public const string Trigger = "projects_unit_is_held";

    /// <summary>What a statement is told that would put a project at a unit that is none of its tenant's.</summary>
    public const string UnitOfAnotherTenant = "A project is at a unit of its own tenant.";

    /// <summary>What a statement is told that would move a project without the permissions for it.</summary>
    public const string MovedWithoutThePermissions = "A project is moved by a seat that may edit it and may open projects at the unit it moves to.";

    /// <summary>The trigger function and the trigger this one was written as before, which the file takes away.</summary>
    private const string Before = "unit_and_owner_are_held";

    /// <inheritdoc />
    /// <remarks>It defines no function a rule could ask, so the owner only names it.</remarks>
    public string Owner => "projects-unit";

    /// <inheritdoc />
    public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(export);

        if (context.Model.FindEntityType(typeof(Project)) is not { } projects)
        {
            return null;
        }

        var table = RowAccessModel.Table(projects);
        var schema = RowAccessModel.Schema(context.Model);
        var function = $"{schema}.{Function}";
        string Column(string property) => RowAccessModel.Column(projects, property);
        var (id, tenant, unit) = (Column(nameof(Project.Id)), Column(nameof(Project.TenantId)), Column(nameof(Project.UnitId)));

        // What the application's own checks ask, as the policies ask it: Tenancy's set of units where the calling
        // seat holds a permission, and the projects' set of projects it holds one on, however it holds it.
        string HeldAt(string permission, string where) => $"{where} = ANY (ARRAY(SELECT {{fn:tenancy/units_where_i_hold_permission}}({RowAccessModel.Literal(permission)})))";
        string HeldOn(string permission, string project) => $"{project} = ANY (ARRAY(SELECT {{fn:{ProjectsWhereIHold.Name}}}({RowAccessModel.Literal(permission)})))";
        // Refused as the toolkit's own access guards refuse, with its hint and the trigger's name: a save the trigger
        // refuses is access.refused to the caller, a 403, and the warning in the log names the trigger.
        string Refusal(string message) => RowAccessModel.Refusal(Trigger, message);

        return new RowAccessContributionResult(
            [],
            [],
            [
                // The trigger this was before, which held the owner as well: the projects' rules hold the owner now.
                $"DROP TRIGGER IF EXISTS projects_{Before} ON {table}",
                $"DROP FUNCTION IF EXISTS {schema}.{Before}()",
                $"CREATE OR REPLACE FUNCTION {function}() RETURNS trigger\n" +
                "    LANGUAGE plpgsql SET search_path = '' AS $body$\n" +
                "BEGIN\n" +
                "    -- Only a signed-in user's statement is held: system work and the tables' owner act for the application.\n" +
                $"    IF CURRENT_USER <> {RowAccessModel.Literal(export.Roles.Resolve(RowAccessRoles.User))} THEN\n" +
                "        RETURN NEW;\n" +
                "    END IF;\n" +
                $"    IF NOT EXISTS (SELECT 1 FROM {{fn:tenancy/tenant_units}}() u WHERE u.\"Id\" = NEW.{unit} AND u.\"TenantId\" = NEW.{tenant}) THEN\n" +
                $"        {Refusal(UnitOfAnotherTenant)}\n" +
                "    END IF;\n" +
                $"    IF NOT ({HeldOn(ProjectPermissionNames.Edit, $"OLD.{id}")}) OR NOT ({HeldAt(ProjectPermissionNames.Open, $"NEW.{unit}")}) THEN\n" +
                $"        {Refusal(MovedWithoutThePermissions)}\n" +
                "    END IF;\n" +
                "    RETURN NEW;\n" +
                "END\n" +
                "$body$",
                $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC",
                $"DROP TRIGGER IF EXISTS {Trigger} ON {table}",
                $"CREATE TRIGGER {Trigger} BEFORE UPDATE OF {unit} ON {table}\n" +
                $"    FOR EACH ROW WHEN (OLD.{unit} IS DISTINCT FROM NEW.{unit})\n" +
                $"    EXECUTE FUNCTION {function}()",
            ]);
    }
}
