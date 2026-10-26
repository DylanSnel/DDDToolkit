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
/// Puts the permissions of every module into Tenancy's catalogue, so a module declares each permission once: as a
/// constant of a static class it marks <c>[Permissions]</c>, with its level and what it is for.
/// <code>
/// // A module's application project, with DDD_Module Ordering
/// [Permissions]
/// public static class OrderingPermissions
/// {
///     [AtTenant(Description = "See the orders.")]
///     public const string View = "orders.view";
/// }
///
/// // Written into that project: the module's list
/// public static class DeclaredPermissions
/// {
///     [TenancyPermissions]
///     public static IReadOnlyList&lt;Permission&gt; All { get; } = [new("orders.view", "Ordering", "See the orders.", Order: 10)];
/// }
///
/// // Written into the host, which references the module
/// internal static class TenancyPermissionsOfModules
/// {
///     public static IReadOnlyList&lt;Permission&gt; All { get; } = Join(global::Shop.Ordering.DeclaredPermissions.All, ...);
///     public static IServiceCollection AddTenancyPermissionsOfModules(this IServiceCollection services) =&gt; ...AddTenancyPermissions(services, All);
/// }
/// </code>
/// <para>
/// <b>The module's list.</b> Every <c>public const string</c> of a <c>[Permissions]</c> class, whatever its level,
/// under the module the project declares with <c>DDD_Module</c> or <c>[assembly: Module]</c>, or its assembly's name
/// where it declares none, in the order the classes' names and their constants are written. A permission's
/// description is its level's <c>Description</c>, or else its <c>&lt;summary&gt;</c>, as text, read whether the project
/// writes a documentation file or not; with neither it is empty, and the core reports DDD00099. Its <c>Implies</c> and
/// <c>Retired</c> are said on the level. The list is marked <c>[TenancyPermissions]</c>, the mark the project that
/// composes the modules collects; a list a module marks by hand is DDD00095.
/// </para>
/// The permissions are needed twice, by two programs that cannot ask each other: the host registers them, where its
/// catalogue is built from the services, and the program that exports the database's policies builds the same
/// catalogue without any. Both compose the modules, so both see every module's assembly, and in it the marked
/// lists. So the host calls <c>services.AddTenancyPermissionsOfModules()</c> once, an export builds with
/// <c>TenancyCatalogue.Build(application, TenancyPermissionsOfModules.All)</c>, and a module that is added changes
/// neither: no list of the modules is written by hand anywhere.
/// <para>
/// <b>Where it writes.</b> Into every application that sees the package, the program the modules are composed in,
/// and into every library that sees it and declares no module, with <c>DDD_Module</c> or <c>[assembly: Module]</c>:
/// the host, or a project the host and the export share. It collects the project's own marked lists and those of
/// every project it references, directly or not, and writes the class when it finds none as well, with an empty
/// <c>All</c>, so the host's one call and the export's build stay what they are while no module, or no module any
/// more, marks a list. A library that declares a module gets nothing: a module states its own permissions, and composes no
/// other module's. An application gets the class whether it declares a module or not, because no other project
/// composes the modules from it: an application in one project that sets <c>DDD_Module</c> to name its code is a
/// module too, and still has to register its own permissions. The class is internal and in the namespace named after the
/// project's assembly, so two projects that both compose the modules each have their own, and a project that sees
/// the other's internals still names its own.
/// </para>
/// <para>
/// <b>What it reports.</b> A list a project marks <c>[TenancyPermissions]</c> by hand: DDD00095 where it is declared,
/// since the mark is the generator's, and a module declares its permissions in a <c>[Permissions]</c> class. A marked
/// list of a referenced project that cannot be read as <c>Type.Member</c> is left out of what is written, which only
/// a list written by hand, and refused where it was, can be.
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
    private const string PermissionsAttribute = "DDDToolkit.Access.PermissionsAttribute";
    private const string LevelBase = "DDDToolkit.Access.PermissionLevelAttribute";

    /// <summary>The class written into a project that declares permissions.</summary>
    public const string DeclaredClassName = "DeclaredPermissions";
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
        // This project's [Permissions] classes: the module's list is written from them.
        var declared = context.SyntaxProvider.ForAttributeWithMetadataName(
                PermissionsAttribute,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (syntaxContext, cancellationToken) => ReadDeclared(syntaxContext, cancellationToken))
            .Where(static each => each is not null)
            .Select(static (each, _) => each!)
            .Collect();

        // This project's own marked lists: a module's list is written, never marked by hand.
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

        context.RegisterSourceOutput(own.Combine(declared).Combine(composition), static (production, all) => Execute(production, all.Left.Left, all.Left.Right, all.Right));
    }

    // ------------------------------------------------------------------ a list of this project

    /// <summary>One list this project marks by hand: what the report names, and where.</summary>
    /// <param name="Name">The list as a message names it: <c>OrderingList.Permissions</c>.</param>
    /// <param name="Location">Its name in its declaration.</param>
    private sealed record OwnList(string Name, LocationInfo? Location);

    private static OwnList? Inspect(GeneratorAttributeSyntaxContext context)
    {
        var name = context.TargetNode switch
        {
            PropertyDeclarationSyntax declaration => declaration.Identifier,
            VariableDeclaratorSyntax declarator => declarator.Identifier,
            _ => default,
        };

        return name == default
            ? null
            : new OwnList(context.TargetSymbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat), LocationInfo.From(name));
    }

    // ------------------------------------------------------------------ the projects it references

    /// <summary>
    /// What this project composes: nothing when it sees no Tenancy, and nothing to write when it is a library that
    /// declares a module. Otherwise the lists of the projects it references that it can read, and whether it can
    /// write the registration beside them.
    /// </summary>
    /// <param name="SeesTenancy">Whether the project references the package that declares the attribute.</param>
    /// <param name="DeclaresModule">Whether the project is a library that declares a module, and so collects nothing.</param>
    /// <param name="IsLibrary">Whether the project is a library, which other projects reference, so its lists are public.</param>
    /// <param name="Namespace">The project's own namespace, from its assembly's name: where the class is written.</param>
    /// <param name="CanRegister">Whether the service collection and the package's registration are both in reach.</param>
    /// <param name="Referenced">What reads each list of a referenced project, in ordinal order.</param>
    /// <param name="Module">The module the project declares, or its assembly's name: what its own permissions are listed under.</param>
    private sealed record Composition(bool SeesTenancy, bool DeclaresModule, bool IsLibrary, string Namespace, bool CanRegister, EquatableArray<string> Referenced, string Module = "");

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

        var module = ModuleBoundary.ModuleOf(compilation.Assembly) ?? compilation.AssemblyName ?? @namespace;

        // A module's own libraries collect nothing, so they are not walked: most projects that see the package are one.
        // An application is walked whatever it declares: it is the program, and nothing composes the modules from it.
        if (isLibrary && ModuleBoundary.ModuleOf(compilation.Assembly) is not null)
        {
            return new Composition(true, true, isLibrary, @namespace, false, EquatableArray<string>.Empty, module);
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
        return new Composition(true, false, isLibrary, @namespace, canRegister, found.Distinct(StringComparer.Ordinal).OrderBy(static each => each, StringComparer.Ordinal).ToEquatableArray(), module);
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

        // A generic type's members are named only with type arguments, which a list of permissions has none to take; nor
        // are those of a type nested in one. A list the generator writes is never in one.
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
    /// Why the list cannot be read as <c>Type.Member</c> at all, or null when it
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

        // Such a member is read through a type parameter constrained to the interface, which a list of permissions has none of.
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

    /// <summary>
    /// Whether code anywhere in <paramref name="compilation"/> reads the member, a property through its getter: what
    /// the class written in that project does.
    /// </summary>
    private static bool IsReadableWithin(ISymbol member, Compilation compilation)
        => compilation.IsSymbolAccessibleWithin(member, compilation.Assembly)
           && (member is not IPropertySymbol { GetMethod: { } getter } || compilation.IsSymbolAccessibleWithin(getter, compilation.Assembly));

    /// <summary>What reads the list from anywhere: <c>global::Shop.Ordering.OrderingList.Permissions</c>.</summary>
    private static string ExpressionOf(ISymbol member)
    {
        var name = SyntaxFacts.GetKeywordKind(member.Name) != SyntaxKind.None ? "@" + member.Name : member.Name;
        return member.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + name;
    }

    // ------------------------------------------------------------------ what is written

    private static void Execute(SourceProductionContext production, ImmutableArray<OwnList> own, ImmutableArray<DeclaredClass> declared, Composition composition)
    {
        var lists = new List<string>(composition.Referenced);
        foreach (var list in own)
        {
            // The mark is the generator's: a module declares its permissions in a [Permissions] class.
            production.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.HandWrittenTenancyPermissions, list.Location?.ToLocation() ?? Location.None, list.Name));
        }

        if (!composition.SeesTenancy)
        {
            return;
        }

        if (declared.Length > 0)
        {
            production.AddSource(DeclaredClassName + ".g.cs", SourceText.From(WriteDeclared(composition, declared), Encoding.UTF8));
            lists.Add("global::" + composition.Namespace + "." + DeclaredClassName + ".All");
        }

        if (composition.DeclaresModule)
        {
            return;
        }

        // Written with no list as well: the host's call and the export's build name no module, so they compile the
        // same before the first module marks its list, and after the last one is taken out.
        var ordered = lists.Distinct(StringComparer.Ordinal).OrderBy(static expression => expression, StringComparer.Ordinal).ToList();
        production.AddSource(ClassName + ".g.cs", SourceText.From(Write(composition, ordered), Encoding.UTF8));
    }

    // ------------------------------------------------------------------ a project's own permissions

    /// <summary>One permission of a <c>[Permissions]</c> class, as the catalogue lists it.</summary>
    private sealed record DeclaredPermission(string Name, string Description, EquatableArray<string> Implies, bool Retired);

    /// <summary>A <c>[Permissions]</c> class of this project and its permissions, in the order they are written.</summary>
    private sealed record DeclaredClass(string Name, EquatableArray<DeclaredPermission> Permissions);

    private static DeclaredClass? ReadDeclared(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type)
        {
            return null;
        }

        var permissions = new List<DeclaredPermission>();
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>().OrderBy(static field => field.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!field.IsConst || field.ConstantValue is not string permission || field.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            var levels = field.GetAttributes().Where(static each => DerivesFrom(each.AttributeClass, LevelBase)).ToList();
            if (levels.Count == 0)
            {
                // A permission without a level is the core's to report (DDD00092); it goes into no catalogue.
                continue;
            }

            string[]? implies = null;
            foreach (var level in levels)
            {
                foreach (var named in level.NamedArguments)
                {
                    if (named.Key == "Implies" && implies is null && !named.Value.IsNull)
                    {
                        implies = [.. named.Value.Values.Select(static value => value.Value as string).Where(static value => value is not null).Select(static value => value!)];
                    }
                }
            }

            // The Description said on a level first, the constant's summary where none is said; a permission with
            // neither is DDD00099, the core's to report, and is written with an empty description.
            permissions.Add(new DeclaredPermission(
                permission,
                PermissionDescriptions.Said(levels) ?? PermissionDescriptions.Summary(field, cancellationToken) ?? string.Empty,
                (implies ?? []).ToEquatableArray(),
                PermissionDescriptions.Retired(levels)));
        }

        return new DeclaredClass(type.ToDisplayString(), permissions.ToEquatableArray());
    }

    private static bool DerivesFrom(INamedTypeSymbol? type, string metadataName)
    {
        for (var current = type?.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == metadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static string WriteDeclared(Composition composition, ImmutableArray<DeclaredClass> declared)
    {
        var writer = new CodeWriter().Header();
        writer.Line("namespace " + composition.Namespace + ";");
        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// The permissions this project declares in its <c>[Permissions]</c> classes, as Tenancy's catalogue lists them, under");
        writer.Line("/// the module <c>" + Escaped(composition.Module) + "</c>: what the project that composes the modules collects, with every module's.");
        writer.Line("/// </summary>");
        writer.Line("/// <remarks>Written by Tenancy's generator; a permission is declared, and described, where its constant is.</remarks>");
        using (writer.Block("public static class " + DeclaredClassName))
        {
            writer.Line("/// <summary>Every permission of the project's <c>[Permissions]</c> classes, in the order they are written.</summary>");
            writer.Line("[global::" + AttributeName + "]");
            writer.Line("public static global::System.Collections.Generic.IReadOnlyList<" + Permission + "> All { get; } = new " + Permission + "[]");
            writer.Line("{");
            var order = 0;
            foreach (var each in declared.OrderBy(static each => each.Name, StringComparer.Ordinal))
            {
                foreach (var permission in each.Permissions)
                {
                    order += 10;
                    var implies = permission.Implies.Count == 0 ? "null" : "new string[] { " + string.Join(", ", permission.Implies.Select(Literal)) + " }";
                    writer.Line("    new(" + Literal(permission.Name) + ", " + Literal(composition.Module) + ", " + Literal(permission.Description) + ", Implies: " + implies
                                + ", Retired: " + (permission.Retired ? "true" : "false") + ", Order: " + order + "),");
                }
            }

            writer.Line("};");
        }

        return writer.ToString();
    }

    private static string Literal(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

    private static string Escaped(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Write(Composition composition, IReadOnlyList<string> lists)
    {
        var writer = new CodeWriter().Header();
        writer.Line("namespace " + composition.Namespace + ";");
        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// The permissions of every module this project references, each module's as Tenancy's generator lists them from its");
        writer.Line("/// <c>[Permissions]</c> classes: what the host adds to Tenancy's catalogue"
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

        writer.Line("/// A module that declares a <c>[Permissions]</c> class is in it as soon as this project references the module.");
        writer.Line("/// </remarks>");
        using (writer.Block("internal static class " + ClassName))
        {
            writer.Line("/// <summary>Every module's permissions, one list after the other.</summary>");
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
                writer.Line("/// list by hand. Call it once, in the project that composes the modules: a permission that is added twice stops the");
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
