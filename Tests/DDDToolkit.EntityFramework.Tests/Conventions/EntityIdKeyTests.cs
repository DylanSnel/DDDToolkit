using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Tests.Converters;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Tests.Conventions;

/// <summary>
/// The database never makes an id. An id is made in code before the save, <c>VoucherNumber.Create()</c>, and Entity
/// Framework generates a key's value only for a key whose own type is a number or a <see cref="Guid"/>: an id is a
/// type of its own, stored through its converter, so its key is a value the application gives, over a long as
/// over a Guid, with the toolkit's conventions and without. A provider that started making ids would fail here.
/// </summary>
public sealed class EntityIdKeyTests : IDisposable
{
    private readonly SqliteDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private static IProperty Key<TEntity>(DbContext context)
        => context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))!.FindPrimaryKey()!.Properties.Single();

    private static DbContextOptions<TContext> OnPostgres<TContext>()
        where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>().UseNpgsql("Host=nowhere;Database=nothing").Options;

    [Fact]
    public void The_key_of_an_id_over_a_long_is_never_made_by_postgres_with_or_without_the_conventions()
    {
        using var withConventions = new VoucherContext(OnPostgres<VoucherContext>());
        using var convertersAlone = new PlainVoucherContext(OnPostgres<PlainVoucherContext>());

        foreach (var context in new DbContext[] { withConventions, convertersAlone })
        {
            Key<Voucher>(context).ValueGenerated.Should().Be(ValueGenerated.Never, "the key's own type is the id, which nothing generates");
            context.Database.GenerateCreateScript().Should().NotContain("IDENTITY").And.NotContain("serial");
        }
    }

    [Fact]
    public void The_key_of_an_id_over_a_guid_is_never_made_by_postgres_either()
    {
        using var context = new VoucherContext(OnPostgres<VoucherContext>());

        Key<Crate>(context).ValueGenerated.Should().Be(ValueGenerated.Never);
        context.Database.GenerateCreateScript().Should().NotContain("DEFAULT", "no column of the model has the database make a value");
    }

    [Fact]
    public void A_new_row_keeps_the_id_it_was_given_on_sqlite()
    {
        _db.EnsureCreated(() => new VoucherContext(_db.Options<VoucherContext>()));

        using (var context = new VoucherContext(_db.Options<VoucherContext>()))
        {
            context.Vouchers.Add(new Voucher(new VoucherNumber(0), "nothing yet"));
            context.Vouchers.Add(new Voucher(new VoucherNumber(42), "the answer"));
            context.SaveChanges();
        }

        // A key SQLite made would number the first row itself: 1, not the 0 it was given.
        using var reading = new VoucherContext(_db.Options<VoucherContext>());
        reading.Vouchers.AsEnumerable().Select(voucher => voucher.Id.Value).Should().BeEquivalentTo([0L, 42L]);
    }

    /// <summary>The vouchers and crates with the toolkit's conventions and the generated converters, as a module's context has them.</summary>
    private sealed class VoucherContext(DbContextOptions<VoucherContext> options) : DbContext(options)
    {
        public DbSet<Voucher> Vouchers => Set<Voucher>();

        public DbSet<Crate> Crates => Set<Crate>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddEfTestsConverters();
        }
    }

    /// <summary>The vouchers with the converters alone: what decides it is Entity Framework's, not a convention of the toolkit's.</summary>
    private sealed class PlainVoucherContext(DbContextOptions<PlainVoucherContext> options) : DbContext(options)
    {
        public DbSet<Voucher> Vouchers => Set<Voucher>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
            => configurationBuilder.AddEfTestsConverters();
    }
}

/// <summary>A voucher's number: an id over a long, which nothing but the application makes.</summary>
[EntityId<long>]
public readonly partial record struct VoucherNumber;

/// <summary>A voucher, keyed by its number.</summary>
[AggregateRoot<VoucherNumber>]
public sealed partial class Voucher
{
    /// <summary>A voucher with the number it was given.</summary>
    public Voucher(VoucherNumber id, string title) : base(id) => Title = title;

    /// <summary>What the voucher is for.</summary>
    public string Title { get; private set; }
}

/// <summary>A crate's id, over a Guid.</summary>
[EntityId<Guid>]
public readonly partial record struct CrateId;

/// <summary>A crate.</summary>
[AggregateRoot<CrateId>]
public sealed partial class Crate
{
    /// <summary>A crate with the id it was given.</summary>
    public Crate(CrateId id) : base(id)
    {
    }
}
