using BenchmarkDotNet.Attributes;
using DDDToolkit.Benchmarks.Domain;
using DDDToolkit.Benchmarks.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Benchmarks.Queries;

/// <summary>
/// One read by key that skips a named query filter, with the key of the filter passed two ways: a new collection
/// on every call, as <c>IgnoreQueryFilters([name])</c> writes, and one array for every call, as Tenancy's
/// <c>IgnoreTenantFilter()</c> passes. Entity Framework keeps the keys as a constant in the query, and its query
/// cache compares that constant by reference unless it is an array.
/// <para>
/// The difference depends on the version of Entity Framework. Run it once as it is, on the version the repository
/// builds with, and once on the floor the packages allow, with <c>-p:DDDDependencyVersions=Floor</c> and
/// <c>--inProcess</c>.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class QueryFilterKeyBenchmarks
{
    private static readonly string[] Keys = [FilteredOrderContext.Archived];

    private SqliteConnection _connection = null!;
    private FilteredOrderContext _context = null!;
    private StructOrderId _id;

    /// <summary>Seeds one database and opens one context, so the measurement is the query and not the context.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new FilteredOrderContext(new DbContextOptionsBuilder<FilteredOrderContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        _id = new StructOrderId(Guid.CreateVersion7());
        _context.StructOrders.Add(new StructOrder(_id, "archived"));
        _context.StructOrders.Add(new StructOrder(new StructOrderId(Guid.CreateVersion7()), "open"));
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

    /// <summary>Closes the context and the database.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>The filter's key in one array, passed on every call.</summary>
    [Benchmark(Baseline = true)]
    public StructOrder? OneArrayOfKeys()
        => _context.StructOrders.AsNoTracking().IgnoreQueryFilters(Keys).FirstOrDefault(order => order.Id == _id);

    /// <summary>The filter's key in a new collection on every call.</summary>
    [Benchmark]
    public StructOrder? NewKeysPerCall()
        => _context.StructOrders.AsNoTracking().IgnoreQueryFilters([FilteredOrderContext.Archived]).FirstOrDefault(order => order.Id == _id);
}
