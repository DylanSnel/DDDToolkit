using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Examples.Tenancy.Projects.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StarterRolesRenamed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Projects reads Tenancy's roles through a function of Tenancy's access file, which answers the starter role
            // a role was made from under its new name. Nothing of this module's own tables changes: the migration keeps
            // the model the snapshot holds in step with the one the module maps.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
