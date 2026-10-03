using DDDToolkit.EntityFramework.Supabase;
using Examples.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Examples.Tenancy.Projects.Infrastructure.Persistence;

/// <summary>
/// How <c>dotnet ef</c> and the Supabase export build a <see cref="ProjectsContext"/>: on Postgres, with the
/// migration history in the module's schema, as a running host has it
/// (<see cref="ModuleDatabase.UsePostgres(DbContextOptionsBuilder, string, string)"/>). Neither connects, so the
/// connection string names no server. The infrastructure project is its own startup project:
/// <code>
/// dotnet ef migrations add Name --project Examples/Tenancy/Modules/Projects/Examples.Tenancy.Projects.Infrastructure --startup-project Examples/Tenancy/Modules/Projects/Examples.Tenancy.Projects.Infrastructure --output-dir Persistence/Migrations --context ProjectsContext
/// dotnet build Examples/Tenancy/Examples.Tenancy.Exporter
/// </code>
/// The second line writes the new migration as a file under <c>Examples/Tenancy/supabase/migrations</c>: the
/// marker is what the project that exports looks for. A running host is registered with this factory too
/// (<see cref="PostgresPools.AddContext{TContext, TFactory}"/>), and stops at start-up while the database
/// misses a migration found here; it applies none itself.
/// </summary>
[SupabaseMigrations]
public sealed class ProjectsContextFactory : IDesignTimeDbContextFactory<ProjectsContext>
{
    public ProjectsContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ProjectsContext>();
        ModuleDatabase.UsePostgres(options, "Host=unused", ProjectsContext.Schema);
        return new ProjectsContext(options.Options);
    }
}
