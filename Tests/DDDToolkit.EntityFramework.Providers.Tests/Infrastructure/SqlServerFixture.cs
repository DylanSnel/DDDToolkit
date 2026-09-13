using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>SQL Server in a container, through Microsoft.EntityFrameworkCore.SqlServer.</summary>
public sealed class SqlServerFixture : ProviderFixture
{
    /// <summary>The image the numbers and findings in the documentation came from.</summary>
    public const string Image = "mcr.microsoft.com/mssql/server:2022-latest";

    private MsSqlContainer? _container;

    /// <inheritdoc />
    public override string ProviderName => "SQL Server";

    /// <inheritdoc />
    protected override async Task<string> StartContainerAsync()
    {
        _container = new MsSqlBuilder(Image).Build();
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
        => new SqlConnectionStringBuilder(template) { InitialCatalog = databaseName }.ConnectionString;

    /// <inheritdoc />
    protected override void Configure(DbContextOptionsBuilder builder, string connectionString)
        => builder.UseSqlServer(connectionString);
}

/// <summary>
/// Names the collection whose tests share one SQL Server container. Everything in the collection runs
/// in sequence; the PostgreSQL collection runs beside it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "SQL Server";
}
