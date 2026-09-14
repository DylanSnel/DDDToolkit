using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DDDToolkit.EntityFramework.Storage;

/// <summary>
/// Stores a <see cref="DateTimeOffset"/> as its UTC instant in a <see cref="DateTime"/> column, so a
/// provider that cannot order its own offset type can still index and order the column. SQLite is the
/// provider that needs this; see <see cref="DomainEventTimestamps"/>.
/// </summary>
internal sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTime>(
    static value => value.UtcDateTime,
    static value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero));

/// <summary>The nullable twin of <see cref="UtcDateTimeOffsetConverter"/>.</summary>
internal sealed class NullableUtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset?, DateTime?>(
    static value => value.HasValue ? value.Value.UtcDateTime : null,
    static value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc), TimeSpan.Zero) : null);

/// <summary>
/// Keeps the provider's own offset type but normalizes the value to UTC on the way in, so every row
/// carries an offset of zero whatever the code that wrote it passed.
/// <para>
/// This is not decoration. Npgsql refuses to write a <see cref="DateTimeOffset"/> whose offset is not
/// zero to <c>timestamp with time zone</c>, and SQL Server would happily keep <c>+02:00</c> on one row
/// and <c>+00:00</c> on the next, so the same event would read back differently on the two providers.
/// Normalizing makes the stored instant, and the value read back, identical everywhere and identical
/// to what <see cref="UtcDateTimeOffsetConverter"/> stores.
/// </para>
/// </summary>
internal sealed class UtcOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
    static value => value.ToUniversalTime(),
    static value => value);

/// <summary>The nullable twin of <see cref="UtcOffsetConverter"/>.</summary>
internal sealed class NullableUtcOffsetConverter() : ValueConverter<DateTimeOffset?, DateTimeOffset?>(
    static value => value.HasValue ? value.Value.ToUniversalTime() : null,
    static value => value);
