using DDDToolkit.Analyzers.Models;
using Microsoft.CodeAnalysis;
using SourceGeneratorsToolkit;

namespace DDDToolkit.Analyzers.Common;
public static class ProvidersExtensions
{
    public static IncrementalValueProvider<DDDOptions> DDDOptionsProvider(this IncrementalGeneratorInitializationContext context)
    {
        return context.AnalyzerConfigOptionsProvider
             .Select((provider, _) =>
             {
                 var options = provider.ToProvider("build_property.ddd_");
                 return new DDDOptions
                 {
                     ModuleName = options.GetOption("module", string.Empty),
                     AlwaysValidValueObjects = options.GetOption("AlwaysValidValueObjects", false),
                 };
             });
    }
}
