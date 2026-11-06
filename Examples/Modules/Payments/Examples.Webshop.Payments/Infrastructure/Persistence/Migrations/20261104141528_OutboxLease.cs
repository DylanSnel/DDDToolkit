using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Webshop.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                schema: "payments",
                table: "OutboxMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                schema: "payments",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockedBy",
                schema: "payments",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                schema: "payments",
                table: "OutboxMessages");
        }
    }
}
