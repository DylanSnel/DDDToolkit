using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

// The first migration of each context the Supabase export tests export and otherwise build from the model. The
// export writes a context's access file once the context has a migration, and none before it, so each of these
// stands in for the migration that makes the context's tables. It makes the context's schema and nothing else: the
// tests that need the tables make them from the model. Each has a version of its own, so contexts exported into one
// directory never share one.

[DbContext(typeof(DeskContext))]
[Migration(Id)]
public sealed class CreateDesk : Migration
{
    public const string Id = "20260801000000_CreateDesk";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.EnsureSchema("desk");
}

[DbContext(typeof(DeskScaleContext))]
[Migration(Id)]
public sealed class CreateDeskScale : Migration
{
    public const string Id = "20260801000100_CreateDeskScale";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.EnsureSchema("desk_scale");
}

[DbContext(typeof(YardContext))]
[Migration(Id)]
public sealed class CreateYard : Migration
{
    public const string Id = "20260801000200_CreateYard";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.EnsureSchema("yard");
}

[DbContext(typeof(AnnalsContext))]
[Migration(Id)]
public sealed class CreateAnnals : Migration
{
    public const string Id = "20260801000300_CreateAnnals";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.EnsureSchema("archive");
}
