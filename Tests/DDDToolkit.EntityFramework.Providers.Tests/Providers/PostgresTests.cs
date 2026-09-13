using DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

namespace DDDToolkit.EntityFramework.Providers.Tests.Providers;

/// <summary>The mapping suite against PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresMappingTests(PostgresFixture fixture) : ProviderMappingTests(fixture)
{
    /// <inheritdoc />
    protected override string GuidColumnType => "uuid";

    /// <inheritdoc />
    protected override string TagsColumnType => "ARRAY";

    /// <inheritdoc />
    protected override string OutboxTimestampColumnType => "timestamp with time zone";

    /// <inheritdoc />
    protected override string NativeOffsetColumnType => "timestamp with time zone";
}

/// <summary>The concurrency suite against PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresConcurrencyTests(PostgresFixture fixture) : ProviderConcurrencyTests(fixture);

/// <summary>The outbox and inbox suite against PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresMessagingTests(PostgresFixture fixture) : ProviderMessagingTests(fixture);
