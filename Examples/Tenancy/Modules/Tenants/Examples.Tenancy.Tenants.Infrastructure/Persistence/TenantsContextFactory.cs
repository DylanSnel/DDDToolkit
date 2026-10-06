using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Supabase;
using Examples.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Examples.Tenancy.Tenants.Infrastructure.Persistence;

/// <summary>
/// How <c>dotnet ef</c> and the Supabase export build a <see cref="TenantsContext"/>: on Postgres, with what the toolkit
/// gives a context that has no services, <c>UseDDDToolkitDesignTime()</c>: the migration history in the module's
/// schema, where a running host, wired with <c>UseDDDToolkit</c>, reads it. Neither connects, so the connection string
/// names no server. The infrastructure project is its own startup project:
/// <code>
/// dotnet ef migrations add Name --project Examples/Tenancy/Modules/Tenants/Examples.Tenancy.Tenants.Infrastructure --startup-project Examples/Tenancy/Modules/Tenants/Examples.Tenancy.Tenants.Infrastructure --output-dir Persistence/Migrations --context TenantsContext
/// dotnet build Examples/Tenancy/Examples.Tenancy.Exporter
/// </code>
/// The second line writes the new migration as a file under <c>Examples/Tenancy/supabase/migrations</c>: the
/// marker is what the project that exports looks for. A running host is registered with this factory too
/// (<see cref="PostgresPools.AddContext{TContext, TFactory}"/>), and stops at start-up while the database
/// misses a migration found here; it applies none itself.
/// </summary>
[SupabaseMigrations]
public sealed class TenantsContextFactory : IDesignTimeDbContextFactory<TenantsContext>
{
    public TenantsContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<TenantsContext>().UseNpgsql("Host=unused").UseDDDToolkitDesignTime().Options);
}
