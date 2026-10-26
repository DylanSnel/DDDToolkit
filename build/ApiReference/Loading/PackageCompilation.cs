using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DDDToolkit.Build.ApiReference.Loading;

/// <summary>
/// The packages' assemblies, read as a compiler reads a package: as metadata references with their XML documentation,
/// in a compilation that also references everything they reference, so every type in a signature resolves to a symbol
/// and every <c>cref</c> finds what it names.
/// </summary>
internal sealed class PackageCompilation
{
    private readonly Dictionary<IAssemblySymbol, string> _paths;

    private PackageCompilation(CSharpCompilation compilation, IReadOnlyList<(IAssemblySymbol Assembly, string Path)> packages)
    {
        Compilation = compilation;
        _paths = packages.ToDictionary(package => package.Assembly, package => package.Path, (IEqualityComparer<IAssemblySymbol>)SymbolEqualityComparer.Default);
        Packages = [.. packages.Select(package => package.Assembly)];
    }

    /// <summary>The compilation the symbols come from.</summary>
    public CSharpCompilation Compilation { get; }

    /// <summary>The documented assemblies, in the order the reference names them.</summary>
    public IReadOnlyList<IAssemblySymbol> Packages { get; }

    /// <summary>The file a documented assembly was read from, with its XML and PDB beside it.</summary>
    public string PathOf(IAssemblySymbol package) => _paths[package];

    /// <summary>
    /// An assembly the packages reference, by name, with its XML documentation: one the reference takes a few types of
    /// without documenting it whole. <see langword="null"/> when no package references it.
    /// </summary>
    public IAssemblySymbol? Referenced(string name)
        => Compilation.References
            .Select(reference => Compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol)
            .FirstOrDefault(assembly => assembly?.Name == name);

    /// <summary>
    /// Loads <paramref name="projects"/>, each a project under <c>Source</c>, from its <c>bin/{configuration}/net10.0</c>
    /// folder, with the packages they restored and the framework's reference assemblies.
    /// </summary>
    public static PackageCompilation Load(string repositoryRoot, string configuration, IReadOnlyList<string> projects)
    {
        // By file name: the same DDDToolkit.dll sits in every bin folder, and a compilation that sees two copies of an
        // assembly reports every type in it as ambiguous.
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        var packageReferences = new List<(MetadataReference Reference, string Path)>();

        foreach (var project in projects)
        {
            var projectDirectory = Path.Combine(repositoryRoot, "Source", project);
            var bin = Path.Combine(projectDirectory, "bin", configuration, "net10.0");
            var assembly = Path.Combine(bin, project + ".dll");
            if (!File.Exists(assembly))
            {
                throw new InvalidOperationException($"{assembly} is missing. Build {project} in {configuration} first.");
            }

            if (!File.Exists(Path.ChangeExtension(assembly, ".xml")))
            {
                throw new InvalidOperationException($"{assembly} has no XML documentation beside it; the reference is written from it.");
            }

            var own = WithDocumentation(assembly);
            references[Path.GetFileName(assembly)] = own;
            packageReferences.Add((own, assembly));

            // The generators are copied into bin as well; they are no part of what a consumer compiles against.
            foreach (var dll in Directory.EnumerateFiles(bin, "*.dll").Where(dll => !dll.EndsWith(".Analyzers.dll", StringComparison.OrdinalIgnoreCase)))
            {
                references.TryAdd(Path.GetFileName(dll), WithDocumentation(dll));
            }

            foreach (var dll in RestoredPackages(projectDirectory))
            {
                references.TryAdd(Path.GetFileName(dll), WithDocumentation(dll));
            }
        }

        foreach (var dll in FrameworkReferenceAssemblies())
        {
            references.TryAdd(Path.GetFileName(dll), WithDocumentation(dll));
        }

        var compilation = CSharpCompilation.Create(
            "ApiReference",
            references: references.Values,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var packages = packageReferences
            .Select(package => (compilation.GetAssemblyOrModuleSymbol(package.Reference) as IAssemblySymbol
                ?? throw new InvalidOperationException($"{package.Path} did not load as an assembly."), package.Path))
            .ToList();

        return new PackageCompilation(compilation, packages);
    }

    private static MetadataReference WithDocumentation(string dll)
    {
        var xml = Path.ChangeExtension(dll, ".xml");
        return MetadataReference.CreateFromFile(dll, documentation: File.Exists(xml) ? XmlDocumentationProvider.CreateFromFile(xml) : null);
    }

    /// <summary>The compile assets of every package the project restored, from its <c>project.assets.json</c>.</summary>
    private static IEnumerable<string> RestoredPackages(string projectDirectory)
    {
        var assetsFile = Path.Combine(projectDirectory, "obj", "project.assets.json");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));
        var root = assets.RootElement;

        var folders = root.GetProperty("packageFolders").EnumerateObject().Select(folder => folder.Name).ToList();
        var libraries = root.GetProperty("libraries");

        foreach (var target in root.GetProperty("targets").EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (library.Value.GetProperty("type").GetString() != "package" || !library.Value.TryGetProperty("compile", out var compile))
                {
                    continue;
                }

                var packagePath = libraries.GetProperty(library.Name).GetProperty("path").GetString()!;
                foreach (var asset in compile.EnumerateObject().Where(asset => asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    var file = folders
                        .Select(folder => Path.Combine(folder, packagePath, asset.Name))
                        .FirstOrDefault(File.Exists);
                    if (file is not null)
                    {
                        yield return file;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The reference pack of the runtime this tool runs on: what a consumer compiles against, and with the framework's
    /// XML beside it, which an <c>inheritdoc</c> of a framework member reads. The runtime's own assemblies when no pack
    /// is installed.
    /// </summary>
    private static IEnumerable<string> FrameworkReferenceAssemblies()
    {
        var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        var dotnetRoot = runtime.Parent?.Parent?.Parent;
        var packs = dotnetRoot is null ? null : new DirectoryInfo(Path.Combine(dotnetRoot.FullName, "packs", "Microsoft.NETCore.App.Ref"));
        var major = Environment.Version.Major + ".";

        var pack = packs is { Exists: true }
            ? packs.EnumerateDirectories()
                .Where(version => version.Name.StartsWith(major, StringComparison.Ordinal))
                .OrderByDescending(version => Version.TryParse(version.Name.Split('-')[0], out var parsed) ? parsed : new Version())
                .Select(version => Path.Combine(version.FullName, "ref", $"net{Environment.Version.Major}.0"))
                .FirstOrDefault(Directory.Exists)
            : null;

        if (pack is not null)
        {
            return Directory.EnumerateFiles(pack, "*.dll");
        }

        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
    }
}
