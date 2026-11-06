using BenchmarkDotNet.Attributes;

namespace DTS.DDD.Benchmarks.FluentValidation;

/// <summary>
/// One value object validated through the FluentValidation generator: made from text, then asked whether it is
/// valid, as a value from a request is. The value is new on every call, so the validation runs every time.
/// </summary>
[MemoryDiagnoser]
public class ValidationBenchmarks
{
    /// <summary>A valid address: the path most values take.</summary>
    [Benchmark(Baseline = true)]
    public bool ValidValue() => EmailAddress.Create("ada@example.com").IsValid;

    /// <summary>An invalid address, which has one failure.</summary>
    [Benchmark]
    public bool InvalidValue() => EmailAddress.Create("not-an-address").IsValid;
}
