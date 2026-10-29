using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Acme.Press.Persistence;

// The first migration of each context. The Supabase export writes a context's access file once the context has a
// migration, and none before it, so each of these stands in for the migration that makes the context's tables. The
// consumer is built, never applied to a database, so a migration makes the context's schema and nothing else. Each has
// a version of its own, so the two contexts' files never share one.

/// <summary>Tenancy's first migration: its schema.</summary>
[DbContext(typeof(TenancyContext))]
[Migration("20260801000000_CreateTenancy")]
public sealed class CreateTenancy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.EnsureSchema(TenancyContext.Schema);
}

/// <summary>The manuscripts' first migration. Their tables are in the database's default schema, so it makes nothing.</summary>
[DbContext(typeof(PressContext))]
[Migration("20260801000100_CreatePress")]
public sealed class CreatePress : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }
}
