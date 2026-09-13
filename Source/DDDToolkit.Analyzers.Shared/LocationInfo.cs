using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// A cacheable stand-in for <see cref="Location"/>. Locations hold on to syntax trees, which would
/// defeat incremental caching; this record keeps only what is needed to recreate one.
/// </summary>
internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? From(Location? location)
    {
        if (location is null || location.SourceTree is null)
        {
            return null;
        }

        return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    public static LocationInfo? From(SyntaxNode node) => From(node.GetLocation());

    public static LocationInfo? From(SyntaxToken token) => From(token.GetLocation());

    public static LocationInfo? From(ISymbol symbol) => From(symbol.Locations.FirstOrDefault());
}

/// <summary>
/// A cacheable diagnostic. Reported from the output step so the pipeline stays incremental.
/// </summary>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> MessageArguments)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] messageArguments)
        => new(descriptor, location, new EquatableArray<string>(messageArguments));

    public bool IsError => Descriptor.DefaultSeverity == DiagnosticSeverity.Error;

    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(
            Descriptor,
            Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None,
            MessageArguments.Select(argument => (object)argument).ToArray());

    public void Report(SourceProductionContext context) => context.ReportDiagnostic(ToDiagnostic());
}

internal static class DiagnosticInfoExtensions
{
    public static void ReportAll(this EquatableArray<DiagnosticInfo> diagnostics, SourceProductionContext context)
    {
        foreach (var diagnostic in diagnostics)
        {
            diagnostic.Report(context);
        }
    }

    public static bool HasErrors(this EquatableArray<DiagnosticInfo> diagnostics) => diagnostics.Any(d => d.IsError);
}
