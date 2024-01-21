using DDDToolkit.NugetApi.Converters;
using DDDToolkit.NugetApi.Domain.ProductAggregate;
using DDDToolkit.NugetApi.Domain.UserAggregate;
using Microsoft.EntityFrameworkCore;


namespace DDDToolkit.NugetApi.Context;

public class ExampleContext : DbContext
{
    public string DbPath { get; }

    public DbSet<User> Users { get; set; }
    public DbSet<Product> Products { get; set; }

    public ExampleContext()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var dbFileName = "example.db";
        DbPath = Path.Combine(baseDirectory, dbFileName);
    }
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseNpgsql("postgresql://postgres:pass123@localhost:5432/DDDToolkit?sslmode=trust");// options.UseSqlServer($"Server=127.0.0.1;Database=DDDToolkit;User Id=sa;Password=password123!;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExampleContext).Assembly);
        modelBuilder.Entity<User>().OwnsMany(x => x.Orders, o =>
        {
            o.OwnsMany(x => x.Products);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitNugetApiConverters();
    }
}
