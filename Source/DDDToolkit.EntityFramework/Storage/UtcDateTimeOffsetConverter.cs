using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DDDToolkit.EntityFramework.Storage;

/// <summary>
/// Stores a <see cref="DateTimeOffset"/> as its UTC instant, so every provider can index and order the
/// column. Shared by the outbox and the inbox.
/// </summary>
internal sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTime>(
    static value => value.UtcDateTime,
    static value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero));

/// <summary>The nullable twin of <see cref="UtcDateTimeOffsetConverter"/>.</summary>
internal sealed class NullableUtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset?, DateTime?>(
    static value => value.HasValue ? value.Value.UtcDateTime : null,
    static value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc), TimeSpan.Zero) : null);
