using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// MSBuild properties exposed to the generators through <c>CompilerVisibleProperty</c> items
/// (see build/DDDToolkit.props in the DDDToolkit package).
/// </summary>
internal sealed record DDDOptions(string ModuleName, bool AlwaysValidValueObjects)
{
    public static readonly DDDOptions Default = new(string.Empty, false);
}

internal static class DDDOptionsProvider
{
    public static IncrementalValueProvider<DDDOptions> GetDDDOptions(this IncrementalGeneratorInitializationContext context)
        => context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
        {
            provider.GlobalOptions.TryGetValue("build_property.DDD_Module", out var moduleName);
            provider.GlobalOptions.TryGetValue("build_property.DDD_AlwaysValidValueObjects", out var alwaysValid);

            return new DDDOptions(
                ModuleName: moduleName?.Trim() ?? string.Empty,
                AlwaysValidValueObjects: bool.TryParse(alwaysValid, out var parsed) && parsed);
        });

    /// <summary>The module name from MSBuild, or a name derived from the assembly name.</summary>
    public static string ResolveModuleName(this DDDOptions options, string? assemblyName)
    {
        if (!string.IsNullOrEmpty(options.ModuleName))
        {
            return options.ModuleName;
        }

        return assemblyName is null ? "AssemblyTypes" : assemblyName.Replace(".", string.Empty);
    }
}
