using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace DDDToolkit.EntityFramework.Storage;

/// <summary>
/// Turns a <see cref="DomainEventTimestamps"/> and a provider name into the one decision the outbox
/// and the inbox both need: UTC <see cref="DateTime"/> column, or the provider's own offset type.
/// <para>
/// It lives here rather than in either mapping so the model builder extensions and the hand-written
/// migration helpers cannot answer it differently. A model that says <c>datetimeoffset</c> over a
/// table that says <c>datetime2</c> is the kind of drift nothing fails on until production.
/// </para>
/// </summary>
internal static class DomainEventTimestampMapping
{
    /// <summary>
    /// Whether timestamps become UTC <see cref="DateTime"/> columns here. True when the caller asked
    /// for that shape outright, and true on SQLite whatever the caller asked for, because SQLite
    /// cannot order by a <see cref="DateTimeOffset"/> and the processor's "oldest first" is not
    /// optional.
    /// </summary>
    internal static bool StoresUtcDateTime(string? providerName, DomainEventTimestamps timestamps)
        => timestamps == DomainEventTimestamps.UtcDateTime || IsSqlite(providerName);

    /// <summary>Whether <paramref name="providerName"/> is the SQLite provider.</summary>
    internal static bool IsSqlite(string? providerName)
        => string.Equals(providerName, DomainEventStorage.SqliteProvider, StringComparison.Ordinal);

    /// <summary>Applies the chosen storage to a required timestamp property.</summary>
    internal static PropertyBuilder<DateTimeOffset> AsTimestamp(this PropertyBuilder<DateTimeOffset> property, bool utcDateTime)
        => utcDateTime
            ? property.HasConversion(new UtcDateTimeOffsetConverter())
            : property.HasConversion(new UtcOffsetConverter());

    /// <summary>Applies the chosen storage to an optional timestamp property.</summary>
    internal static PropertyBuilder<DateTimeOffset?> AsTimestamp(this PropertyBuilder<DateTimeOffset?> property, bool utcDateTime)
        => utcDateTime
            ? property.HasConversion(new NullableUtcDateTimeOffsetConverter())
            : property.HasConversion(new NullableUtcOffsetConverter());

    /// <summary>
    /// The timestamp column a hand-written migration should create. <c>Column&lt;T&gt;</c> returns the
    /// same builder type whatever <c>T</c> is, so the two shapes sit in one anonymous type without the
    /// whole <c>CreateTable</c> call being written twice.
    /// </summary>
    internal static OperationBuilder<AddColumnOperation> TimestampColumn(this ColumnsBuilder table, bool utcDateTime, bool nullable = false)
        => utcDateTime
            ? table.Column<DateTime>(nullable: nullable)
            : table.Column<DateTimeOffset>(nullable: nullable);
}
