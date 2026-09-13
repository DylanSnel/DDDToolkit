using BenchmarkDotNet.Running;

namespace DDDToolkit.Benchmarks;

/// <summary>
/// Entry point. Run everything with <c>dotnet run -c Release -- --filter *</c>, or one class with
/// <c>--filter *LookupBenchmarks*</c>. The numbers in <c>docs/performance.md</c> came from the
/// former.
/// </summary>
public static class Program
{
    /// <summary>Hands the command line to BenchmarkDotNet's switcher.</summary>
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
