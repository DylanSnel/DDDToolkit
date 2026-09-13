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
        FromOutputDirectory("Microsoft.Extensions.DependencyInjection.Abstractions.dll"),
        FromOutputDirectory("Microsoft.Extensions.Logging.Abstractions.dll"),
        FromOutputDirectory("Microsoft.Extensions.Caching.Memory.dll"),
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
        FromOutputDirectory("Microsoft.Extensions.DependencyInjection.Abstractions.dll"),
        MetadataReference.CreateFromFile(typeof(global::DDDToolkit.HotChocolate.Attributes.GraphQLTypeAttribute<>).Assembly.Location),
    ]);

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

    private static PortableExecutableReference FromOutputDirectory(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"'{fileName}' is not in the test output directory. The test project must reference the project that brings it in. "
                    + "If the package was upgraded, check whether the assembly was renamed or merged into another one.",
                path);
        }

        return MetadataReference.CreateFromFile(path);
    }
}
