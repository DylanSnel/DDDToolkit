using BenchmarkDotNet.Attributes;
using DDDToolkit.Benchmarks.Domain;

namespace DDDToolkit.Benchmarks.Identifiers;

/// <summary>
/// One equality check and one hash, on their own. These are the two operations every dictionary,
/// set, <c>Distinct</c> and <c>GroupBy</c> is built out of, so it is worth seeing them undiluted.
/// </summary>
[MemoryDiagnoser]
public class EqualityBenchmarks
{
    private StructOrderId _structLeft;
    private StructOrderId _structRight;
    private RecordOrderId _recordLeft = RecordOrderId.From(Guid.Empty);
    private RecordOrderId _recordRight = RecordOrderId.From(Guid.Empty);

    /// <summary>Builds two distinct instances that carry the same value, so equality has to compare.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var value = Guid.CreateVersion7();

        _structLeft = new StructOrderId(value);
        _structRight = new StructOrderId(value);
        _recordLeft = RecordOrderId.From(value);

        // A separate instance: reference equality must not short-circuit the comparison.
        _recordRight = RecordOrderId.From(value);
    }

    /// <summary>Struct equality, which the language writes field by field.</summary>
    [Benchmark(Baseline = true)]
    public bool StructEquals() => _structLeft == _structRight;

    /// <summary>Record equality, which the toolkit routes through <c>GetEqualityComponents</c>.</summary>
    [Benchmark]
    public bool RecordEquals() => _recordLeft == _recordRight;

    /// <summary>The struct identifier's hash.</summary>
    [Benchmark]
    public int StructHashCode() => _structLeft.GetHashCode();

    /// <summary>The record identifier's hash, also computed from <c>GetEqualityComponents</c>.</summary>
    [Benchmark]
    public int RecordHashCode() => _recordLeft.GetHashCode();
}
