using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Catalog;
using DDDToolkit.Examples.Inventory;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Payments;
using DDDToolkit.Examples.Shipping;
using FluentAssertions;

namespace DDDToolkit.Examples.Tests;

/// <summary>
/// Whether the example's files are in sync is not a test here: the host's build checks it, and fails in
/// CI when a migration was added without its file. What a test can pin down is where the file names
/// come from: each module's <c>[assembly: Module]</c>, the same declaration the boundary analyzer reads.
/// </summary>
public sealed class SupabaseMigrationsTests
{
    [Fact]
    public void Each_module_names_its_files_after_the_module_it_declares()
    {
        SupabaseMigrations.ModuleNameOf(typeof(CatalogContext)).Should().Be("catalog");
        SupabaseMigrations.ModuleNameOf(typeof(OrderingContext)).Should().Be("ordering");
        SupabaseMigrations.ModuleNameOf(typeof(InventoryContext)).Should().Be("inventory");
        SupabaseMigrations.ModuleNameOf(typeof(PaymentsContext)).Should().Be("payments");
        SupabaseMigrations.ModuleNameOf(typeof(ShippingContext)).Should().Be("shipping");
    }

    [Fact]
    public void The_committed_files_carry_those_names()
    {
        var migrations = SupabaseMigrations.FindDirectory(Path.Combine(RepositoryRoot(), "Examples", "ModularMonolith.Supabase"));

        Directory.GetFiles(migrations, "*.ddd.sql").Select(Path.GetFileName)
            .Select(name => name!.Split('.')[^3])
            .Distinct()
            .Should().BeEquivalentTo("catalog", "ordering", "inventory", "payments", "shipping");
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
