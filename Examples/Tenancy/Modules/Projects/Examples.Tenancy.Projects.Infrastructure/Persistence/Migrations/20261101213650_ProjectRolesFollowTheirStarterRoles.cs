using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Projects.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectRolesFollowTheirStarterRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MadeFrom",
                schema: "projects",
                table: "ProjectRoles",
                newName: "FromStarterRole");

            migrationBuilder.RenameIndex(
                name: "IX_ProjectRoles_TenantId_MadeFrom",
                schema: "projects",
                table: "ProjectRoles",
                newName: "IX_ProjectRoles_TenantId_FromStarterRole");

            migrationBuilder.AddColumn<string[]>(
                name: "PermissionsFromStarterRole",
                schema: "projects",
                table: "ProjectRoles",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PermissionsFromStarterRole",
                schema: "projects",
                table: "ProjectRoles");

            migrationBuilder.RenameColumn(
                name: "FromStarterRole",
                schema: "projects",
                table: "ProjectRoles",
                newName: "MadeFrom");

            migrationBuilder.RenameIndex(
                name: "IX_ProjectRoles_TenantId_FromStarterRole",
                schema: "projects",
                table: "ProjectRoles",
                newName: "IX_ProjectRoles_TenantId_MadeFrom");
        }
    }
}
