using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Projects.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "projects");

            migrationBuilder.CreateTable(
                name: "Projects",
                schema: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OwnerSeatId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedByIdentity = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedByKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChangedBySeat = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByIdentity = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedBySeat = table.Column<Guid>(type: "uuid", nullable: true),
                    PlannedFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PlannedUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectsOutboxMessages",
                schema: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AggregateId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectsOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCrewMembers",
                schema: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeatId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCrewMembers", x => new { x.ProjectId, x.Id });
                    table.ForeignKey(
                        name: "FK_ProjectCrewMembers_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "projects",
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCrewRoleGrants",
                schema: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CrewMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GivenBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCrewRoleGrants", x => new { x.ProjectId, x.CrewMemberId, x.Id });
                    table.ForeignKey(
                        name: "FK_ProjectCrewRoleGrants_ProjectCrewMembers_ProjectId_CrewMemb~",
                        columns: x => new { x.ProjectId, x.CrewMemberId },
                        principalSchema: "projects",
                        principalTable: "ProjectCrewMembers",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCrewMembers_SeatId",
                schema: "projects",
                table: "ProjectCrewMembers",
                column: "SeatId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCrewRoleGrants_ProjectId_CrewMemberId_RoleId",
                schema: "projects",
                table: "ProjectCrewRoleGrants",
                columns: new[] { "ProjectId", "CrewMemberId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TenantId_Number",
                schema: "projects",
                table: "Projects",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_UnitId",
                schema: "projects",
                table: "Projects",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectsOutboxMessages_ProcessedAt",
                schema: "projects",
                table: "ProjectsOutboxMessages",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectCrewRoleGrants",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "ProjectsOutboxMessages",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "ProjectCrewMembers",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "Projects",
                schema: "projects");
        }
    }
}
