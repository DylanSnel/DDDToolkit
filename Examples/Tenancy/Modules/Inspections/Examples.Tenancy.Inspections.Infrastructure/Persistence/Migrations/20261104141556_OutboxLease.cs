using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Inspections.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                schema: "inspections",
                table: "InspectionsOutboxMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                schema: "inspections",
                table: "InspectionsOutboxMessages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockedBy",
                schema: "inspections",
                table: "InspectionsOutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                schema: "inspections",
                table: "InspectionsOutboxMessages");
        }
    }
}
