using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Inspections.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inspections");

            migrationBuilder.CreateTable(
                name: "Inspections",
                schema: "inspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedByIdentity = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedByKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChangedBySeat = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByIdentity = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedBySeat = table.Column<Guid>(type: "uuid", nullable: true),
                    DaysFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    DaysUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inspections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InspectionsOutboxMessages",
                schema: "inspections",
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
                    table.PrimaryKey("PK_InspectionsOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_ProjectId",
                schema: "inspections",
                table: "Inspections",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionsOutboxMessages_ProcessedAt",
                schema: "inspections",
                table: "InspectionsOutboxMessages",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Inspections",
                schema: "inspections");

            migrationBuilder.DropTable(
                name: "InspectionsOutboxMessages",
                schema: "inspections");
        }
    }
}
