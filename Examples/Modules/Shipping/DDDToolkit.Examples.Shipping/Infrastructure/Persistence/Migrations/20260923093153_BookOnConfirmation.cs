using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDDToolkit.Examples.Shipping.Migrations
{
    /// <inheritdoc />
    public partial class BookOnConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "InboxMessages",
                schema: "ddd",
                newName: "InboxMessages",
                newSchema: "shipping");

            migrationBuilder.RenameColumn(
                name: "OrderedAt",
                schema: "shipping",
                table: "Shipments",
                newName: "ConfirmedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ddd");

            migrationBuilder.RenameTable(
                name: "InboxMessages",
                schema: "shipping",
                newName: "InboxMessages",
                newSchema: "ddd");

            migrationBuilder.RenameColumn(
                name: "ConfirmedAt",
                schema: "shipping",
                table: "Shipments",
                newName: "OrderedAt");
        }
    }
}
