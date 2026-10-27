using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Tenants.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StarterRolesRenamed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FromPack",
                schema: "tenancy",
                table: "Roles",
                newName: "FromStarterRole");

            migrationBuilder.RenameColumn(
                name: "PermissionsFromPack",
                schema: "tenancy",
                table: "Roles",
                newName: "PermissionsFromStarterRole");

            // Tenancy's two triggers on what a role was made from are named after the starter role now. The access file
            // after this migration makes the new ones. The old ones still fire on the renamed columns, so they come off
            // here, with their functions.
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS tenancy_role_pack_is_fixed ON "tenancy"."Roles";""");
            migrationBuilder.Sql("""DROP FUNCTION IF EXISTS "tenancy".role_pack_is_fixed();""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS tenancy_role_follows_its_pack ON "tenancy"."Roles";""");
            migrationBuilder.Sql("""DROP FUNCTION IF EXISTS "tenancy".role_follows_its_pack();""");

            // The event a role following its starter role raises is stored under its new name from now on, and two of
            // the events' members are renamed. The rows already written are given both, so the access history still
            // reads them and the outbox still delivers them.
            foreach (var table in new[] { "TenancyEventLog", "TenancyOutboxMessages" })
            {
                migrationBuilder.Sql($"""UPDATE "tenancy"."{table}" SET "Payload" = replace("Payload", '"FromPack":', '"FromStarterRole":') WHERE "EventName" = 'tenancy.role-created';""");
                migrationBuilder.Sql($"""UPDATE "tenancy"."{table}" SET "EventName" = 'tenancy.role-followed-its-starter-role', "Payload" = replace("Payload", '"Pack":', '"StarterRole":') WHERE "EventName" = 'tenancy.role-followed-its-pack';""");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FromStarterRole",
                schema: "tenancy",
                table: "Roles",
                newName: "FromPack");

            migrationBuilder.RenameColumn(
                name: "PermissionsFromStarterRole",
                schema: "tenancy",
                table: "Roles",
                newName: "PermissionsFromPack");

            foreach (var table in new[] { "TenancyEventLog", "TenancyOutboxMessages" })
            {
                migrationBuilder.Sql($"""UPDATE "tenancy"."{table}" SET "Payload" = replace("Payload", '"FromStarterRole":', '"FromPack":') WHERE "EventName" = 'tenancy.role-created';""");
                migrationBuilder.Sql($"""UPDATE "tenancy"."{table}" SET "EventName" = 'tenancy.role-followed-its-pack', "Payload" = replace("Payload", '"StarterRole":', '"Pack":') WHERE "EventName" = 'tenancy.role-followed-its-starter-role';""");
            }
        }
    }
}
