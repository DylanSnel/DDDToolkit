using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DDDToolkit.HotChocolate.Tests.Infrastructure;

/// <summary>
/// HotChocolate as a test run has it, read from the assemblies in the run's own folder: which assemblies of
/// HotChocolate are there, and which HotChocolate every other assembly there was compiled against.
/// </summary>
/// <remarks>
/// HotChocolate asks that all of its packages in one application are the same version, and an assembly of
/// HotChocolate carries the version of its package, so a folder says whether that holds. It matters for what was
/// compiled as well as for what is loaded: an assembly compiled against an older HotChocolate loads on a newer one
/// without complaint and fails when it first calls what changed in between. A field paged with a total count,
/// compiled against 16.0.0, threw a <see cref="MissingMethodException"/> on 16.6.6.
/// <para>
/// Only the files' metadata is read, so no assembly is loaded that the run would not load itself. The file is
/// compiled into every test project that asks it.
/// </para>
/// </remarks>
internal static class HotChocolateOfTheRun
{
    /// <summary>An assembly of HotChocolate in the run's folder.</summary>
    /// <param name="Name">The assembly's name.</param>
    /// <param name="Version">Its version, which is its package's.</param>
    public sealed record Own(string Name, Version Version)
    {
        /// <inheritdoc />
        public override string ToString() => $"{Name} {Version}";
    }

    /// <summary>What an assembly that is not HotChocolate's was compiled against.</summary>
    /// <param name="Assembly">The assembly that references HotChocolate.</param>
    /// <param name="Reference">The assembly of HotChocolate it references.</param>
    /// <param name="Version">The version of it the compiler was given.</param>
    public sealed record CompiledAgainst(string Assembly, string Reference, Version Version)
    {
        /// <inheritdoc />
        public override string ToString() => $"{Assembly} was compiled against {Reference} {Version}";
    }

    /// <summary>
    /// Whether a package or an assembly of this name is released as part of HotChocolate, with HotChocolate's
    /// version: HotChocolate itself, Fusion, which is <c>HotChocolate.Fusion.*</c>, and GreenDonut. Nitro, the
    /// page HotChocolate serves, is not: it has a version of its own.
    /// </summary>
    public static bool IsHotChocolate(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Is(name, "HotChocolate") || Is(name, "GreenDonut");

        static bool Is(string name, string family)
            => name.Equals(family, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(family + ".", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The assemblies of HotChocolate beside the running test, each with its version.</summary>
    public static IReadOnlyList<Own> Assemblies()
        => [.. Managed().Where(assembly => IsHotChocolate(assembly.Name)).Select(assembly => new Own(assembly.Name, assembly.Version))];

    /// <summary>Every reference to an assembly of HotChocolate from an assembly beside the running test that is not HotChocolate's.</summary>
    public static IReadOnlyList<CompiledAgainst> References()
        => [.. Managed()
            .Where(assembly => !IsHotChocolate(assembly.Name))
            .SelectMany(assembly => assembly.References
                .Where(reference => IsHotChocolate(reference.Name))
                .Select(reference => new CompiledAgainst(assembly.Name, reference.Name, reference.Version)))];

    /// <summary>The managed assemblies in the run's folder: a name, a version and what each references.</summary>
    private static IEnumerable<(string Name, Version Version, IReadOnlyList<(string Name, Version Version)> References)> Managed()
    {
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll").Order(StringComparer.Ordinal))
        {
            using var stream = File.OpenRead(file);
            using var image = new PEReader(stream);
            if (!image.HasMetadata)
            {
                // A native library, which the folder has too.
                continue;
            }

            var metadata = image.GetMetadataReader();
            if (!metadata.IsAssembly)
            {
                continue;
            }

            var definition = metadata.GetAssemblyDefinition();
            var references = metadata.AssemblyReferences
                .Select(metadata.GetAssemblyReference)
                .Select(reference => (metadata.GetString(reference.Name), reference.Version))
                .ToList();
            yield return (metadata.GetString(definition.Name), definition.Version, references);
        }
    }
}
