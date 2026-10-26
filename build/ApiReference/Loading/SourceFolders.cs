using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Build.ApiReference.Loading;

/// <summary>
/// The folder of the project each type is written in, read from the assembly's portable PDB. The namespaces are too
/// coarse to group a page by, since every aggregate and its events share one, and the folders are what the authors
/// grouped the code by. The PDB names every source document a type's methods were compiled from, and a type without a
/// method body, such as an enum, is named by its <c>TypeDefinitionDocuments</c> record.
/// </summary>
internal sealed class SourceFolders
{
    // The custom debug information Roslyn writes for a type that has no method bodies to name its documents.
    private static readonly Guid TypeDefinitionDocuments = new("932E74BC-DBA9-4478-8D46-0F32A7BAB3D3");

    // Per type, by metadata name, the paths of its own sources, relative to the project.
    private readonly Dictionary<string, List<string>> _paths;

    private SourceFolders(Dictionary<string, List<string>> paths) => _paths = paths;

    /// <summary>
    /// Reads the PDB beside <paramref name="assemblyPath"/>; <paramref name="projectName"/> is the project's folder name,
    /// which every document path of its own sources contains, mapped or not.
    /// </summary>
    public static SourceFolders Read(string assemblyPath, string projectName)
    {
        using var peStream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(peStream);
        var metadata = pe.GetMetadataReader();

        using var pdbStream = File.OpenRead(Path.ChangeExtension(assemblyPath, ".pdb"));
        using var pdbProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
        var pdb = pdbProvider.GetMetadataReader();

        var documents = new Dictionary<TypeDefinitionHandle, HashSet<DocumentHandle>>();
        void Add(Dictionary<TypeDefinitionHandle, HashSet<DocumentHandle>> into, TypeDefinitionHandle type, DocumentHandle document)
        {
            if (!into.TryGetValue(type, out var set))
            {
                into[type] = set = [];
            }

            set.Add(document);
        }

        // The bodies of a type's async methods, iterators and lambdas are compiled into types the compiler nests in it, and
        // the PDB names their documents there: a type whose every method is async has no sequence point of its own.
        var generated = new Dictionary<TypeDefinitionHandle, HashSet<DocumentHandle>>();

        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var owner = typeHandle;
            while (metadata.GetString(metadata.GetTypeDefinition(owner).Name).StartsWith('<')
                   && metadata.GetTypeDefinition(owner).GetDeclaringType() is { IsNil: false } declaring)
            {
                owner = declaring;
            }

            var into = owner == typeHandle ? documents : generated;
            foreach (var methodHandle in metadata.GetTypeDefinition(typeHandle).GetMethods())
            {
                var debug = pdb.GetMethodDebugInformation(methodHandle.ToDebugInformationHandle());
                if (!debug.Document.IsNil)
                {
                    Add(into, owner, debug.Document);
                }
                else if (!debug.SequencePointsBlob.IsNil)
                {
                    foreach (var point in debug.GetSequencePoints())
                    {
                        Add(into, owner, point.Document);
                    }
                }
            }
        }

        foreach (var handle in pdb.CustomDebugInformation)
        {
            var information = pdb.GetCustomDebugInformation(handle);
            if (information.Parent.Kind != HandleKind.TypeDefinition || pdb.GetGuid(information.Kind) != TypeDefinitionDocuments)
            {
                continue;
            }

            var blob = pdb.GetBlobReader(information.Value);
            while (blob.RemainingBytes > 0)
            {
                Add(documents, (TypeDefinitionHandle)information.Parent, MetadataTokens.DocumentHandle(blob.ReadCompressedInteger()));
            }
        }

        var marker = "/" + projectName + "/";
        List<string> Sources(IEnumerable<DocumentHandle> handles)
            => handles
                .Select(document => pdb.GetString(pdb.GetDocument(document).Name).Replace('\\', '/'))
                .Select(path => path.IndexOf(marker, StringComparison.OrdinalIgnoreCase) is var at and >= 0 ? path[(at + marker.Length)..] : null)
                .OfType<string>()
                .Where(path => !path.StartsWith("obj/", StringComparison.OrdinalIgnoreCase))
                .ToList();

        var paths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var type in documents.Keys.Concat(generated.Keys).Distinct())
        {
            // Generated documents are not under the project and say nothing. What the compiler nested in a type counts
            // only for a type that names no source of its own.
            var own = Sources(documents.GetValueOrDefault(type) ?? []);
            paths[MetadataName(metadata, type)] = own.Count > 0 ? own : Sources(generated.GetValueOrDefault(type) ?? []);
        }

        return new SourceFolders(paths);
    }

    /// <summary>
    /// The folder <paramref name="type"/> is written in, relative to its project, <c>""</c> for the project's root; or
    /// <see langword="null"/> for a type only a generator wrote.
    /// </summary>
    public string? FolderOf(INamedTypeSymbol type)
    {
        var own = _paths.GetValueOrDefault(MetadataName(type)) ?? [];

        // A nested type without a method body, such as an interface, has no record of its own: the compiler names its
        // file among those of the outermost type, and it is the one named after it, as TenancyUseCases.IInvitationStore.cs is.
        if (own.Count == 0 && type.ContainingType is not null)
        {
            var outermost = type;
            while (outermost.ContainingType is { } containing)
            {
                outermost = containing;
            }

            own = (_paths.GetValueOrDefault(MetadataName(outermost)) ?? [])
                .Where(path => Path.GetFileNameWithoutExtension(path).Split('.').Contains(type.Name, StringComparer.Ordinal))
                .ToList();
        }

        // A partial type written across folders lives where its shortest path is: the file named after it, beside the
        // folders of its parts.
        return own
            .Select(path => Path.GetDirectoryName(path)!.Replace('\\', '/'))
            .OrderBy(folder => folder.Count(c => c == '/'))
            .ThenBy(folder => folder.Length)
            .FirstOrDefault();
    }

    private static string MetadataName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return MetadataName(metadata, declaring) + "+" + name;
        }

        var ns = metadata.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string MetadataName(INamedTypeSymbol type)
    {
        if (type.ContainingType is { } containing)
        {
            return MetadataName(containing) + "+" + type.MetadataName;
        }

        var ns = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();
        return ns.Length == 0 ? type.MetadataName : ns + "." + type.MetadataName;
    }
}
