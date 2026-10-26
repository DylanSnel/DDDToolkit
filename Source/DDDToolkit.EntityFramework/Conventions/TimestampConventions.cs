using DDDToolkit.EntityFramework.Storage;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Conventions;

/// <summary>
/// Stores every timestamp of a model as the instant it names, in a UTC <see cref="DateTime"/> column,
/// which every provider can compare and order in SQL.
/// <para>
/// SQLite is the reason. It keeps a <see cref="DateTimeOffset"/> as text and refuses to compare or order
/// one, so a query such as "the grants that apply now" cannot run in the database there. The outbox and
/// the inbox solve this for their own columns; this convention applies the same converters to a whole
/// model, for code whose timestamps are compared in a query rather than only read back.
/// </para>
/// </summary>
public static class TimestampConventions
{
    /// <summary>
    /// Stores every <see cref="DateTimeOffset"/> and <see cref="DateTimeOffset"/>? property of the model,
    /// keyless and owned types included, as its UTC instant in a <see cref="DateTime"/> column, on every
    /// provider. Call it from <c>DbContext.ConfigureConventions</c>, next to <c>AddDDDToolkitConventions()</c>:
    /// <code>
    /// protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    /// {
    ///     configurationBuilder.AddDDDToolkitConventions();
    ///     configurationBuilder.StoreDateTimeOffsetsAsUtc();
    /// }
    /// </code>
    /// <para>
    /// The offset a value was written with is not kept: it reads back as the same instant with an offset
    /// of zero. A property configured with a conversion of its own in <c>OnModelCreating</c> keeps that
    /// one, as the outbox's timestamps do.
    /// </para>
    /// </summary>
    /// <param name="configurationBuilder">The context's convention configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configurationBuilder"/> is null.</exception>
    public static ModelConfigurationBuilder StoreDateTimeOffsetsAsUtc(this ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableUtcDateTimeOffsetConverter>();

        return configurationBuilder;
    }
}
