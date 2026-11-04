using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Projects.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                schema: "projects",
                table: "ProjectsOutboxMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                schema: "projects",
                table: "ProjectsOutboxMessages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockedBy",
                schema: "projects",
                table: "ProjectsOutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                schema: "projects",
                table: "ProjectsOutboxMessages");
        }
    }
}
