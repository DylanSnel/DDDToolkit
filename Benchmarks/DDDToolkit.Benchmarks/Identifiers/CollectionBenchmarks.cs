using BenchmarkDotNet.Attributes;
using DDDToolkit.Benchmarks.Domain;

namespace DDDToolkit.Benchmarks.Identifiers;

/// <summary>
/// Walking a list of identifiers and comparing each one, which is what
/// <c>lines.Any(line =&gt; line.Product == product)</c> compiles down to. The lists are built once, so
/// this measures reading and comparing, not allocating.
/// </summary>
[MemoryDiagnoser]
public class CollectionBenchmarks
{
    /// <summary>How many identifiers one operation walks.</summary>
    public const int Count = 10_000;

    private StructOrderId[] _structIds = [];
    private RecordOrderId[] _recordIds = [];
    private StructOrderId _structNeedle;
    private RecordOrderId _recordNeedle = RecordOrderId.From(Guid.Empty);

    /// <summary>Builds both lists from the same values and picks the same element as the needle.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var values = new Guid[Count];
        for (var i = 0; i < Count; i++)
        {
            values[i] = Guid.CreateVersion7();
        }

        _structIds = Array.ConvertAll(values, static value => new StructOrderId(value));
        _recordIds = Array.ConvertAll(values, RecordOrderId.From);

        // The last element, so every comparison in the scan runs before the match is found.
        _structNeedle = new StructOrderId(values[^1]);
        _recordNeedle = RecordOrderId.From(values[^1]);
    }

    /// <summary>Scans the struct list, comparing every element.</summary>
    [Benchmark(Baseline = true)]
    public int StructId()
    {
        var matches = 0;
        foreach (var id in _structIds)
        {
            if (id == _structNeedle)
            {
                matches++;
            }
        }

        return matches;
    }

    /// <summary>Scans the record list, comparing every element.</summary>
    [Benchmark]
    public int RecordId()
    {
        var matches = 0;
        foreach (var id in _recordIds)
        {
            if (id == _recordNeedle)
            {
                matches++;
            }
        }

        return matches;
    }

    /// <summary>Sums the underlying values, so the loop touches each identifier's payload.</summary>
    [Benchmark]
    public int StructIdRead()
    {
        var total = 0;
        foreach (var id in _structIds)
        {
            total += id.Value.GetHashCode();
        }

        return total;
    }

    /// <summary>The same read through a reference, which costs one dereference per element.</summary>
    [Benchmark]
    public int RecordIdRead()
    {
        var total = 0;
        foreach (var id in _recordIds)
        {
            total += id.Value.GetHashCode();
        }

        return total;
    }
}
