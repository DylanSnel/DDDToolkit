using DDDToolkit.EntityFramework.Supabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Acme.Press.Persistence;

/// <summary>
/// Tenancy's context at design time, on Postgres, for <c>dotnet ef</c> and for the host's Supabase export; neither
/// opens a connection. The marker is what the host's build looks for in the projects it references.
/// </summary>
[SupabaseMigrations]
public sealed class TenancyContextFactory : IDesignTimeDbContextFactory<TenancyContext>
{
    /// <inheritdoc />
    public TenancyContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<TenancyContext>().UseNpgsql("Host=unused").Options);
}

/// <summary>The manuscripts' context at design time, the same way.</summary>
[SupabaseMigrations]
public sealed class PressContextFactory : IDesignTimeDbContextFactory<PressContext>
{
    /// <inheritdoc />
    public PressContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<PressContext>().UseNpgsql("Host=unused").Options);
}
