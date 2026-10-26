using System.Reflection;
using DDDToolkit.Build.ApiReference.Loading;
using DDDToolkit.Build.ApiReference.Model;
using DDDToolkit.Build.ApiReference.Pages;

// Writes the API reference of every supporting domain, docs/tenancy/reference/ and docs/membership/reference/, from the
// packages' assemblies and XML documentation. The project builds the packages first; build/generate-api-reference.sh runs it.
var root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "DDDToolkit.slnx")))
{
    root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("The tool runs from inside the repository.");
}

var configuration = typeof(PageWriter).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "PackagesConfiguration").Value!;

foreach (var plan in ReferencePlan.All)
{
    Write(plan);
}

void Write(ReferencePlan plan)
{
    // A compilation per domain, of its own packages: what a consumer of that domain compiles against.
    var packages = PackageCompilation.Load(root, configuration, plan.Assemblies);
    var documentation = new Documentation(packages.Compilation);
    var surface = new ApiSurface();
    var layout = ReferenceLayout.Build(plan, packages, surface, documentation);
    var writer = new PageWriter(documentation, layout);

    var output = Path.Combine([root, .. plan.Folder.Split('/')]);
    Directory.CreateDirectory(output);
    Console.WriteLine($"{plan.Domain}, in {plan.Folder}:");

    // A page the plan no longer has would stay on the site, out of date. Only a page this tool wrote goes: it says so at the top.
    foreach (var stale in Directory.EnumerateFiles(output, "*.md")
                 .Where(file => plan.Pages.All(page => page.File != Path.GetFileName(file)))
                 .Where(file => File.ReadAllText(file).Contains(PageWriter.GeneratedMarker, StringComparison.Ordinal)))
    {
        File.Delete(stale);
    }

    foreach (var page in layout.Pages)
    {
        var markdown = writer.Write(page);
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
        Console.WriteLine($"warning: no group of the plan takes {type.ToDisplayString()}; it is under Other. Add its folder to Pages/{plan.Domain}Reference.cs.");
    }

    Console.WriteLine();
}
