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
/// </summary>
public static class ReferenceSets
{
    /// <summary>.NET 10 reference assemblies plus DDDToolkit and DDDToolkit.Abstractions.</summary>
    public static ImmutableArray<PortableExecutableReference> Core { get; } =
    [
        .. Basic.Reference.Assemblies.Net100.References.All,
        MetadataReference.CreateFromFile(typeof(ValueObject).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ValueObjectAttribute).Assembly.Location),
    ];

    /// <summary>Only the assembly declaring <c>[BackingField]</c> and <c>[Owned]</c>.</summary>
    public static PortableExecutableReference EntityFrameworkAbstractions { get; }
        = FromOutputDirectory("Microsoft.EntityFrameworkCore.Abstractions.dll");

    /// <summary>Everything the generated EF Core converters and model configuration need.</summary>
    public static ImmutableArray<PortableExecutableReference> EntityFramework { get; } =
    [
        EntityFrameworkAbstractions,
        FromOutputDirectory("Microsoft.EntityFrameworkCore.dll"),
        FromOutputDirectory("Microsoft.EntityFrameworkCore.Relational.dll"),
        FromOutputDirectory("Microsoft.Extensions.DependencyInjection.Abstractions.dll"),
        FromOutputDirectory("Microsoft.Extensions.Logging.Abstractions.dll"),
        FromOutputDirectory("Microsoft.Extensions.Caching.Memory.dll"),
    ];

    /// <summary>Everything the generated FluentValidation validators need.</summary>
    public static ImmutableArray<PortableExecutableReference> FluentValidation { get; } =
    [
        FromOutputDirectory("FluentValidation.dll"),
    ];

    /// <summary>Everything the generated HotChocolate change-type providers and bindings need.</summary>
    public static ImmutableArray<PortableExecutableReference> HotChocolate { get; } =
    [
        FromOutputDirectory("HotChocolate.Abstractions.dll"),
        FromOutputDirectory("HotChocolate.Types.dll"),
        FromOutputDirectory("HotChocolate.Types.Shared.dll"),
        FromOutputDirectory("HotChocolate.Execution.dll"),
        FromOutputDirectory("HotChocolate.Execution.Abstractions.dll"),
        FromOutputDirectory("HotChocolate.Utilities.dll"),
        FromOutputDirectory("HotChocolate.Language.SyntaxTree.dll"),
        FromOutputDirectory("HotChocolate.Language.Utf8.dll"),
        FromOutputDirectory("HotChocolate.Features.dll"),
        FromOutputDirectory("Microsoft.Extensions.DependencyInjection.Abstractions.dll"),
        MetadataReference.CreateFromFile(typeof(global::DDDToolkit.HotChocolate.Attributes.GraphQLTypeAttribute<>).Assembly.Location),
    ];

    private static PortableExecutableReference FromOutputDirectory(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"'{fileName}' is not in the test output directory. The test project must reference the project that brings it in.",
                path);
        }

        return MetadataReference.CreateFromFile(path);
    }
}
