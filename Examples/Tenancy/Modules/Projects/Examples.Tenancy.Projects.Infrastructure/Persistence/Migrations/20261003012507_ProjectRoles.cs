using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Projects.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ProjectCrewRoleGrants",
                schema: "projects",
                table: "ProjectCrewRoleGrants");

            migrationBuilder.DropIndex(
                name: "IX_ProjectCrewRoleGrants_ProjectId_CrewMemberId_RoleId",
                schema: "projects",
                table: "ProjectCrewRoleGrants");

            migrationBuilder.DropColumn(
                name: "Id",
                schema: "projects",
                table: "ProjectCrewRoleGrants");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProjectCrewRoleGrants",
                schema: "projects",
                table: "ProjectCrewRoleGrants",
                columns: new[] { "ProjectId", "CrewMemberId", "RoleId" });

            migrationBuilder.CreateTable(
                name: "ProjectRoles",
                schema: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Keys = table.Column<string[]>(type: "text[]", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MadeFrom = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectRoles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRoles_TenantId_MadeFrom",
                schema: "projects",
                table: "ProjectRoles",
                columns: new[] { "TenantId", "MadeFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRoles_TenantId_Name",
                schema: "projects",
                table: "ProjectRoles",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectRoles",
                schema: "projects");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProjectCrewRoleGrants",
                schema: "projects",
                table: "ProjectCrewRoleGrants");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                schema: "projects",
                table: "ProjectCrewRoleGrants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProjectCrewRoleGrants",
                schema: "projects",
                table: "ProjectCrewRoleGrants",
                columns: new[] { "ProjectId", "CrewMemberId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCrewRoleGrants_ProjectId_CrewMemberId_RoleId",
                schema: "projects",
                table: "ProjectCrewRoleGrants",
                columns: new[] { "ProjectId", "CrewMemberId", "RoleId" },
                unique: true);
        }
    }
}
