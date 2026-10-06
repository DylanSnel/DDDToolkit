using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Supabase;
using Examples.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Examples.Tenancy.Inspections.Infrastructure.Persistence;

/// <summary>
/// How <c>dotnet ef</c> and the Supabase export build an <see cref="InspectionsContext"/>: on Postgres, with what the toolkit
/// gives a context that has no services, <c>UseDDDToolkitDesignTime()</c>: the migration history in the module's
/// schema, where a running host, wired with <c>UseDDDToolkit</c>, reads it. Neither connects, so the connection string
/// names no server. The infrastructure project is its own startup project:
/// <code>
/// dotnet ef migrations add Name --project Examples/Tenancy/Modules/Inspections/Examples.Tenancy.Inspections.Infrastructure --startup-project Examples/Tenancy/Modules/Inspections/Examples.Tenancy.Inspections.Infrastructure --output-dir Persistence/Migrations --context InspectionsContext
/// dotnet build Examples/Tenancy/Examples.Tenancy.Exporter
/// </code>
/// The second line writes the new migration as a file under <c>Examples/Tenancy/supabase/migrations</c>: the
/// marker is what the project that exports looks for. A running host is registered with this factory too
/// (<see cref="PostgresPools.AddContext{TContext, TFactory}"/>), and stops at start-up while the database
/// misses a migration found here; it applies none itself.
/// </summary>
[SupabaseMigrations]
public sealed class InspectionsContextFactory : IDesignTimeDbContextFactory<InspectionsContext>
{
    public InspectionsContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<InspectionsContext>().UseNpgsql("Host=unused").UseDDDToolkitDesignTime().Options);
}
