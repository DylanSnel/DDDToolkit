using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Tenants.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KeysFromPack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "KeysFromPack",
                schema: "tenancy",
                table: "Roles",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeysFromPack",
                schema: "tenancy",
                table: "Roles");
        }
    }
}
