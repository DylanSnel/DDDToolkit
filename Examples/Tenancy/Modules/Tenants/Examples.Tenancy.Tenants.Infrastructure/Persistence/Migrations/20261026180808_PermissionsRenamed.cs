using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Tenants.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermissionsRenamed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Key",
                schema: "tenancy",
                table: "SeatRights",
                newName: "Permission");

            migrationBuilder.RenameIndex(
                name: "IX_SeatRights_SeatId_Key",
                schema: "tenancy",
                table: "SeatRights",
                newName: "IX_SeatRights_SeatId_Permission");

            migrationBuilder.RenameColumn(
                name: "KeysFromPack",
                schema: "tenancy",
                table: "Roles",
                newName: "PermissionsFromPack");

            migrationBuilder.RenameColumn(
                name: "Keys",
                schema: "tenancy",
                table: "Roles",
                newName: "Permissions");

            // The event a role's permissions changing raises is stored under its new name from now on: the rows
            // already written under the old one are given it, so the access history still reads them.
            migrationBuilder.Sql("""UPDATE "tenancy"."TenancyEventLog" SET "EventName" = 'tenancy.role-permissions-changed' WHERE "EventName" = 'tenancy.role-keys-changed';""");
            migrationBuilder.Sql("""UPDATE "tenancy"."TenancyOutboxMessages" SET "EventName" = 'tenancy.role-permissions-changed' WHERE "EventName" = 'tenancy.role-keys-changed';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Permission",
                schema: "tenancy",
                table: "SeatRights",
                newName: "Key");

            migrationBuilder.RenameIndex(
                name: "IX_SeatRights_SeatId_Permission",
                schema: "tenancy",
                table: "SeatRights",
                newName: "IX_SeatRights_SeatId_Key");

            migrationBuilder.RenameColumn(
                name: "PermissionsFromPack",
                schema: "tenancy",
                table: "Roles",
                newName: "KeysFromPack");

            migrationBuilder.RenameColumn(
                name: "Permissions",
                schema: "tenancy",
                table: "Roles",
                newName: "Keys");

            migrationBuilder.Sql("""UPDATE "tenancy"."TenancyEventLog" SET "EventName" = 'tenancy.role-keys-changed' WHERE "EventName" = 'tenancy.role-permissions-changed';""");
            migrationBuilder.Sql("""UPDATE "tenancy"."TenancyOutboxMessages" SET "EventName" = 'tenancy.role-keys-changed' WHERE "EventName" = 'tenancy.role-permissions-changed';""");
        }
    }
}
