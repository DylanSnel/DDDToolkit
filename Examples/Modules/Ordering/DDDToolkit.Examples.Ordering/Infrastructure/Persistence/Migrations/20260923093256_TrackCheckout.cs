using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDDToolkit.Examples.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class TrackCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "OutboxMessages",
                schema: "ddd",
                newName: "OutboxMessages",
                newSchema: "ordering");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                schema: "ordering",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedAt",
                schema: "ordering",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Paid",
                schema: "ordering",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "ordering",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "StockReserved",
                schema: "ordering",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Total_Amount",
                schema: "ordering",
                table: "Orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Total_Currency",
                schema: "ordering",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPriceAmount",
                schema: "ordering",
                table: "OrderLine",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "UnitPriceCurrency",
                schema: "ordering",
                table: "OrderLine",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CatalogPrices",
                schema: "ordering",
                columns: table => new
                {
                    Sku = table.Column<string>(type: "text", nullable: false),
                    PricedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Price_Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Price_Currency = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogPrices", x => x.Sku);
                });

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "ordering",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MessageName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => new { x.MessageId, x.Consumer });
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_ProcessedAt",
                schema: "ordering",
                table: "InboxMessages",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogPrices",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "ordering");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                schema: "ordering",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                schema: "ordering",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Paid",
                schema: "ordering",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "ordering",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StockReserved",
                schema: "ordering",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Total_Amount",
                schema: "ordering",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Total_Currency",
                schema: "ordering",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "UnitPriceAmount",
                schema: "ordering",
                table: "OrderLine");

            migrationBuilder.DropColumn(
                name: "UnitPriceCurrency",
                schema: "ordering",
                table: "OrderLine");

            migrationBuilder.EnsureSchema(
                name: "ddd");

            migrationBuilder.RenameTable(
                name: "OutboxMessages",
                schema: "ordering",
                newName: "OutboxMessages",
                newSchema: "ddd");
        }
    }
}
