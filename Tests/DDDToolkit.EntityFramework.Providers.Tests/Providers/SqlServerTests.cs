using DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

namespace DDDToolkit.EntityFramework.Providers.Tests.Providers;

/// <summary>The mapping suite against SQL Server.</summary>
[Collection(SqlServerCollection.Name)]
[Trait(ProviderTraits.Key, ProviderTraits.SqlServer)]
public sealed class SqlServerMappingTests(SqlServerFixture fixture) : ProviderMappingTests(fixture)
{
    /// <inheritdoc />
    protected override string GuidColumnType => "uniqueidentifier";

    /// <inheritdoc />
    protected override string TagsColumnType => "nvarchar";

    /// <inheritdoc />
    protected override string ProviderTimestampColumnType => "datetimeoffset";

    /// <summary>
    /// <c>datetime2</c>, not <c>datetimeoffset</c>. This is the provider where the choice is real: a
    /// database written by an earlier build has these columns, and moving it to the default needs an
    /// <c>ALTER COLUMN</c>.
    /// </summary>
    protected override string UtcDateTimeColumnType => "datetime2";

    /// <summary>
    /// No. <c>datetime2</c> comes back as a <c>DateTime</c> and the model wants a
    /// <c>DateTimeOffset</c>, so the first read throws. Loud is the right answer here.
    /// </summary>
    protected override bool UnmigratedDatabaseStillReads => false;

    /// <summary>
    /// <c>Tags</c> is a JSON string, so reaching inside it needs <c>OPENJSON</c>. Nothing in this line
    /// runs on PostgreSQL.
    /// </summary>
    protected override string TagsContainSevenSql
        => """SELECT COUNT(*) FROM "Book" WHERE EXISTS (SELECT 1 FROM OPENJSON("Tags") WHERE CAST([value] AS int) = 7)""";
}

/// <summary>The concurrency suite against SQL Server.</summary>
[Collection(SqlServerCollection.Name)]
[Trait(ProviderTraits.Key, ProviderTraits.SqlServer)]
public sealed class SqlServerConcurrencyTests(SqlServerFixture fixture) : ProviderConcurrencyTests(fixture);

/// <summary>The outbox and inbox suite against SQL Server.</summary>
[Collection(SqlServerCollection.Name)]
[Trait(ProviderTraits.Key, ProviderTraits.SqlServer)]
public sealed class SqlServerMessagingTests(SqlServerFixture fixture) : ProviderMessagingTests(fixture);

/// <summary>The container guard against SQL Server.</summary>
[Collection(SqlServerCollection.Name)]
[Trait(ProviderTraits.Key, ProviderTraits.SqlServer)]
public sealed class SqlServerContainerTests(SqlServerFixture fixture) : ProviderContainerTests(fixture)
{
    /// <inheritdoc />
    protected override string ExpectedImage => ContainerImages.SqlServer;
}
