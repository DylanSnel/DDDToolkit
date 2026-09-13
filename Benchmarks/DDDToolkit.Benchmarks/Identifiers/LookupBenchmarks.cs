using BenchmarkDotNet.Attributes;
using DDDToolkit.Benchmarks.Domain;

namespace DDDToolkit.Benchmarks.Identifiers;

/// <summary>
/// Identifiers as dictionary and set keys, which is how most in-memory lookups by id are written.
/// Every probe hits, so each one pays for a hash and at least one equality check.
/// </summary>
[MemoryDiagnoser]
public class LookupBenchmarks
{
    /// <summary>How many entries the dictionary and the set hold.</summary>
    public const int Count = 10_000;

    /// <summary>How many lookups one operation performs.</summary>
    public const int Probes = 1_000;

    private Dictionary<StructOrderId, int> _structMap = [];
    private Dictionary<RecordOrderId, int> _recordMap = [];
    private HashSet<StructOrderId> _structSet = [];
    private HashSet<RecordOrderId> _recordSet = [];
    private StructOrderId[] _structProbes = [];
    private RecordOrderId[] _recordProbes = [];

    /// <summary>Fills both containers from the same values and probes the same entries in both.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var values = new Guid[Count];
        for (var i = 0; i < Count; i++)
        {
            values[i] = Guid.CreateVersion7();
        }

        _structMap = new Dictionary<StructOrderId, int>(Count);
        _recordMap = new Dictionary<RecordOrderId, int>(Count);
        _structSet = new HashSet<StructOrderId>(Count);
        _recordSet = new HashSet<RecordOrderId>(Count);

        for (var i = 0; i < Count; i++)
        {
            _structMap[new StructOrderId(values[i])] = i;
            _recordMap[RecordOrderId.From(values[i])] = i;
            _structSet.Add(new StructOrderId(values[i]));
            _recordSet.Add(RecordOrderId.From(values[i]));
        }

        var probes = new Guid[Probes];
        for (var i = 0; i < Probes; i++)
        {
            probes[i] = values[i * (Count / Probes)];
        }

        _structProbes = Array.ConvertAll(probes, static value => new StructOrderId(value));
        _recordProbes = Array.ConvertAll(probes, RecordOrderId.From);
    }

    /// <summary><see cref="Probes"/> hits against a dictionary keyed by the struct identifier.</summary>
    [Benchmark(Baseline = true)]
    public int StructDictionary()
    {
        var total = 0;
        foreach (var id in _structProbes)
        {
            total += _structMap[id];
        }

        return total;
    }

    /// <summary><see cref="Probes"/> hits against a dictionary keyed by the record identifier.</summary>
    [Benchmark]
    public int RecordDictionary()
    {
        var total = 0;
        foreach (var id in _recordProbes)
        {
            total += _recordMap[id];
        }

        return total;
    }

    /// <summary><see cref="Probes"/> hits against a set of struct identifiers.</summary>
    [Benchmark]
    public int StructHashSet()
    {
        var total = 0;
        foreach (var id in _structProbes)
        {
            if (_structSet.Contains(id))
            {
                total++;
            }
        }

        return total;
    }

    /// <summary><see cref="Probes"/> hits against a set of record identifiers.</summary>
    [Benchmark]
    public int RecordHashSet()
    {
        var total = 0;
        foreach (var id in _recordProbes)
        {
            if (_recordSet.Contains(id))
            {
                total++;
            }
        }

        return total;
    }
}
