using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// The lines that say which of a project's ids are a package's: a generic assembly attribute of the package's marked
/// <c>[TemplateIds]</c>, <c>[assembly: TenancyIds&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;]</c>. Each type
/// argument of the line is the id of the class declared with the template its type parameter's <c>[TemplateType]</c>
/// names. A module that sees the ids and none of the classes writes the line once, and the registrations and the
/// classes a package asks for that take only ids are closed over them, as over the classes of the module that declares
/// them.
/// <para>
/// A line fills an id and nothing else: a type parameter that takes a class, or a later type argument of a template,
/// is no id a line can name. So a registration or a class that takes one is not for a project that has only a line,
/// and hears nothing about it there. A class a project or its module declares with a template always comes first.
/// </para>
/// </summary>
internal static class TemplateIdLines
{
    /// <summary>One id a line names: the template whose class's id it is, the id, and the line.</summary>
    /// <param name="TemplateKey">The template attribute's open definition, fully qualified.</param>
    /// <param name="Id">The id.</param>
    /// <param name="Assembly">The assembly whose line it is.</param>
    /// <param name="Where">The line, when it is in the project being compiled; null otherwise.</param>
    internal sealed record NamedId(string TemplateKey, ITypeSymbol Id, IAssemblySymbol Assembly, LocationInfo? Where);

    /// <summary>The ids this project's own lines name, in the order the compiler reads the lines.</summary>
    public static List<NamedId> Here(Compilation compilation, CancellationToken cancellationToken)
        => Read(compilation.Assembly, here: true, cancellationToken);

    /// <summary>
    /// The ids the lines name of the projects of <paramref name="module"/> this project references, read once per
    /// assembly: the module's application project says it, and the project below it that holds the context reads it
    /// there. For a project of no module, nothing: a line is a module's word about the ids it sees.
    /// </summary>
    public static List<NamedId> InModule(Compilation compilation, string? module, CancellationToken cancellationToken)
    {
        var found = new List<NamedId>();
        if (module is null)
        {
            return found;
        }

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(ModuleBoundary.ModuleOf(assembly), module, StringComparison.Ordinal))
            {
                found.AddRange(ByAssembly.GetValue(assembly, static each => Read(each, here: false, CancellationToken.None).ToArray()));
            }
        }

        return found;
    }

    /// <summary>The lines of a referenced assembly, read once: it does not change while the project that references it is edited.</summary>
    private static readonly ConditionalWeakTable<IAssemblySymbol, NamedId[]> ByAssembly = new();

    /// <summary>
    /// The candidates a line gives a type parameter that takes the id of <paramref name="take"/>'s template, one per id
    /// named for it: none for a type parameter that takes a class or a later type argument, which no line names.
    /// </summary>
    public static List<DefinitionFactory.TemplateSource> SourcesFor(TemplateRegistrations.Take take, IEnumerable<NamedId> named)
    {
        var sources = new List<DefinitionFactory.TemplateSource>();
        if (take.TakeType || take.Argument != 0)
        {
            return sources;
        }

        foreach (var each in named)
        {
            if (each.TemplateKey != take.Key)
            {
                continue;
            }

            var text = each.Id.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // Two lines that name one id name it once.
            if (sources.Any(source => source.IdType == text))
            {
                continue;
            }

            sources.Add(new DefinitionFactory.TemplateSource(
                each.Id.Name,
                text,
                text,
                DefinitionFactory.IsEntityId(each.Id),
                canGenerate: false,
                metadataName: null,
                symbol: null)
            {
                IdSymbol = each.Id,
                LineIn = each.Assembly,
                LineLocation = each.Where,
            });
        }

        return sources;
    }

    /// <summary>
    /// The ids an assembly's lines name. A line is an assembly attribute whose class is a generic class marked
    /// <c>[TemplateIds]</c>, each of whose type parameters takes the id of a template, with nothing else said. A line of a
    /// class that is not, the package's mistake, names nothing.
    /// </summary>
    private static List<NamedId> Read(IAssemblySymbol assembly, bool here, CancellationToken cancellationToken)
    {
        var found = new List<NamedId>();
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is not { IsGenericType: true } line
                || !line.OriginalDefinition.GetAttributes().Any(static marker => marker.AttributeClass is { } markerClass && EntityDeclarations.Is(markerClass, KnownTypes.TemplateIdsAttribute))
                || TemplateRegistrations.TakesOf(line.OriginalDefinition.TypeParameters) is not { } takes
                || takes.Any(static take => take is null || take.TakeType || take.Argument != 0))
            {
                continue;
            }

            var where = here && attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is { } syntax ? LocationInfo.From(syntax) : null;
            for (var position = 0; position < takes.Length; position++)
            {
                if (line.TypeArguments[position] is { TypeKind: not TypeKind.Error } id)
                {
                    found.Add(new NamedId(takes[position]!.Key, id, assembly, where));
                }
            }
        }

        return found;
    }
}
