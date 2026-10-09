using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Tenants.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenancy");

            migrationBuilder.CreateTable(
                name: "Organizations",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationUnitPaths",
                schema: "tenancy",
                columns: table => new
                {
                    AncestorId = table.Column<Guid>(type: "uuid", nullable: false),
                    DescendantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Distance = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationUnitPaths", x => new { x.AncestorId, x.DescendantId });
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Use = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FromPack = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Keys = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeatRights",
                schema: "tenancy",
                columns: table => new
                {
                    SeatId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatRights", x => new { x.SeatId, x.UnitId, x.RoleId, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "Seats",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobTitle = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Identity = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenancyAccessRevisions",
                schema: "tenancy",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenancyAccessRevisions", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "TenancyEventLog",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AggregateId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ActedByKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActedById = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenancyEventLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenancyOutboxMessages",
                schema: "tenancy",
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
                    table.PrimaryKey("PK_TenancyOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDemo = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Shape = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationUnits",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CostCentre = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationUnits_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeatPlacements",
                schema: "tenancy",
                columns: table => new
                {
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeatId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    PlacedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlacedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatPlacements", x => new { x.SeatId, x.UnitId });
                    table.ForeignKey(
                        name: "FK_SeatPlacements_Seats_SeatId",
                        column: x => x.SeatId,
                        principalSchema: "tenancy",
                        principalTable: "Seats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeatRoleGrants",
                schema: "tenancy",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeatId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatRoleGrants", x => new { x.SeatId, x.UnitId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_SeatRoleGrants_SeatPlacements_SeatId_UnitId",
                        columns: x => new { x.SeatId, x.UnitId },
                        principalSchema: "tenancy",
                        principalTable: "SeatPlacements",
                        principalColumns: new[] { "SeatId", "UnitId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnitPaths_DescendantId",
                schema: "tenancy",
                table: "OrganizationUnitPaths",
                column: "DescendantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnitPaths_TenantId",
                schema: "tenancy",
                table: "OrganizationUnitPaths",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnits_ParentId",
                schema: "tenancy",
                table: "OrganizationUnits",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnits_TenantId",
                schema: "tenancy",
                table: "OrganizationUnits",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnits_TenantId_WhereRoot",
                schema: "tenancy",
                table: "OrganizationUnits",
                column: "TenantId",
                unique: true,
                filter: "\"ParentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_NormalizedName",
                schema: "tenancy",
                table: "Roles",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeatPlacements_SeatId_WherePrimary",
                schema: "tenancy",
                table: "SeatPlacements",
                column: "SeatId",
                unique: true,
                filter: "\"IsPrimary\"");

            migrationBuilder.CreateIndex(
                name: "IX_SeatRights_RoleId",
                schema: "tenancy",
                table: "SeatRights",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_SeatRights_SeatId_Key",
                schema: "tenancy",
                table: "SeatRights",
                columns: new[] { "SeatId", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_SeatRights_TenantId",
                schema: "tenancy",
                table: "SeatRights",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SeatRoleGrants_RoleId",
                schema: "tenancy",
                table: "SeatRoleGrants",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Seats_Identity_TenantId",
                schema: "tenancy",
                table: "Seats",
                columns: new[] { "Identity", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seats_TenantId",
                schema: "tenancy",
                table: "Seats",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenancyEventLog_RecordedAt",
                schema: "tenancy",
                table: "TenancyEventLog",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TenancyEventLog_TenantId_RecordedAt",
                schema: "tenancy",
                table: "TenancyEventLog",
                columns: new[] { "TenantId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TenancyOutboxMessages_ProcessedAt",
                schema: "tenancy",
                table: "TenancyOutboxMessages",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                schema: "tenancy",
                table: "Tenants",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationUnitPaths",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "OrganizationUnits",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "SeatRights",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "SeatRoleGrants",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "TenancyAccessRevisions",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "TenancyEventLog",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "TenancyOutboxMessages",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Organizations",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "SeatPlacements",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Seats",
                schema: "tenancy");
        }
    }
}
