using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// MSBuild properties exposed to the generators through <c>CompilerVisibleProperty</c> items, which
/// build/ and buildTransitive/&lt;package id&gt;.props inside the DDDToolkit.Analyzers package declare.
/// </summary>
/// <param name="ModuleName">The project's <c>DDD_Module</c>, or empty when it sets none.</param>
/// <param name="PropertiesDeclared">
/// Whether the properties reached the compiler at all. A property that is declared and not set arrives
/// as an empty value; one that is not declared does not arrive, and that only happens when the props
/// file was not imported. The generators then ignore whatever the project sets, which is DDD00014.
/// </param>
internal sealed record DDDOptions(string ModuleName, bool PropertiesDeclared)
{
    public static readonly DDDOptions Default = new(string.Empty, PropertiesDeclared: true);
}

internal static class DDDOptionsProvider
{
    public static IncrementalValueProvider<DDDOptions> GetDDDOptions(this IncrementalGeneratorInitializationContext context)
        => context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
        {
            var declared = provider.GlobalOptions.TryGetValue("build_property.DDD_Module", out var moduleName);

            return new DDDOptions(ModuleName: moduleName?.Trim() ?? string.Empty, PropertiesDeclared: declared);
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
