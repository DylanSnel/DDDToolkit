using BenchmarkDotNet.Running;

namespace DDDToolkit.Benchmarks.FluentValidation;

/// <summary>
/// Entry point. Run everything with <c>dotnet run -c Release -- --filter *</c>.
/// </summary>
public static class Program
{
    /// <summary>Hands the command line to BenchmarkDotNet's switcher.</summary>
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
