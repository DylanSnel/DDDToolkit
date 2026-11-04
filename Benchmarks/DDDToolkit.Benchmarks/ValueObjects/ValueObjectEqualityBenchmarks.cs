using BenchmarkDotNet.Attributes;
using DDDToolkit.Benchmarks.Domain;

namespace DDDToolkit.Benchmarks.ValueObjects;

/// <summary>
/// The equality of a <c>[ValueObject]</c> with two properties: one comparison, one hash, and the lookups a
/// dictionary keyed by the value object makes.
/// </summary>
[MemoryDiagnoser]
public class ValueObjectEqualityBenchmarks
{
    /// <summary>How many lookups one operation of <see cref="DictionaryLookup"/> makes.</summary>
    public const int Lookups = 1_000;

    private Money _left = null!;
    private Money _right = null!;
    private Dictionary<Money, int> _prices = [];
    private Money[] _probes = [];

    /// <summary>Builds two equal values, and a dictionary with an entry for each probe.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _left = new Money(12.50m, "EUR");
        _right = new Money(12.50m, "EUR");

        _probes = new Money[Lookups];
        for (var i = 0; i < Lookups; i++)
        {
            _probes[i] = new Money(i, "EUR");
            _prices[new Money(i, "EUR")] = i;
        }
    }

    /// <summary>Two equal values that are different objects, so every property is compared.</summary>
    [Benchmark(Baseline = true)]
    public bool EqualsSameValue() => _left.Equals(_right);

    /// <summary>The hash of one value.</summary>
    [Benchmark]
    public int HashCode() => _left.GetHashCode();

    /// <summary><see cref="Lookups"/> lookups that hit, each a hash and at least one comparison.</summary>
    [Benchmark]
    public int DictionaryLookup()
    {
        var found = 0;
        foreach (var probe in _probes)
        {
            if (_prices.ContainsKey(probe))
            {
                found++;
            }
        }

        return found;
    }
}
