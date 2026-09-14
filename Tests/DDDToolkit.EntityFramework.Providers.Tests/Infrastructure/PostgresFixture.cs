using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>PostgreSQL in a container, through Npgsql.EntityFrameworkCore.PostgreSQL.</summary>
public sealed class PostgresFixture : ProviderFixture
{
    private PostgreSqlContainer? _container;

    /// <inheritdoc />
    public override string ProviderName => "PostgreSQL";

    /// <inheritdoc />
    public override string Image => ContainerImages.Postgres;

    /// <inheritdoc />
    protected override async Task<string> StartContainerAsync()
    {
        _container = new PostgreSqlBuilder(Image).Build();
        await _container.StartAsync();
        return _container.GetConnectionString();
    }

    /// <inheritdoc />
    protected override async ValueTask StopContainerAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <inheritdoc />
    protected override string ConnectionStringFor(string template, string databaseName)
        => new NpgsqlConnectionStringBuilder(template) { Database = databaseName }.ConnectionString;

    /// <inheritdoc />
    protected override void Configure(DbContextOptionsBuilder builder, string connectionString)
        => builder.UseNpgsql(connectionString);
}

/// <summary>
/// Names the collection whose tests share one PostgreSQL container. Everything in the collection runs
/// in sequence; the SQL Server collection runs beside it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "PostgreSQL";
}
