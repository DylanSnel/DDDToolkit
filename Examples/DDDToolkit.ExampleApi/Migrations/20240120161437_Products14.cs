using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDDToolkit.ExampleApi.Migrations;

/// <inheritdoc />
public partial class Products14 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Products",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Price = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Products", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                PasswordHash = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                Name_FirstName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Name_LastName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Name_MiddleNames = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Order",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlacedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Order", x => new { x.UserId, x.Id });
                table.ForeignKey(
                    name: "FK_Order_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ProductId",
            columns: table => new
            {
                OrderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Value = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductId", x => new { x.OrderUserId, x.OrderId, x.Id });
                table.ForeignKey(
                    name: "FK_ProductId_Order_OrderUserId_OrderId",
                    columns: x => new { x.OrderUserId, x.OrderId },
                    principalTable: "Order",
                    principalColumns: new[] { "UserId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ProductId");

        migrationBuilder.DropTable(
            name: "Products");

        migrationBuilder.DropTable(
            name: "Order");

        migrationBuilder.DropTable(
            name: "Users");
    }
}
