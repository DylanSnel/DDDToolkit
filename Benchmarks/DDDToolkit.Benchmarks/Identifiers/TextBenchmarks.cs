using BenchmarkDotNet.Attributes;
using DDDToolkit.Benchmarks.Domain;

namespace DDDToolkit.Benchmarks.Identifiers;

/// <summary>
/// <c>ToString</c> and <c>Parse</c>, the two operations an identifier performs at the edge of the
/// system: writing one into a URL or a log line, and reading one back off a route.
/// </summary>
[MemoryDiagnoser]
public class TextBenchmarks
{
    private StructOrderId _structId;
    private RecordOrderId _recordId = RecordOrderId.From(Guid.Empty);
    private string _text = string.Empty;

    /// <summary>Builds one identifier of each kind and the text both of them parse.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var value = Guid.CreateVersion7();

        _structId = new StructOrderId(value);
        _recordId = RecordOrderId.From(value);
        _text = _structId.ToString();
    }

    /// <summary>The struct identifier's prefixed text.</summary>
    [Benchmark(Baseline = true)]
    public string StructToString() => _structId.ToString();

    /// <summary>The record identifier's prefixed text, from the shared base type.</summary>
    [Benchmark]
    public string RecordToString() => _recordId.ToString();

    /// <summary>Parsing the prefixed text back into a struct identifier.</summary>
    [Benchmark]
    public StructOrderId StructParse() => StructOrderId.Parse(_text);

    /// <summary>Parsing the same text back into a record identifier.</summary>
    [Benchmark]
    public RecordOrderId RecordParse() => RecordOrderId.Parse(_text);
}
