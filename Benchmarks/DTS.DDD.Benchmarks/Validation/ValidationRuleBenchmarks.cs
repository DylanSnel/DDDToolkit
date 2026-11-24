using BenchmarkDotNet.Attributes;

namespace DTS.DDD.Benchmarks.Validation;

/// <summary>
/// The measurement of DTS.DDD.Benchmarks.FluentValidation with the rule generator: a value made from text, then asked
/// whether it is valid. The value is new on every call, so the rules run every time.
/// </summary>
[MemoryDiagnoser]
public class ValidationRuleBenchmarks
{
    /// <summary>A valid address.</summary>
    [Benchmark(Baseline = true)]
    public bool ValidValue() => RuleEmailAddress.Create("ada@example.com").IsValid;

    /// <summary>An invalid address, which has one failure.</summary>
    [Benchmark]
    public bool InvalidValue() => RuleEmailAddress.Create("not-an-address").IsValid;
}
