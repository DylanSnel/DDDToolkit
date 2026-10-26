using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests.Conventions;

/// <summary>
/// <c>StoreDateTimeOffsetsAsUtc()</c>: every timestamp of a model becomes a UTC <see cref="DateTime"/>
/// column, so SQLite, which keeps a <see cref="DateTimeOffset"/> as text, can compare and order it in SQL.
/// </summary>
public sealed class TimestampConventionTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabase _db = new();

    public TimestampConventionTests() => _db.EnsureCreated(CreateContext);

    public void Dispose() => _db.Dispose();

    private VoyageContext CreateContext() => new(_db.Options<VoyageContext>());

    [Fact]
    public void Every_DateTimeOffset_property_is_a_utc_DateTime_column_including_keyless_and_owned()
    {
        using var context = CreateContext();

        var timestamps = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
            .ToList();

        timestamps.Select(property => property.DeclaringType.ClrType.Name + "." + property.Name).Should().BeEquivalentTo(
            "Voyage.StartsAt", "Voyage.EndsAt", "Stop.ReachedAt", "VoyageRow.StartsAt", "VoyageRow.EndsAt");

        foreach (var property in timestamps)
        {
            var converter = property.GetValueConverter();
            converter.Should().BeOfType(
                property.ClrType == typeof(DateTimeOffset) ? typeof(UtcDateTimeOffsetConverter) : typeof(NullableUtcDateTimeOffsetConverter),
                property.Name + " is a timestamp");
            converter!.ProviderClrType.Should().Be(property.IsNullable ? typeof(DateTime?) : typeof(DateTime));
        }
    }

    [Fact]
    public void A_DateTimeOffset_comparison_translates_on_sqlite()
    {
        using (var context = CreateContext())
        {
            context.Voyages.AddRange(
                new Voyage { Id = 1, StartsAt = Noon.AddDays(-2), EndsAt = Noon.AddDays(-1) },
                new Voyage { Id = 2, StartsAt = Noon.AddDays(-1), EndsAt = null },
                new Voyage { Id = 3, StartsAt = Noon.AddHours(-1), EndsAt = Noon.AddHours(1) },
                new Voyage { Id = 4, StartsAt = Noon.AddDays(1), EndsAt = null });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var now = Noon;

            context.Voyages
                .Where(voyage => voyage.StartsAt <= now && (voyage.EndsAt == null || voyage.EndsAt > now))
                .OrderByDescending(voyage => voyage.StartsAt)
                .Select(voyage => voyage.Id)
                .ToList()
                .Should().Equal(3, 2);

            // The keyless view over the same table compares in SQL as well.
            context.Set<VoyageRow>().Count(row => row.EndsAt != null && row.EndsAt <= now).Should().Be(1);
        }

        // Without the convention, SQLite has nothing to compare the text with.
        using var plain = new PlainVoyageContext(_db.Options<PlainVoyageContext>());
        var act = () => plain.Voyages.Where(voyage => voyage.StartsAt <= Noon).ToList();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_instant_round_trips_with_offset_zero()
    {
        var written = new DateTimeOffset(2026, 9, 29, 14, 30, 0, TimeSpan.FromHours(2));

        using (var context = CreateContext())
        {
            var voyage = new Voyage { Id = 1, StartsAt = written, EndsAt = written.AddHours(1) };
            voyage.Stops.Add(new Stop { Number = 1, ReachedAt = written });
            context.Voyages.Add(voyage);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var voyage = context.Voyages.Single();

            voyage.StartsAt.Should().Be(written, "it is the same instant");
            voyage.StartsAt.Offset.Should().Be(TimeSpan.Zero);
            voyage.StartsAt.UtcDateTime.Should().Be(new DateTime(2026, 9, 29, 12, 30, 0, DateTimeKind.Utc));
            voyage.EndsAt!.Value.Offset.Should().Be(TimeSpan.Zero);
            voyage.Stops.Single().ReachedAt.Should().Be(written);
            voyage.Stops.Single().ReachedAt.Offset.Should().Be(TimeSpan.Zero);
        }
    }

    public sealed class Voyage
    {
        public int Id { get; set; }

        public DateTimeOffset StartsAt { get; set; }

        public DateTimeOffset? EndsAt { get; set; }

        public List<Stop> Stops { get; } = [];
    }

    public sealed class Stop
    {
        public int Number { get; set; }

        public DateTimeOffset ReachedAt { get; set; }
    }

    public sealed class VoyageRow
    {
        public DateTimeOffset StartsAt { get; set; }

        public DateTimeOffset? EndsAt { get; set; }
    }

    /// <summary>
    /// A voyage with an owned collection of stops and a keyless view over its table: the three places a
    /// timestamp can sit.
    /// </summary>
    public abstract class VoyageModel(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Voyage> Voyages => Set<Voyage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Voyage>(voyage =>
            {
                voyage.ToTable("Voyages");
                voyage.Property(v => v.Id).ValueGeneratedNever();
                voyage.OwnsMany(v => v.Stops, stop =>
                {
                    stop.ToTable("Stops");
                    stop.WithOwner().HasForeignKey("VoyageId");
                    stop.HasKey("VoyageId", nameof(Stop.Number));
                });
            });

            modelBuilder.Entity<VoyageRow>().HasNoKey().ToView("Voyages");
        }
    }

    /// <summary>The model with the convention.</summary>
    public sealed class VoyageContext(DbContextOptions<VoyageContext> options) : VoyageModel(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.StoreDateTimeOffsetsAsUtc();
        }
    }

    /// <summary>The same model without it: what SQLite does with a timestamp left as it is.</summary>
    public sealed class PlainVoyageContext(DbContextOptions<PlainVoyageContext> options) : VoyageModel(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
            => configurationBuilder.AddDDDToolkitConventions();
    }
}
