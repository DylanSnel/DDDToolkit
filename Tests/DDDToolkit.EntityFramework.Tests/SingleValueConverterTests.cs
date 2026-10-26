using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// <c>SingleValueConverter&lt;T, TValue&gt;</c>: the one converter that stores any generated id, single value object or
/// always-valid twin as its value, through <c>ISingleValue</c>. It is what the project holding a context registers for
/// the ids of a domain or contracts project that has no Entity Framework, and so no nested converter of its own.
/// <para>
/// The types here do have nested converters; <see cref="ConsignmentContext"/> registers this one instead, so what is
/// proven is the converter itself: both directions, a query parameter, and a SQLite round trip.
/// </para>
/// </summary>
public sealed class SingleValueConverterTests : IDisposable
{
    private readonly SqliteDatabase _db = new();

    public SingleValueConverterTests() => _db.EnsureCreated(CreateContext);

    public void Dispose() => _db.Dispose();

    private ConsignmentContext CreateContext() => new(_db.Options<ConsignmentContext>());

    [Fact]
    public void A_struct_id_converts_both_ways()
    {
        var converter = new SingleValueConverter<ShelfId, Guid>();
        var id = ShelfId.CreateUnique();

        converter.ConvertToProvider(id).Should().Be(id.Value);
        converter.ConvertFromProvider(id.Value).Should().Be(id);

        var tags = new SingleValueConverter<TagId, int>();
        tags.ConvertFromProvider(7).Should().Be(new TagId(7));
    }

    [Fact]
    public void A_class_id_and_its_twin_convert_both_ways()
    {
        var id = UserId.CreateUnique();

        new SingleValueConverter<UserId, Guid>().ConvertToProvider(id).Should().Be(id.Value);
        new SingleValueConverter<UserId, Guid>().ConvertFromProvider(id.Value).Should().BeOfType<UserId>().And.Be(id);

        var twin = id.ToValid();
        new SingleValueConverter<ValidUserId, Guid>().ConvertToProvider(twin).Should().Be(id.Value);
        new SingleValueConverter<ValidUserId, Guid>().ConvertFromProvider(id.Value).Should().BeOfType<ValidUserId>().And.Be(twin);
    }

    [Fact]
    public void A_single_value_object_and_its_twin_convert_both_ways_and_the_twin_validates()
    {
        var email = EmailAddress.Create("ada@example.com");

        new SingleValueConverter<EmailAddress, string>().ConvertToProvider(email).Should().Be("ada@example.com");
        new SingleValueConverter<EmailAddress, string>().ConvertFromProvider("ada@example.com").Should().Be(email);
        new SingleValueConverter<ValidEmailAddress, string>().ConvertFromProvider("ada@example.com").Should().Be(email.ToValid());

        // Read back as the nested converter reads it: the plain type unchecked, the twin through its validating constructor.
        new SingleValueConverter<EmailAddress, string>().ConvertFromProvider("not an address").Should().NotBeNull();
        FluentActions.Invoking(() => new SingleValueConverter<ValidEmailAddress, string>().ConvertFromProvider("not an address"))
            .Should().Throw<InvalidValueObjectException>();
    }

    [Fact]
    public void Every_kind_is_written_to_SQLite_read_back_and_compared_as_a_query_parameter()
    {
        var id = ShelfId.CreateUnique();
        var owner = UserId.CreateUnique();
        var contact = EmailAddress.Create("ada@example.com");

        using (var context = CreateContext())
        {
            context.Consignments.Add(new Consignment { Id = id, Owner = owner, Contact = contact, Confirmed = contact.ToValid(), Tag = new TagId(3) });
            context.Consignments.Add(new Consignment { Id = ShelfId.CreateUnique(), Owner = UserId.CreateUnique(), Contact = EmailAddress.Create("bob@example.com"), Tag = new TagId(4) });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var consignment = context.Consignments.Single(candidate => candidate.Id == id);
            consignment.Owner.Should().BeOfType<UserId>().And.Be(owner);
            consignment.Contact.Should().Be(contact);
            consignment.Confirmed.Should().BeOfType<ValidEmailAddress>().And.Be(contact.ToValid());
            consignment.Tag.Should().Be(new TagId(3));

            context.Consignments.Count(candidate => candidate.Owner == owner).Should().Be(1);
            context.Consignments.Count(candidate => candidate.Contact == contact).Should().Be(1);
            context.Consignments.Count(candidate => candidate.Tag == new TagId(4)).Should().Be(1);
            context.Consignments.Count(candidate => candidate.Confirmed == null).Should().Be(1);
        }

        // The column holds the value, as the nested converters store it.
        using var command = _db.Connection.CreateCommand();
        command.CommandText = "SELECT \"Contact\" FROM \"Consignments\" WHERE \"Tag\" = 3";
        command.ExecuteScalar().Should().Be("ada@example.com");
    }
}

/// <summary>A row of every kind of single value, stored through <see cref="SingleValueConverter{T, TValue}"/>.</summary>
public sealed class Consignment
{
    public ShelfId Id { get; set; }

    public UserId Owner { get; set; } = null!;

    public EmailAddress Contact { get; set; } = null!;

    public ValidEmailAddress? Confirmed { get; set; }

    public TagId Tag { get; set; }
}

/// <summary>A plain context that registers <see cref="SingleValueConverter{T, TValue}"/> for every type it stores.</summary>
public sealed class ConsignmentContext(DbContextOptions<ConsignmentContext> options) : DbContext(options)
{
    public DbSet<Consignment> Consignments => Set<Consignment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        Store<ShelfId, Guid>(configurationBuilder);
        Store<TagId, int>(configurationBuilder);
        Store<UserId, Guid>(configurationBuilder);
        Store<EmailAddress, string>(configurationBuilder);
        Store<ValidEmailAddress, string>(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Consignment>().HasKey(consignment => consignment.Id);

    /// <summary>What the generated <c>Add{Module}Converters()</c> writes for a type of another project.</summary>
    private static void Store<T, TValue>(ModelConfigurationBuilder configurationBuilder)
        where T : DDDToolkit.Interfaces.ISingleValue<T, TValue>
    {
        configurationBuilder.Properties<T>().HaveConversion<SingleValueConverter<T, TValue>>();
        configurationBuilder.DefaultTypeMapping<T>().HasConversion<SingleValueConverter<T, TValue>>();
    }
}
