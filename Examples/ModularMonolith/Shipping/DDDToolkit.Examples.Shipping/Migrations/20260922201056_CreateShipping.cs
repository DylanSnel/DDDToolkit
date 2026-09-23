using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDDToolkit.Examples.Shipping.Migrations
{
    /// <inheritdoc />
    public partial class CreateShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ddd");

            migrationBuilder.EnsureSchema(
                name: "shipping");

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "ddd",
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

            migrationBuilder.CreateTable(
                name: "Shipments",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<Guid>(type: "uuid", nullable: false),
                    Destination = table.Column<string>(type: "text", nullable: false),
                    OrderedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_ProcessedAt",
                schema: "ddd",
                table: "InboxMessages",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "ddd");

            migrationBuilder.DropTable(
                name: "Shipments",
                schema: "shipping");
        }
    }
}
