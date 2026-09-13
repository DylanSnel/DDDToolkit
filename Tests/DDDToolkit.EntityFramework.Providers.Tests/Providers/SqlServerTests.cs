using DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

namespace DDDToolkit.EntityFramework.Providers.Tests.Providers;

/// <summary>The mapping suite against SQL Server.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerMappingTests(SqlServerFixture fixture) : ProviderMappingTests(fixture)
{
    /// <inheritdoc />
    protected override string GuidColumnType => "uniqueidentifier";

    /// <inheritdoc />
    protected override string TagsColumnType => "nvarchar";

    /// <inheritdoc />
    protected override string OutboxTimestampColumnType => "datetime2";

    /// <inheritdoc />
    protected override string NativeOffsetColumnType => "datetimeoffset";
}

/// <summary>The concurrency suite against SQL Server.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerConcurrencyTests(SqlServerFixture fixture) : ProviderConcurrencyTests(fixture);

/// <summary>The outbox and inbox suite against SQL Server.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerMessagingTests(SqlServerFixture fixture) : ProviderMessagingTests(fixture);
