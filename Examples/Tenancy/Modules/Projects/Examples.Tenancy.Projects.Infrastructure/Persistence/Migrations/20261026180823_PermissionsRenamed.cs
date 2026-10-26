using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Projects.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermissionsRenamed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Keys",
                schema: "projects",
                table: "ProjectRoles",
                newName: "Permissions");

            // The event a project role's permissions changing raises is stored under its new name from now on: a row
            // the outbox has not delivered yet is given it, so it is still delivered.
            migrationBuilder.Sql("""UPDATE "projects"."ProjectsOutboxMessages" SET "EventName" = 'projects.project-role-permissions-changed' WHERE "EventName" = 'projects.project-role-keys-changed';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Permissions",
                schema: "projects",
                table: "ProjectRoles",
                newName: "Keys");

            migrationBuilder.Sql("""UPDATE "projects"."ProjectsOutboxMessages" SET "EventName" = 'projects.project-role-keys-changed' WHERE "EventName" = 'projects.project-role-permissions-changed';""");
        }
    }
}
