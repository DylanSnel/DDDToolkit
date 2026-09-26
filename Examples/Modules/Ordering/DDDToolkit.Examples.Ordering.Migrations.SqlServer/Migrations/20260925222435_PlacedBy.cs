using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDDToolkit.Examples.Ordering.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class PlacedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlacedBy",
                schema: "ordering",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PlacedBy",
                schema: "ordering",
                table: "Orders",
                column: "PlacedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_PlacedBy",
                schema: "ordering",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PlacedBy",
                schema: "ordering",
                table: "Orders");
        }
    }
}
