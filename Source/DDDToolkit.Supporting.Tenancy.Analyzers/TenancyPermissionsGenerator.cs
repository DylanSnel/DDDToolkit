using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Supporting.Tenancy.Analyzers;

/// <summary>
/// Collects the permission keys of every module a project references, so a module states its keys once: on the
/// static list it marks with <c>[TenancyPermissions]</c>.
/// <code>
/// // A module's application project
/// public static class OrderingKeys
/// {
///     [TenancyPermissions]
///     public static IReadOnlyList&lt;Permission&gt; Permissions { get; } = [new("orders.view", "Ordering", "See the orders")];
/// }
///
/// // Written into the host, which references the module
/// internal static class TenancyPermissionsOfModules
/// {
///     public static IReadOnlyList&lt;Permission&gt; All { get; } = Join(global::Shop.Ordering.OrderingKeys.Permissions, ...);
///     public static IServiceCollection AddTenancyPermissionsOfModules(this IServiceCollection services) =&gt; ...AddTenancyPermissions(services, All);
/// }
/// </code>
/// The keys are needed twice, by two programs that cannot ask each other: the host registers them, where its
/// catalogue is built from the services, and the program that exports the database's policies builds the same
/// catalogue without any. Both compose the modules, so both see every module's assembly, and in it the marked
/// lists. So the host calls <c>services.AddTenancyPermissionsOfModules()</c> once, an export builds with
/// <c>TenancyCatalogue.Build(application, TenancyPermissionsOfModules.All)</c>, and a module that is added changes
/// neither: no list of the modules is written by hand anywhere.
/// <para>
/// <b>Where it writes.</b> Into every project that sees the package and declares no module with
/// <c>[assembly: Module]</c>: the host, or a project the host and the export share. It collects the project's own
/// marked lists and those of every project it references, directly or not, and writes the class when it finds none
/// as well, with an empty <c>All</c>, so the host's one call and the export's build stay what they are while no
/// module, or no module any more, marks a list. A project that declares a module gets nothing: a module states its
/// own keys, and composes no other module's. The class is internal and in the namespace named after the project's
/// assembly, so two projects that both compose the modules each have their own, and a project that sees the other's
/// internals still names its own.
/// </para>
/// <para>
/// <b>What it reports.</b> A marked list that could not be read where it is collected: not static, without a
/// getter, in a generic type, in a type that cannot be named such as an extension block, a static virtual or
/// abstract member of an interface, of a type that is no sequence of <c>Permission</c>, or, in a library, not public.
/// That is DDD00063 where the list is declared, because a project that references the library, the one that composes
/// the modules among them, does not even see a list that is not public, and would leave its keys out in silence.
/// Whether the library declares a module does not matter: one that leaves its <c>[assembly: Module]</c> out is
/// still referenced. Only an application, the program the modules are composed in, may keep a list of its own
/// internal, since it collects that one itself; one that it cannot read outside the list's own type is reported
/// all the same. A list that cannot be read is left out of what is written here as well, so the report is the one
/// error, not a second one in written code.
/// </para>
/// <para>
/// The package's types are named here by their metadata names, as text: this assembly references nothing of the
/// package, and what it writes is compiled in the application, which does.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TenancyPermissionsGenerator : IIncrementalGenerator
{
    private const string Package = "DDDToolkit.Supporting.Tenancy";
    private const string AttributeName = Package + ".Catalogue.TenancyPermissionsAttribute";
    private const string PermissionName = Package + ".Catalogue.Permission";
    private const string RegistrationName = Package + ".TenancyServiceCollectionExtensions";
    private const string ServiceCollectionName = "Microsoft.Extensions.DependencyInjection.IServiceCollection";

    /// <summary>The class written into a project that composes the modules.</summary>
    public const string ClassName = "TenancyPermissionsOfModules";

    private const string Permission = "global::" + PermissionName;
    private const string Sequence = "global::System.Collections.Generic.IEnumerable<" + Permission + ">";
    private const string ServiceCollection = "global::" + ServiceCollectionName;

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // This project's own marked lists, each read where it is declared: what is reported about it is said there.
        var own = context.SyntaxProvider.ForAttributeWithMetadataName(
                AttributeName,
                predicate: static (node, _) => node is PropertyDeclarationSyntax or VariableDeclaratorSyntax,
                transform: static (syntaxContext, _) => Inspect(syntaxContext))
            .Where(static list => list is not null)
            .Select(static (list, _) => list!)
            .Collect();

        // Whether this project composes modules, and the lists of the projects it references: read off the
        // compilation into plain values, so the output step stays cached while an edit changes none of them.
        var composition = context.CompilationProvider.Select(static (compilation, cancellationToken) => Compose(compilation, cancellationToken));

        context.RegisterSourceOutput(own.Combine(composition), static (production, pair) => Execute(production, pair.Left, pair.Right));
    }

    // ------------------------------------------------------------------ a list of this project

    /// <summary>
    /// One marked list of this project: how it is read, or why it cannot be, whether a project that references
    /// this one sees it, and whether this project itself can.
    /// </summary>
    /// <param name="Name">The list as a message names it: <c>OrderingKeys.Permissions</c>.</param>
    /// <param name="Location">Its name in its declaration.</param>
    /// <param name="Expression">What reads it, <c>global::Shop.Ordering.OrderingKeys.Permissions</c>; null when it cannot be read.</param>
    /// <param name="Problem">Why it cannot be read, as the message says it; null when it can, or when the compiler says so already.</param>
    /// <param name="IsPublic">Whether it is public, in public types, with a public getter: what a project that references this one sees.</param>
    /// <param name="IsReadableHere">
    /// Whether code anywhere in this project reads it, as the written class does: neither the member, nor its getter,
    /// nor a type it is in, is private or protected.
    /// </param>
    private sealed record OwnList(string Name, LocationInfo? Location, string? Expression, string? Problem, bool IsPublic, bool IsReadableHere);

    private static OwnList? Inspect(GeneratorAttributeSyntaxContext context)
    {
        var compilation = context.SemanticModel.Compilation;
        var member = context.TargetSymbol;
        ITypeSymbol type;
        SyntaxToken name;
        switch (member)
        {
            case IPropertySymbol property when context.TargetNode is PropertyDeclarationSyntax declaration:
                (type, name) = (property.Type, declaration.Identifier);
                break;
            case IFieldSymbol field when context.TargetNode is VariableDeclaratorSyntax declarator:
                (type, name) = (field.Type, declarator.Identifier);
                break;
            default:
                return null;
        }

        if (compilation.GetTypeByMetadataName(PermissionName) is not { } permission)
        {
            return null;
        }

        var problem = ProblemOf(member, type, permission, compilation);
        return new OwnList(
            member.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
            LocationInfo.From(name),
            problem is null && type.TypeKind != TypeKind.Error ? ExpressionOf(member) : null,
            problem,
            IsPublic(member),
            IsReadableWithin(member, compilation));
    }

    // ------------------------------------------------------------------ the projects it references

    /// <summary>
    /// What this project composes: nothing when it sees no Tenancy, and nothing to write when it declares a module.
    /// Otherwise the lists of the projects it references that it can read, and whether it can write the
    /// registration beside them.
    /// </summary>
    /// <param name="SeesTenancy">Whether the project references the package that declares the attribute.</param>
    /// <param name="DeclaresModule">Whether the project declares a module, and so collects nothing.</param>
    /// <param name="IsLibrary">Whether the project is a library, which other projects reference, so its lists are public.</param>
    /// <param name="Namespace">The project's own namespace, from its assembly's name: where the class is written.</param>
    /// <param name="CanRegister">Whether the service collection and the package's registration are both in reach.</param>
    /// <param name="Referenced">What reads each list of a referenced project, in ordinal order.</param>
    private sealed record Composition(bool SeesTenancy, bool DeclaresModule, bool IsLibrary, string Namespace, bool CanRegister, EquatableArray<string> Referenced);

    private static Composition Compose(Compilation compilation, CancellationToken cancellationToken)
    {
        var @namespace = Identifiers.NamespaceFrom(compilation.AssemblyName);

        // An application is what an OutputType of Exe or WinExe compiles to: the program the modules are composed
        // in, which no other project composes them from. Everything else is referenced by something.
        var isLibrary = compilation.Options.OutputKind is OutputKind.DynamicallyLinkedLibrary or OutputKind.NetModule;
        if (compilation.GetTypeByMetadataName(AttributeName) is not { } attribute
            || compilation.GetTypeByMetadataName(PermissionName) is not { } permission)
        {
            return new Composition(false, false, isLibrary, @namespace, false, EquatableArray<string>.Empty);
        }

        // A module's own projects collect nothing, so they are not walked: most projects that see the package are one.
        if (ModuleBoundary.ModuleOf(compilation.Assembly) is not null)
        {
            return new Composition(true, true, isLibrary, @namespace, false, EquatableArray<string>.Empty);
        }

        var found = new List<string>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Only an assembly compiled against the package can mark a list with its attribute: the rest, the
            // framework's among them, are passed over without reading a type.
            if (assembly.Name == Package || !assembly.Modules.Any(static module => module.ReferencedAssemblies.Any(static identity => identity.Name == Package)))
            {
                continue;
            }

            Collect(assembly.GlobalNamespace, attribute, permission, compilation, found, cancellationToken);
        }

        var canRegister = compilation.GetTypeByMetadataName(ServiceCollectionName) is not null && compilation.GetTypeByMetadataName(RegistrationName) is not null;
        return new Composition(true, false, isLibrary, @namespace, canRegister, found.Distinct(StringComparer.Ordinal).OrderBy(static each => each, StringComparer.Ordinal).ToEquatableArray());
    }

    /// <summary>The marked lists under <paramref name="space"/> that this project can read, as what reads each.</summary>
    private static void Collect(INamespaceSymbol space, INamedTypeSymbol attribute, INamedTypeSymbol permission, Compilation compilation, List<string> found, CancellationToken cancellationToken)
    {
        foreach (var member in space.GetMembers())
        {
            if (member is INamespaceSymbol inner)
            {
                Collect(inner, attribute, permission, compilation, found, cancellationToken);
            }
            else if (member is INamedTypeSymbol type)
            {
                Collect(type, attribute, permission, compilation, found, cancellationToken);
            }
        }
    }

    private static void Collect(INamedTypeSymbol type, INamedTypeSymbol attribute, INamedTypeSymbol permission, Compilation compilation, List<string> found, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A generic type's members are named only with type arguments, which a list of keys has none to take; nor
        // are those of a type nested in one. What its own project says about it is DDD00063.
        if (type.IsGenericType || !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
        {
            return;
        }

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case INamedTypeSymbol nested:
                    Collect(nested, attribute, permission, compilation, found, cancellationToken);
                    break;
                case IPropertySymbol { IsStatic: true } property when IsMarked(property, attribute)
                    && IsReadableWithin(property, compilation)
                    && ProblemOf(property, property.Type, permission, compilation) is null:
                    found.Add(ExpressionOf(property));
                    break;
                case IFieldSymbol { IsStatic: true } field when IsMarked(field, attribute)
                    && IsReadableWithin(field, compilation)
                    && ProblemOf(field, field.Type, permission, compilation) is null:
                    found.Add(ExpressionOf(field));
                    break;
            }
        }
    }

    private static bool IsMarked(ISymbol member, INamedTypeSymbol attribute)
        => member.GetAttributes().Any(each => SymbolEqualityComparer.Default.Equals(each.AttributeClass, attribute));

    // ------------------------------------------------------------------ what a list has to be

    /// <summary>
    /// Why the list cannot be read as <c>Type.Member</c> at all, as the message of DDD00063 says it, or null when it
    /// can. Who may read it, every project or its own alone, is asked apart: an application reads its own lists.
    /// </summary>
    private static string? ProblemOf(ISymbol member, ITypeSymbol type, INamedTypeSymbol permission, Compilation compilation)
    {
        if (!member.IsStatic)
        {
            return "is not static";
        }

        if (member is IPropertySymbol { GetMethod: null })
        {
            return "has no getter";
        }

        // Such a member is read through a type parameter constrained to the interface, which a list of keys has none of.
        if (member.ContainingType.TypeKind == TypeKind.Interface && (member.IsVirtual || member.IsAbstract))
        {
            return "is a static virtual or abstract member of an interface, which is read only through a type parameter";
        }

        for (var container = member.ContainingType; container is not null; container = container.ContainingType)
        {
            if (container.IsGenericType)
            {
                return "is declared in the generic type '" + container.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) + "'";
            }

            if (container.IsFileLocal)
            {
                return "is declared in the file-local type '" + container.Name + "', which no other file can name";
            }

            // An extension block, or a type the compiler made: no code names it.
            if (!container.CanBeReferencedByName)
            {
                return "is declared in '" + container.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) + "', which code cannot name";
            }
        }

        // A type the compiler cannot bind it reports itself; the list is then only left out.
        if (type.TypeKind == TypeKind.Error)
        {
            return null;
        }

        // As the written code reads it: handed to a parameter of IEnumerable<Permission>.
        var sequence = compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T).Construct(permission);
        var conversion = compilation.ClassifyCommonConversion(type, sequence);
        return (conversion.IsIdentity || conversion.IsImplicit) && !conversion.IsUserDefined
            ? null
            : "is of type '" + type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) + "', which is no sequence of Permission";
    }

    /// <summary>Whether the member and every type it is declared in are public: what another project sees of it.</summary>
    private static bool IsPublic(ISymbol member)
    {
        for (ISymbol? symbol = member; symbol is not null and not INamespaceSymbol; symbol = symbol.ContainingSymbol)
        {
            if (symbol.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return member is not IPropertySymbol { GetMethod: { DeclaredAccessibility: not Accessibility.Public } };
    }

    /// <summary>
    /// Whether code anywhere in <paramref name="compilation"/> reads the member, a property through its getter: what
    /// the class written in that project does.
    /// </summary>
    private static bool IsReadableWithin(ISymbol member, Compilation compilation)
        => compilation.IsSymbolAccessibleWithin(member, compilation.Assembly)
           && (member is not IPropertySymbol { GetMethod: { } getter } || compilation.IsSymbolAccessibleWithin(getter, compilation.Assembly));

    /// <summary>What reads the list from anywhere: <c>global::Shop.Ordering.OrderingKeys.Permissions</c>.</summary>
    private static string ExpressionOf(ISymbol member)
    {
        var name = SyntaxFacts.GetKeywordKind(member.Name) != SyntaxKind.None ? "@" + member.Name : member.Name;
        return member.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + name;
    }

    // ------------------------------------------------------------------ what is written

    private static void Execute(SourceProductionContext production, ImmutableArray<OwnList> own, Composition composition)
    {
        var lists = new List<string>(composition.Referenced);
        foreach (var list in own)
        {
            // In a library a list is public, since the projects that reference it read it; in an application it is
            // at least readable outside its own type, since the class written there reads it.
            var problem = list.Problem
                ?? (composition.IsLibrary && !list.IsPublic ? "is not public, and its project is a library: the project that composes the modules reads it from outside" : null)
                ?? (!list.IsReadableHere ? "cannot be read outside the type it is declared in" : null);
            if (problem is not null)
            {
                production.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.TenancyPermissionsUnreadable, list.Location?.ToLocation() ?? Location.None, list.Name, problem));
            }
            else if (list.Expression is not null)
            {
                lists.Add(list.Expression);
            }
        }

        if (!composition.SeesTenancy || composition.DeclaresModule)
        {
            return;
        }

        // Written with no list as well: the host's call and the export's build name no module, so they compile the
        // same before the first module marks its list, and after the last one is taken out.
        var ordered = lists.Distinct(StringComparer.Ordinal).OrderBy(static expression => expression, StringComparer.Ordinal).ToList();
        production.AddSource(ClassName + ".g.cs", SourceText.From(Write(composition, ordered), Encoding.UTF8));
    }

    private static string Write(Composition composition, IReadOnlyList<string> lists)
    {
        var writer = new CodeWriter().Header();
        writer.Line("namespace " + composition.Namespace + ";");
        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// The permission keys of every module this project references, each as its module states them once, on the list");
        writer.Line("/// it marks with <c>[TenancyPermissions]</c>: what the host adds to Tenancy's catalogue"
                    + (composition.CanRegister ? ", with <see cref=\"AddTenancyPermissionsOfModules\"/>," : ",") + " and what a program");
        writer.Line("/// that exports the database's policies builds the same catalogue from, with");
        writer.Line("/// <c>TenancyCatalogue.Build(application, TenancyPermissionsOfModules.All)</c>.");
        writer.Line("/// </summary>");
        writer.Line("/// <remarks>");
        if (lists.Count == 0)
        {
            writer.Line("/// Written by Tenancy's generator, which found no marked list in this project or in a project it references, so");
            writer.Line("/// <see cref=\"All\"/> is empty.");
        }
        else
        {
            writer.Line("/// Written by Tenancy's generator from the lists it found, in the order of their names:");
            for (var index = 0; index < lists.Count; index++)
            {
                writer.Line("/// <c>" + lists[index].Substring("global::".Length) + "</c>" + (index == lists.Count - 1 ? "." : ","));
            }
        }

        writer.Line("/// A module that marks its list is in it as soon as this project references the module.");
        writer.Line("/// </remarks>");
        using (writer.Block("internal static class " + ClassName))
        {
            writer.Line("/// <summary>Every module's keys, one list after the other.</summary>");
            if (lists.Count == 0)
            {
                writer.Line("public static global::System.Collections.Generic.IReadOnlyList<" + Permission + "> All { get; } = global::System.Array.Empty<" + Permission + ">();");
            }
            else
            {
                writer.Line("public static global::System.Collections.Generic.IReadOnlyList<" + Permission + "> All { get; } = Join(");
                for (var index = 0; index < lists.Count; index++)
                {
                    writer.Line("    " + lists[index] + (index == lists.Count - 1 ? ");" : ","));
                }
            }

            if (composition.CanRegister)
            {
                writer.Line();
                writer.Line("/// <summary>");
                writer.Line("/// Adds <see cref=\"All\"/> to Tenancy's catalogue as one contribution, the way <c>AddTenancyPermissions</c> adds a");
                writer.Line("/// list by hand. Call it once, in the project that composes the modules: a key that is added twice stops the");
                writer.Line("/// catalogue when it is built.");
                writer.Line("/// </summary>");
                writer.Line("/// <param name=\"services\">The service collection.</param>");
                writer.Line("public static " + ServiceCollection + " AddTenancyPermissionsOfModules(this " + ServiceCollection + " services)");
                writer.Line("    => global::" + RegistrationName + ".AddTenancyPermissions(services, All);");
            }

            if (lists.Count > 0)
            {
                writer.Line();
                writer.Line("/// <summary>The lists one after the other, in one array.</summary>");
                using (writer.Block("private static " + Permission + "[] Join(params " + Sequence + "[] lists)"))
                {
                    writer.Line("var all = new global::System.Collections.Generic.List<" + Permission + ">();");
                    using (writer.Block("foreach (var list in lists)"))
                    {
                        writer.Line("all.AddRange(list);");
                    }

                    writer.Line();
                    writer.Line("return all.ToArray();");
                }
            }
        }

        return writer.ToString();
    }
}
