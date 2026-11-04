using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Tenants.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                schema: "tenancy",
                table: "TenancyOutboxMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                schema: "tenancy",
                table: "TenancyOutboxMessages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockedBy",
                schema: "tenancy",
                table: "TenancyOutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                schema: "tenancy",
                table: "TenancyOutboxMessages");
        }
    }
}
