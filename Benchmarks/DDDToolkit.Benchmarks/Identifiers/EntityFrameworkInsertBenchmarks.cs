using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using DDDToolkit.Benchmarks.Domain;
using DDDToolkit.Benchmarks.Infrastructure;

namespace DDDToolkit.Benchmarks.Identifiers;

/// <summary>
/// Inserting <see cref="Count"/> aggregates and saving them, once per invocation against a database
/// created for that invocation. Both aggregates have the same shape and the same payload column;
/// the key type is the only difference.
/// <para>
/// <c>InvocationCount(1)</c> with an iteration setup is how a benchmark that mutates a database stays
/// honest: every measured invocation starts from an empty table, so no run is helped or hurt by what
/// the run before it wrote.
/// </para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, warmupCount: 5, iterationCount: 40, invocationCount: 1)]
public class EntityFrameworkInsertBenchmarks
{
    /// <summary>How many aggregates one operation inserts.</summary>
    public const int Count = 1_000;

    private Guid[] _values = [];
    private BenchmarkDatabase _database = null!;

    /// <summary>Pre-generates the keys, so no measured invocation pays for <c>Guid.CreateVersion7</c>.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _values = new Guid[Count];
        for (var i = 0; i < Count; i++)
        {
            _values[i] = Guid.CreateVersion7();
        }
    }

    /// <summary>A database per invocation, so every insert starts from an empty table.</summary>
    [IterationSetup]
    public void IterationSetup() => _database = new BenchmarkDatabase();

    /// <summary>Drops the database the invocation wrote to.</summary>
    [IterationCleanup]
    public void IterationCleanup() => _database.Dispose();

    /// <summary>Inserts <see cref="Count"/> aggregates keyed by the struct identifier.</summary>
    [Benchmark(Baseline = true)]
    public int StructId()
    {
        using var context = _database.CreateContext();
        for (var i = 0; i < Count; i++)
        {
            context.StructOrders.Add(new StructOrder(new StructOrderId(_values[i]), "reference"));
        }

        return context.SaveChanges();
    }

    /// <summary>Inserts <see cref="Count"/> aggregates keyed by the record identifier.</summary>
    [Benchmark]
    public int RecordId()
    {
        using var context = _database.CreateContext();
        for (var i = 0; i < Count; i++)
        {
            context.RecordOrders.Add(new RecordOrder(RecordOrderId.From(_values[i]), "reference"));
        }

        return context.SaveChanges();
    }
}
