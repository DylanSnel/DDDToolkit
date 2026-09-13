using BenchmarkDotNet.Attributes;
using DDDToolkit.Benchmarks.Domain;
using DDDToolkit.Benchmarks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Benchmarks.Identifiers;

/// <summary>
/// Reading aggregates back: a whole table materialised, and single rows fetched by key. Reads do not
/// change the database, so one seeded database serves every invocation; the context is new each time,
/// because a warm change tracker would answer the second lookup without touching SQLite.
/// </summary>
[MemoryDiagnoser]
public class EntityFrameworkQueryBenchmarks
{
    /// <summary>How many rows of each aggregate the database holds.</summary>
    public const int Count = 2_000;

    /// <summary>How many single-row lookups one operation performs.</summary>
    public const int Probes = 100;

    private BenchmarkDatabase _database = null!;
    private Guid[] _probes = [];

    /// <summary>Seeds both tables from the same keys and picks the rows the lookups will ask for.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _database = new BenchmarkDatabase();

        var values = new Guid[Count];
        for (var i = 0; i < Count; i++)
        {
            values[i] = Guid.CreateVersion7();
        }

        using (var context = _database.CreateContext())
        {
            for (var i = 0; i < Count; i++)
            {
                context.StructOrders.Add(new StructOrder(new StructOrderId(values[i]), "reference"));
                context.RecordOrders.Add(new RecordOrder(RecordOrderId.From(values[i]), "reference"));
            }

            context.SaveChanges();
        }

        _probes = new Guid[Probes];
        for (var i = 0; i < Probes; i++)
        {
            _probes[i] = values[i * (Count / Probes)];
        }
    }

    /// <summary>Closes the seeded database.</summary>
    [GlobalCleanup]
    public void Cleanup() => _database.Dispose();

    /// <summary>Materialises every row keyed by the struct identifier.</summary>
    [Benchmark(Baseline = true)]
    public int StructIdReadAll()
    {
        using var context = _database.CreateContext();
        return context.StructOrders.AsNoTracking().ToList().Count;
    }

    /// <summary>Materialises every row keyed by the record identifier.</summary>
    [Benchmark]
    public int RecordIdReadAll()
    {
        using var context = _database.CreateContext();
        return context.RecordOrders.AsNoTracking().ToList().Count;
    }

    /// <summary><see cref="Probes"/> single-row lookups by a struct identifier key.</summary>
    [Benchmark]
    public int StructIdFindByKey()
    {
        using var context = _database.CreateContext();
        var found = 0;
        foreach (var value in _probes)
        {
            var id = new StructOrderId(value);
            if (context.StructOrders.AsNoTracking().Single(order => order.Id == id) is not null)
            {
                found++;
            }
        }

        return found;
    }

    /// <summary><see cref="Probes"/> single-row lookups by a record identifier key.</summary>
    [Benchmark]
    public int RecordIdFindByKey()
    {
        using var context = _database.CreateContext();
        var found = 0;
        foreach (var value in _probes)
        {
            var id = RecordOrderId.From(value);
            if (context.RecordOrders.AsNoTracking().Single(order => order.Id == id) is not null)
            {
                found++;
            }
        }

        return found;
    }
}
