using System.Reflection;
using System.Text.RegularExpressions;
using DDDToolkit.Build.ApiReference.Loading;
using DDDToolkit.Build.ApiReference.Model;
using DDDToolkit.Build.ApiReference.Pages;
using Microsoft.CodeAnalysis;

// Writes docs/tenancy/reference/ from the Tenancy packages' assemblies and XML documentation. The project builds the
// packages first; build/generate-api-reference.sh runs it.
var root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "DDDToolkit.slnx")))
{
    root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("The tool runs from inside the repository.");
}

var configuration = typeof(PageWriter).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "PackagesConfiguration").Value!;

var packages = PackageCompilation.Load(root, configuration, ReferencePlan.Assemblies);
var documentation = new Documentation(packages.Compilation);
var surface = new ApiSurface();
var layout = ReferenceLayout.Build(packages, surface, documentation);
var writer = new PageWriter(documentation, layout);

var output = Path.Combine(root, "docs", "tenancy", "reference");
Directory.CreateDirectory(output);

// A page the plan no longer has would stay on the site, out of date. Only a page this tool wrote goes: it says so at the top.
foreach (var stale in Directory.EnumerateFiles(output, "*.md")
             .Where(file => ReferencePlan.Pages.All(page => page.File != Path.GetFileName(file)))
             .Where(file => File.ReadAllText(file).Contains(PageWriter.GeneratedMarker, StringComparison.Ordinal)))
{
    File.Delete(stale);
}

foreach (var page in layout.Pages)
{
    var intro = Regex.Replace(page.Plan.Intro, Regex.Escape(PageWriter.TypeParameterTable) + @"(\w+)\}", match =>
    {
        var type = layout.Locations.Keys.OfType<INamedTypeSymbol>().Single(symbol => symbol.Name == match.Groups[1].Value && symbol.ContainingType is null);
        return writer.TypeParameters(type);
    });

    var markdown = writer.Write(page, intro);
    File.WriteAllText(Path.Combine(output, page.Plan.File), markdown);
    Console.WriteLine($"{page.Plan.File}: {page.Groups.Sum(group => group.Types.Count)} types, {markdown.Split('\n').Length} lines");
}

var types = layout.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Types).ToList();
Console.WriteLine();
Console.WriteLine($"{types.Count} types, {types.Sum(type => type.Members.Count)} members.");
Console.WriteLine($"Undocumented: {writer.UndocumentedTypes.Count} types, {writer.UndocumentedMembers.Count} members.");
foreach (var id in writer.UndocumentedTypes.Concat(writer.UndocumentedMembers))
{
    Console.WriteLine("  " + id);
}

Console.WriteLine($"Documented comments that leave out a parameter or type parameter: {writer.MissingTags.Count}.");
foreach (var missing in writer.MissingTags)
{
    Console.WriteLine("  " + missing);
}

Console.WriteLine($"Tags naming a parameter that is not there: {writer.StaleTags.Count}.");
foreach (var stale in writer.StaleTags)
{
    Console.WriteLine("  " + stale);
}

Console.WriteLine($"Left out: {surface.CompilerWritten} members the compiler wrote, and {surface.Skipped.Count} hidden from the editor.");
foreach (var (symbol, reason) in surface.Skipped)
{
    Console.WriteLine($"  {reason} {symbol.ToDisplayString()}");
}

foreach (var type in layout.Unclaimed)
{
    Console.WriteLine($"warning: no group of the plan takes {type.ToDisplayString()}; it is under Other. Add its folder to Pages/ReferencePlan.cs.");
}
