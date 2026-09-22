using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Tests.Converters;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.ExampleApi.Converters;
using DDDToolkit.ExampleLibrary.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>The key-part domain with nothing configured by hand: every key comes from the convention.</summary>
public class KeyPartContext(DbContextOptions<KeyPartContext> options) : DbContext(options)
{
    public DbSet<Survey> Surveys => Set<Survey>();

    public DbSet<Census> Censuses => Set<Census>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }
}

/// <summary>The same domain with the host's own key on <see cref="Survey"/>, which must win over the convention.</summary>
public class ExplicitSurveyKeyContext(DbContextOptions<ExplicitSurveyKeyContext> options) : DbContext(options)
{
    public DbSet<Survey> Surveys => Set<Survey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Survey>().HasKey(survey => survey.Id);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }
}

/// <summary><see cref="Archive"/> owns a type that cannot carry its key part; building the model must fail.</summary>
public class MissingKeyPartContext(DbContextOptions<MissingKeyPartContext> options) : DbContext(options)
{
    public DbSet<Archive> Archives => Set<Archive>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }
}

/// <summary>
/// <see cref="LibraryContext"/> without the key-part convention: the model every earlier release
/// built, for comparing against the model built with it.
/// </summary>
public class LibraryContextWithoutKeyParts(DbContextOptions<LibraryContext> options) : LibraryContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Remove(typeof(KeyPartConvention));
    }
}

/// <summary>Keyed and unkeyed aggregates side by side, with or without the key-part convention.</summary>
public class MixedKeyPartContext(DbContextOptions<MixedKeyPartContext> options) : LibraryContextBase<MixedKeyPartContext>(options)
{
    public DbSet<Survey> Surveys => Set<Survey>();
}

/// <inheritdoc cref="MixedKeyPartContext"/>
public class MixedContextWithoutKeyParts(DbContextOptions<MixedKeyPartContext> options) : MixedKeyPartContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Remove(typeof(KeyPartConvention));
    }
}

/// <summary>The library domain's sets and conventions, for a context that adds more.</summary>
public abstract class LibraryContextBase<TContext>(DbContextOptions<TContext> options) : DbContext(options)
    where TContext : DbContext
{
    public DbSet<Shelf> Shelves => Set<Shelf>();

    public DbSet<Person> People => Set<Person>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddCommonConverters();
        configurationBuilder.AddApiConverters();
        configurationBuilder.AddEfTestsConverters();
    }
}
