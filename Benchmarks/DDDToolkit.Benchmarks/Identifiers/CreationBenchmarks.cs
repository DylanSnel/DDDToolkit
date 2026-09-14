using BenchmarkDotNet.Attributes;
using DDDToolkit.Benchmarks.Domain;

namespace DDDToolkit.Benchmarks.Identifiers;

/// <summary>
/// Creating <see cref="Count"/> identifiers from values you already hold, which is what happens on
/// every row a query materialises. The <c>Guid</c> baseline is there so you can see how much of the
/// cost is the wrapper and how much is the array underneath it.
/// </summary>
[MemoryDiagnoser]
public class CreationBenchmarks
{
    /// <summary>How many identifiers one operation creates.</summary>
    public const int Count = 10_000;

    private Guid[] _values = [];

    /// <summary>Pre-generates the raw values, so no benchmark pays for <c>Guid.NewGuid</c>.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _values = new Guid[Count];
        for (var i = 0; i < Count; i++)
        {
            _values[i] = Guid.CreateVersion7();
        }
    }

    /// <summary>The floor: an array of the raw values, wrapped in nothing.</summary>
    [Benchmark(Baseline = true)]
    public Guid[] RawGuid()
    {
        var result = new Guid[Count];
        for (var i = 0; i < Count; i++)
        {
            result[i] = _values[i];
        }

        return result;
    }

    /// <summary>The struct identifier: the same array with a type on it.</summary>
    [Benchmark]
    public StructOrderId[] StructId()
    {
        var result = new StructOrderId[Count];
        for (var i = 0; i < Count; i++)
        {
            result[i] = new StructOrderId(_values[i]);
        }

        return result;
    }

    /// <summary>The record identifier: an array of references plus one object per identifier.</summary>
    [Benchmark]
    public RecordOrderId[] RecordId()
    {
        var result = new RecordOrderId[Count];
        for (var i = 0; i < Count; i++)
        {
            result[i] = RecordOrderId.From(_values[i]);
        }

        return result;
    }
}
