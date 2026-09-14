using System.Collections.ObjectModel;
using BenchmarkDotNet.Attributes;

namespace DDDToolkit.Benchmarks.Identifiers;

/// <summary>
/// What reading a generated read-only collection property costs.
/// <para>
/// The generator emits <c>public partial IReadOnlyList&lt;T&gt; Lines =&gt; _lines.AsReadOnly();</c>, and
/// <c>AsReadOnly()</c> is <c>new ReadOnlyCollection&lt;T&gt;(this)</c> with no cache of its own, so every
/// read of the property builds a wrapper around the same list and throws it away. The wrapper is what
/// stops a caller casting the property back to <c>List&lt;T&gt;</c> and mutating the aggregate from
/// outside, so it earns its place; the question here is only whether it has to be rebuilt each time.
/// </para>
/// <para>
/// Three shapes, measured on the same list: the wrapper per read as generated today, the wrapper
/// cached in a field, and the bare field as the floor. The floor is not a candidate, because handing
/// out the list is the encapsulation hole the wrapper exists to close. It is here to say what the
/// protection costs.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class ReadOnlyViewBenchmarks
{
    /// <summary>How many elements the list holds. The wrapper is O(1), so this is here to prove it.</summary>
    [Params(4, 256)]
    public int Count { get; set; }

    /// <summary>How many times one operation reads the property, which is where the cost lives.</summary>
    public const int Reads = 16;

    private List<Guid> _lines = [];
    private ReadOnlyCollection<Guid>? _cachedView;

    [GlobalSetup]
    public void Setup()
    {
        _lines = [];
        for (var i = 0; i < Count; i++)
        {
            _lines.Add(Guid.CreateVersion7());
        }

        _cachedView = null;
    }

    /// <summary>What the property does today: a fresh wrapper on every read.</summary>
    private IReadOnlyList<Guid> PerRead => _lines.AsReadOnly();

    /// <summary>The candidate: built once and reused. Safe because the backing field is readonly, so
    /// the list the view wraps can never be swapped out from under it.</summary>
    private IReadOnlyList<Guid> Cached => _cachedView ??= _lines.AsReadOnly();

    /// <summary>The floor. Allocation free, and the reason it is not an option: this cast succeeds.</summary>
    private IReadOnlyList<Guid> Bare => _lines;

    [Benchmark(Baseline = true)]
    public int WrapperPerRead()
    {
        var total = 0;
        for (var i = 0; i < Reads; i++)
        {
            total += PerRead.Count;
        }

        return total;
    }

    [Benchmark]
    public int WrapperCached()
    {
        var total = 0;
        for (var i = 0; i < Reads; i++)
        {
            total += Cached.Count;
        }

        return total;
    }

    [Benchmark]
    public int NoWrapper()
    {
        var total = 0;
        for (var i = 0; i < Reads; i++)
        {
            total += Bare.Count;
        }

        return total;
    }
}
