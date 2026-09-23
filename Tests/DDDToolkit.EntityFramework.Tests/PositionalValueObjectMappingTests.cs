using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Tests.Converters;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// A positional <c>[ValueObject]</c> as an EF Core complex type. Its properties are declared by the
/// generator as <c>protected init</c> rather than synthesized by the compiler, and its parameterless
/// constructor chains to the primary one; EF has to map and materialize it all the same.
/// </summary>
public sealed class PositionalValueObjectMappingTests : IDisposable
{
    private readonly SqliteDatabase _db = new();

    public PositionalValueObjectMappingTests() => _db.EnsureCreated(Create);

    public void Dispose() => _db.Dispose();

    private WarehouseContext Create() => new(_db.Options<WarehouseContext>());

    [Fact]
    public void A_positional_value_object_is_a_complex_type_and_round_trips()
    {
        var id = WarehouseId.CreateUnique();

        using (var context = Create())
        {
            context.Warehouses.Add(new Warehouse(id, new Location("Utrecht", "NL", Note: null)));
            context.SaveChanges();
        }

        using (var context = Create())
        {
            context.Model.FindEntityType(typeof(Warehouse))!.FindComplexProperty(nameof(Warehouse.Location)).Should().NotBeNull();

            var warehouse = context.Warehouses.Single(w => w.Id == id);
            warehouse.Location.Should().Be(new Location("Utrecht", "NL", Note: null));
            context.Warehouses.Count(w => w.Location.City == "Utrecht").Should().Be(1);

            warehouse.Move(warehouse.Location.With(city: "Amsterdam"));
            context.SaveChanges();
        }

        using (var context = Create())
        {
            context.Warehouses.Single(w => w.Id == id).Location.City.Should().Be("Amsterdam");
        }
    }
}

[ValueObject]
public partial record Location(string City, string Country, string? Note);

[EntityId<Guid>("WH")]
public readonly partial record struct WarehouseId;

[AggregateRoot<WarehouseId>]
public partial class Warehouse
{
    public Warehouse(WarehouseId id, Location location) : base(id) => Location = location;

    public Location Location { get; private set; }

    public void Move(Location location) => Location = location;
}

public class WarehouseContext(DbContextOptions<WarehouseContext> options) : DbContext(options)
{
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }
}
