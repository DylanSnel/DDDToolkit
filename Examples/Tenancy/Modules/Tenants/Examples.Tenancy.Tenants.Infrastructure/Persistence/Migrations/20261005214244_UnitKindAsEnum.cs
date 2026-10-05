using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Tenants.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnitKindAsEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                schema: "tenancy",
                table: "OrganizationUnits",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                schema: "tenancy",
                table: "OrganizationUnits",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);
        }
    }
}
