using System.Collections.Immutable;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Harness;

/// <summary>
/// The metadata references the test snippets compile against.
/// <para>
/// The framework itself comes from <c>Basic.Reference.Assemblies.Net100</c> — the real .NET 10 reference
/// assemblies, so the snippets see exactly what a <c>net10.0</c> project sees (including
/// <c>System.Collections.ObjectModel.ReadOnlySet&lt;T&gt;</c> and <c>Guid.CreateVersion7</c>).
/// </para>
/// <para>
/// The DDDToolkit assemblies come from the test's own output directory: the test project references the
/// runtime projects, so the DLLs are next to the test assembly. Integration references (EF Core,
/// FluentValidation, HotChocolate) are resolved the same way and are opt-in per test, because several
/// generators change what they emit depending on whether those assemblies are referenced at all.
/// </para>
/// <para>
/// Every set is resolved lazily and cached. Package upgrades rename and merge assemblies — HotChocolate 16
/// folded <c>HotChocolate.Types.Shared</c> into <c>HotChocolate.Types.Abstractions</c> and
/// <c>HotChocolate.Execution</c> into <c>HotChocolate</c> — and a set that resolved eagerly in a static
/// initializer turned one missing file into a <see cref="TypeInitializationException"/> that failed every
/// test in the project, including those that never touch the integration. Laziness keeps such a failure
/// inside the tests that actually asked for that set.
/// </para>
/// </summary>
public static class ReferenceSets
{
    private static readonly Lazy<ImmutableArray<PortableExecutableReference>> LazyCore = new(() =>
    [
        .. Basic.Reference.Assemblies.Net100.References.All,
        MetadataReference.CreateFromFile(typeof(ValueObject).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ValueObjectAttribute).Assembly.Location),
    ]);

    private static readonly Lazy<PortableExecutableReference> LazyEntityFrameworkAbstractions =
        new(() => FromOutputDirectory("Microsoft.EntityFrameworkCore.Abstractions.dll"));

    private static readonly Lazy<ImmutableArray<PortableExecutableReference>> LazyEntityFramework = new(() =>
    [
        EntityFrameworkAbstractions,
        FromOutputDirectory("Microsoft.EntityFrameworkCore.dll"),
        FromOutputDirectory("Microsoft.EntityFrameworkCore.Relational.dll"),
        DependencyInjectionAbstractions,
        FromType(typeof(Microsoft.Extensions.Logging.ILogger)),
        FromType(typeof(Microsoft.Extensions.Caching.Memory.MemoryCache)),
    ]);

    private static readonly Lazy<ImmutableArray<PortableExecutableReference>> LazyFluentValidation = new(() =>
    [
        FromOutputDirectory("FluentValidation.dll"),
    ]);

    private static readonly Lazy<ImmutableArray<PortableExecutableReference>> LazyHotChocolate = new(() =>
    [
        FromOutputDirectory("HotChocolate.dll"),
        FromOutputDirectory("HotChocolate.Abstractions.dll"),
        FromOutputDirectory("HotChocolate.Primitives.dll"),
        FromOutputDirectory("HotChocolate.Types.dll"),
        FromOutputDirectory("HotChocolate.Types.Abstractions.dll"),
        FromOutputDirectory("HotChocolate.Execution.Abstractions.dll"),
        FromOutputDirectory("HotChocolate.Execution.Configuration.Abstractions.dll"),
        FromOutputDirectory("HotChocolate.Utilities.dll"),
        FromOutputDirectory("HotChocolate.Language.SyntaxTree.dll"),
        FromOutputDirectory("HotChocolate.Language.Utf8.dll"),
        FromOutputDirectory("HotChocolate.Features.dll"),
        DependencyInjectionAbstractions,
        FromType(typeof(global::DDDToolkit.HotChocolate.Attributes.GraphQLTypeAttribute<>)),
    ]);

    /// <summary>
    /// Resolved from the type rather than from a file name: this assembly ships in the
    /// Microsoft.AspNetCore.App shared framework, which the test project references transitively through
    /// HotChocolate, so whether a copy lands in the output directory depends on how the pinned package
    /// version compares to the runtime installed on the machine. It is copied on a developer machine
    /// running an older runtime and absent on a build agent running a current one.
    /// <para>
    /// Deliberately a property with no initializer. A static initializer here would run when the class is
    /// first touched and, on failure, fail every test in the project rather than only the ones that asked
    /// for this set.
    /// </para>
    /// </summary>
    private static PortableExecutableReference DependencyInjectionAbstractions
        => FromType(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection));

    /// <summary>.NET 10 reference assemblies plus DDDToolkit and DDDToolkit.Abstractions.</summary>
    public static ImmutableArray<PortableExecutableReference> Core => LazyCore.Value;

    /// <summary>Only the assembly declaring <c>[BackingField]</c> and <c>[Owned]</c>.</summary>
    public static PortableExecutableReference EntityFrameworkAbstractions => LazyEntityFrameworkAbstractions.Value;

    /// <summary>Everything the generated EF Core converters and model configuration need.</summary>
    public static ImmutableArray<PortableExecutableReference> EntityFramework => LazyEntityFramework.Value;

    /// <summary>Everything the generated FluentValidation validators need.</summary>
    public static ImmutableArray<PortableExecutableReference> FluentValidation => LazyFluentValidation.Value;

    /// <summary>Everything the generated HotChocolate change-type providers and bindings need.</summary>
    public static ImmutableArray<PortableExecutableReference> HotChocolate => LazyHotChocolate.Value;

    /// <summary>
    /// Resolves the assembly declaring <paramref name="type"/>. Preferred over a file name whenever a type
    /// is reachable at compile time: the compiler checks it, and it is immune to the assembly being
    /// provided by the shared framework instead of being copied to the output directory.
    /// </summary>
    private static PortableExecutableReference FromType(Type type)
        => MetadataReference.CreateFromFile(type.Assembly.Location);

    /// <summary>
    /// Resolves one assembly to a metadata reference, preferring the copy next to the test assembly and
    /// falling back to the loaded one. Used for assemblies with no type worth naming here.
    /// <para>
    /// The fallback is not belt and braces. Whether an assembly is copied to the output directory depends
    /// on what the installed shared framework already provides, which differs between machines and between
    /// runtime versions: <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> is copied on a
    /// Windows developer machine and resolved from the framework on the Linux build agent, where the file
    /// is simply absent. Resolving through the loaded assembly works in both cases and gives the reference
    /// the test process itself is running against.
    /// </para>
    /// </summary>
    private static PortableExecutableReference FromOutputDirectory(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(path))
        {
            return MetadataReference.CreateFromFile(path);
        }

        var simpleName = Path.GetFileNameWithoutExtension(fileName);

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly =>
                !assembly.IsDynamic
                && assembly.Location.Length > 0
                && string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

        if (loaded is not null)
        {
            return MetadataReference.CreateFromFile(loaded.Location);
        }

        try
        {
            var byName = System.Reflection.Assembly.Load(simpleName);
            if (byName.Location.Length > 0)
            {
                return MetadataReference.CreateFromFile(byName.Location);
            }
        }
        catch (Exception exception) when (exception is FileNotFoundException or BadImageFormatException)
        {
            // Fall through to the descriptive error below.
        }

        throw new FileNotFoundException(
            $"'{fileName}' is neither in the test output directory nor loadable by name. The test project must reference "
                + "the project that brings it in. If the package was upgraded, check whether the assembly was renamed or "
                + "merged into another one.",
            path);
    }
}
