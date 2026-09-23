using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Shipping;

namespace DDDToolkit.Examples.Tests;

/// <summary>
/// The test a real project would have: whoever adds a migration and forgets to export it finds out here,
/// not from <c>supabase db push</c>. Fix a failure by running
/// <c>dotnet run --project Examples/ModularMonolith/DDDToolkit.Examples.Host -- export-supabase</c>.
/// </summary>
public sealed class SupabaseMigrationsTests
{
    private static readonly string Migrations = SupabaseMigrations.FindDirectory(
        Path.Combine(RepositoryRoot(), "Examples", "ModularMonolith"));

    [Fact]
    public void Supabase_has_every_Ordering_migration()
    {
        using var context = new OrderingContextFactory().CreateDbContext([]);
        SupabaseMigrations.EnsureInSync(context, Migrations);
    }

    [Fact]
    public void Supabase_has_every_Shipping_migration()
    {
        using var context = new ShippingContextFactory().CreateDbContext([]);
        SupabaseMigrations.EnsureInSync(context, Migrations);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DDDToolkit.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No DDDToolkit.slnx above '{AppContext.BaseDirectory}'.");
    }
}
