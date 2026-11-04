using BenchmarkDotNet.Attributes;
using DDDToolkit.Benchmarks.Domain;
using DDDToolkit.Benchmarks.Infrastructure;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Benchmarks.Saving;

/// <summary>
/// One <c>SaveChanges</c> that writes one changed aggregate, on a context that tracks <see cref="Tracked"/>
/// aggregates. The toolkit's interceptors call <c>DetectChanges</c>, which compares every tracked entity with its
/// snapshot, so their cost grows with the number of tracked entities. The same save on a context without the
/// toolkit's interceptors is the baseline.
/// </summary>
[MemoryDiagnoser]
public class SaveChangesBenchmarks
{
    private SqliteConnection _connection = null!;
    private OrderContext _plain = null!;
    private OrderContext _toolkit = null!;
    private StructOrder _plainOrder = null!;
    private StructOrder _toolkitOrder = null!;
    private int _round;

    /// <summary>How many aggregates the context tracks while it saves one of them.</summary>
    [Params(10, 1_000)]
    public int Tracked { get; set; }

    /// <summary>Seeds one database, and loads every order tracked into two contexts: one with the toolkit's interceptors, one without.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using (var seed = new OrderContext(new DbContextOptionsBuilder<OrderContext>().UseSqlite(_connection).Options))
        {
            seed.Database.EnsureCreated();
            for (var i = 0; i < Tracked; i++)
            {
                seed.StructOrders.Add(new StructOrder(new StructOrderId(Guid.CreateVersion7()), "reference"));
            }

            seed.SaveChanges();
        }

        var services = new ServiceCollection().BuildServiceProvider();
        _plain = new OrderContext(new DbContextOptionsBuilder<OrderContext>().UseSqlite(_connection).Options);
        _toolkit = new OrderContext(new DbContextOptionsBuilder<OrderContext>()
            .UseSqlite(_connection)
            .AddInterceptors(
                new PublishDomainEventsInterceptor(services, new DDDEntityFrameworkOptions()),
                new InvariantInterceptor(),
                new AggregateVersionInterceptor())
            .Options);

        _plainOrder = _plain.StructOrders.ToList()[0];
        _toolkitOrder = _toolkit.StructOrders.ToList()[0];
    }

    /// <summary>Closes both contexts and the database.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _plain.Dispose();
        _toolkit.Dispose();
        _connection.Dispose();
    }

    /// <summary>The save with Entity Framework alone.</summary>
    [Benchmark(Baseline = true)]
    public int WithoutTheToolkit()
    {
        _plainOrder.ChangeReference("reference " + ++_round);
        return _plain.SaveChanges();
    }

    /// <summary>The same save through the toolkit's interceptors: domain events, invariants and the version.</summary>
    [Benchmark]
    public int WithTheToolkit()
    {
        _toolkitOrder.ChangeReference("reference " + ++_round);
        return _toolkit.SaveChanges();
    }
}
