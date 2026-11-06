using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Webshop.Inventory.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class OutboxLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                schema: "inventory",
                table: "OutboxMessages",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                schema: "inventory",
                table: "OutboxMessages",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockedBy",
                schema: "inventory",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                schema: "inventory",
                table: "OutboxMessages");
        }
    }
}
