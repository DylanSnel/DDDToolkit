using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Shipping;

namespace DDDToolkit.Examples.Tests;

/// <summary>
/// The test a real project would have: whoever adds a migration and forgets to export it finds out here,
/// not from <c>supabase db push</c>. Fix a failure by running
/// <c>dotnet run --project Examples/ModularMonolith/DDDToolkit.Examples.Host -- export-supabase</c>.
/// <para>
/// One test for every module, and no host: each module's source builds its context from the design-time
/// factory, so this is as fast as reading the migrations.
/// </para>
/// </summary>
public sealed class SupabaseMigrationsTests
{
    [Fact]
    public void Supabase_has_every_migration_of_every_module()
        => SupabaseMigrations.EnsureInSync(
            [OrderingModule.SupabaseMigrations, ShippingModule.SupabaseMigrations],
            SupabaseMigrations.FindDirectory(Path.Combine(RepositoryRoot(), "Examples", "ModularMonolith")));

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
