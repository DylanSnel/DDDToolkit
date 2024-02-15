using DDDToolkit.ExampleApi.Converters;
using DDDToolkit.ExampleApi.Domain.ProductAggregate;
using DDDToolkit.ExampleApi.Domain.UserAggregate;
using DDDToolkit.ExampleLibrary.Converters;
using Microsoft.EntityFrameworkCore;


namespace DDDToolkit.ExampleApi.Context;

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
        => options.UseSqlServer($"Server=127.0.0.1;Database=DDDToolkit;User Id=sa;Password=password123!;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExampleContext).Assembly);
        modelBuilder.Entity<User>().OwnsMany(x => x.Orders, o =>
        {
            o.OwnsMany(x => x.Products, p => p.ToTable("UserProducts"));
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddCommonConverters();
        configurationBuilder.AddDDDToolkitExampleApiConverters();
        //configurationBuilder.Properties<EmailAddress>().HaveConversion<EmailAddress.EmailAddressConverter>().HaveMaxLength(EmailAddress.MaxLength);
    }
}
