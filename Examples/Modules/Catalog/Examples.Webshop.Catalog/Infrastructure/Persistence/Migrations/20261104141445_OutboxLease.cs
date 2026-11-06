using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Webshop.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                schema: "catalog",
                table: "OutboxMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                schema: "catalog",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockedBy",
                schema: "catalog",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                schema: "catalog",
                table: "OutboxMessages");
        }
    }
}
