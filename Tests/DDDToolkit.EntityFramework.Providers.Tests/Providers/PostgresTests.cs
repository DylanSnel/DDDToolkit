using DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

namespace DDDToolkit.EntityFramework.Providers.Tests.Providers;

/// <summary>The mapping suite against PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
[Trait(ProviderTraits.Key, ProviderTraits.Postgres)]
public sealed class PostgresMappingTests(PostgresFixture fixture) : ProviderMappingTests(fixture)
{
    /// <inheritdoc />
    protected override string GuidColumnType => "uuid";

    /// <inheritdoc />
    protected override string TagsColumnType => "ARRAY";

    /// <inheritdoc />
    protected override string ProviderTimestampColumnType => "timestamp with time zone";

    /// <summary>
    /// The same answer as <see cref="ProviderTimestampColumnType"/>, and that is the finding. Npgsql
    /// maps both a UTC <c>DateTime</c> and a <c>DateTimeOffset</c> to <c>timestamptz</c>, so the two
    /// shapes are one column here and a PostgreSQL database needs no migration either way.
    /// </summary>
    protected override string UtcDateTimeColumnType => "timestamp with time zone";

    /// <summary>
    /// Yes: the default mapping and <c>UtcDateTime</c> are one column here, so there is nothing to
    /// migrate and nothing to notice.
    /// </summary>
    protected override bool UnmigratedDatabaseStillReads => true;

    /// <summary>
    /// <c>Tags</c> is a real <c>integer[]</c>, so the array operators answer directly. Nothing in this
    /// line runs on SQL Server.
    /// </summary>
    protected override string TagsContainSevenSql => """SELECT COUNT(*) FROM "Book" WHERE 7 = ANY("Tags")""";
}

/// <summary>The concurrency suite against PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
[Trait(ProviderTraits.Key, ProviderTraits.Postgres)]
public sealed class PostgresConcurrencyTests(PostgresFixture fixture) : ProviderConcurrencyTests(fixture);

/// <summary>The outbox and inbox suite against PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
[Trait(ProviderTraits.Key, ProviderTraits.Postgres)]
public sealed class PostgresMessagingTests(PostgresFixture fixture) : ProviderMessagingTests(fixture);

/// <summary>The container guard against PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
[Trait(ProviderTraits.Key, ProviderTraits.Postgres)]
public sealed class PostgresContainerTests(PostgresFixture fixture) : ProviderContainerTests(fixture)
{
    /// <inheritdoc />
    protected override string ExpectedImage => ContainerImages.Postgres;
}
