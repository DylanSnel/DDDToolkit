using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework;

/// <summary>
/// What <c>UseDDDToolkit</c>, <c>UseDDDToolkitCore</c> and <c>UseDDDToolkitDesignTime</c> leave in a context's options so
/// that its migration history is kept in the context's default schema, beside its tables, wherever the options name no
/// history table of their own.
/// </summary>
/// <remarks>
/// <para>
/// Entity Framework learns where the history is from the options alone, <c>MigrationsHistoryTable(name, schema)</c>,
/// and keeps it in the provider's default schema when they name none: <c>public</c> on Postgres, <c>dbo</c> on SQL
/// Server. So every context of a database whose model has a schema of its own shared one history, and read the others'
/// migrations as its own, unless its options repeated the schema the model already said. The default schema is the
/// model's, and a model is built after its options, so no call on the options can name it. This extension makes the
/// provider's own history repository with the options it would have had, had they named the default schema: when the
/// repository is made, which is after the model. It works for every relational provider, configured before this
/// extension or after it, in the options callback or in <c>OnConfiguring</c>. SQLite, which has no schemas, leaves the
/// schema's name out of its SQL, as it does for the tables.
/// </para>
/// <para>
/// It needs none of the application's services, so the options of a design-time factory, which has none, hold it as
/// well. <c>dotnet ef</c> and the Supabase export make a context through that factory, and the history their scripts
/// write is then the one the running application reads. Options that name the history table, by its name or its
/// schema, keep what they name: that is how a context keeps its history where an earlier release left it. A model with
/// no default schema keeps the provider's. A host that replaced the history repository with <c>ReplaceService</c>,
/// for every implementation or for the provider's alone, keeps its own repository, made as Entity Framework makes it:
/// a repository of the host's own decides where its history is.
/// </para>
/// <para>
/// Entity Framework hands an extension the services of a context only where it builds the context's internal service
/// provider itself. A host that hands it one, with <c>UseInternalServiceProvider</c>, built the services without this
/// extension, so its contexts would keep the history in the provider's default schema while a design-time factory,
/// which has no such provider, keeps it in the model's. Such options are refused, unless they name the history table,
/// which then holds for both.
/// </para>
/// <para>
/// <c>DDDToolkit.EntityFramework.Postgres</c> grants the bookkeeping role the table the context's history repository
/// records a migration in, whatever placed it there, so it needs nothing of this type.
/// </para>
/// </remarks>
internal sealed class MigrationHistoryExtension : IDbContextOptionsExtension
{
    /// <summary>The one instance: it holds nothing, so every context's options can share it.</summary>
    public static readonly MigrationHistoryExtension Instance = new();

    /// <summary>Per provider's options extension, the history repository the provider registers, once found.</summary>
    private static readonly ConcurrentDictionary<Type, Type?> ProvidersRepositories = new();

    private MigrationHistoryExtension() => Info = new ExtensionInfo(this);

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info { get; }

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
    {
        var registered = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IHistoryRepository) && !descriptor.IsKeyedService);
        if (registered is null)
        {
            // The provider comes after this extension, and adds its repository only where none is: so this one is the
            // context's, and makes the provider's when it is first asked for.
            services.AddScoped<IHistoryRepository>(provider => Make(provider, ProvidersRepository(provider)));
        }
        else if (registered.ImplementationType is { } repository)
        {
            // The provider came first: its repository stays, made with the options it reads the history from.
            services.Remove(registered);
            services.AddScoped<IHistoryRepository>(provider => Make(provider, repository));
        }

        // A repository registered otherwise is not the provider's own, and is left as it is.
    }

    /// <summary>
    /// Refuses options that hand Entity Framework an internal service provider of the host's own and name no history
    /// table: <see cref="ApplyServices"/> never reaches such a provider, so the history would stay where the provider
    /// keeps it, and a design-time factory would keep it elsewhere, without a word.
    /// </summary>
    /// <exception cref="InvalidOperationException">The options name an internal service provider and no history table.</exception>
    public void Validate(IDbContextOptions options)
    {
        if (options.FindExtension<CoreOptionsExtension>()?.InternalServiceProvider is null
            || options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault() is not { } relational
            || relational.MigrationsHistoryTableName is not null
            || relational.MigrationsHistoryTableSchema is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            "The context's options hand Entity Framework an internal service provider of their own (UseInternalServiceProvider), and the toolkit cannot place the migration history in a provider it did not build: " +
            "the context would keep it in the provider's default schema, and its design-time factory, with UseDDDToolkitDesignTime(), in the context's default schema. " +
            "Name the history table in the options of both, MigrationsHistoryTable(HistoryRepository.DefaultTableName, schema), or let Entity Framework build the internal service provider.");
    }

    /// <summary>
    /// The provider's history repository <paramref name="repository"/>, made as Entity Framework makes it, but with the
    /// options' history table in the model's default schema where the options name none. Where the options replace the
    /// provider's repository with one of the host's own, <c>ReplaceService&lt;IHistoryRepository, TProvidersRepository,
    /// TReplacement&gt;()</c>, that one is made instead, as Entity Framework would have made it, with nothing changed: the
    /// replacement is matched to the provider's registration by its type, which this extension's registration does not
    /// have, so Entity Framework would leave it out.
    /// </summary>
    private static IHistoryRepository Make(IServiceProvider services, Type repository)
    {
        if (services.GetRequiredService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ReplacedServices is { } replaced
            && replaced.TryGetValue((typeof(IHistoryRepository), repository), out var replacement))
        {
            return (IHistoryRepository)ActivatorUtilities.CreateInstance(services, replacement);
        }

        var dependencies = services.GetRequiredService<HistoryRepositoryDependencies>();
        if (InTheDefaultSchema(dependencies.Options, services) is { } options)
        {
            dependencies = dependencies with { Options = options };
        }

        return (IHistoryRepository)ActivatorUtilities.CreateInstance(services, repository, dependencies);
    }

    /// <summary>
    /// <paramref name="options"/> with the history table in the model's default schema, or <see langword="null"/>
    /// where they name a history table, by its name or its schema, or the model has no default schema.
    /// </summary>
    private static DbContextOptions? InTheDefaultSchema(IDbContextOptions options, IServiceProvider services)
    {
        var relational = RelationalOptionsExtension.Extract(options);
        if (relational.MigrationsHistoryTableName is not null || relational.MigrationsHistoryTableSchema is not null)
        {
            return null;
        }

        // The model the migrations are made from, which is the one dotnet ef reads. It is built with the context's own,
        // so asking it costs nothing a migration would not.
        return services.GetRequiredService<IDesignTimeModel>().Model.GetDefaultSchema() is { } schema && options is DbContextOptions all
            ? all.WithExtension(relational.WithMigrationsHistoryTableSchema(schema))
            : null;
    }

    /// <summary>
    /// The history repository the context's provider registers: what its options extension adds for
    /// <see cref="IHistoryRepository"/>, asked of it once per provider.
    /// </summary>
    /// <exception cref="InvalidOperationException">The provider registers no repository of a type of its own.</exception>
    private static Type ProvidersRepository(IServiceProvider services)
    {
        var provider = services.GetRequiredService<IDbContextOptions>().Extensions.Single(extension => extension.Info.IsDatabaseProvider);

        return ProvidersRepositories.GetOrAdd(provider.GetType(), _ =>
        {
            var registrations = new ServiceCollection();
            provider.ApplyServices(registrations);
            return registrations.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IHistoryRepository) && !descriptor.IsKeyedService)?.ImplementationType;
        }) ?? throw new InvalidOperationException(
            $"The provider of '{services.GetRequiredService<ICurrentDbContext>().Context.GetType().Name}' registers no migration history repository of its own, so the toolkit cannot keep the history in the context's default schema. " +
            "Name the history table in the provider's options with MigrationsHistoryTable(...), and the toolkit leaves it as named.");
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "MigrationHistoryInDefaultSchema ";

        // It holds nothing, so every context that has it can share the internal services of another that has it.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["DDDToolkit:MigrationHistoryInDefaultSchema"] = "1";
    }
}
