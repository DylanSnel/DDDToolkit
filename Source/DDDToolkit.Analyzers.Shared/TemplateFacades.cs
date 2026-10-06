using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// Closes the generic classes packages name with <c>[assembly: TemplateFacade]</c> over the classes a project
/// declares with their templates, as a class of the project's own, named after its module, that derives from the
/// package's class: the module Tenants gets <c>TenantsTenancy</c>, and every project that references it writes
/// <c>TenantsTenancy.SeatCommands</c> rather than nine type arguments.
/// <para>
/// <b>A name, not a second type.</b> C# finds a type nested in a class through every class that derives from it,
/// so <c>TenantsTenancy.SeatOverview</c> is the package's own nested type, closed over the module's classes: the
/// type the package's registration added to the container, the one reflection and the compiler's messages show,
/// with the package's documentation. The class is abstract with a private constructor: nothing makes one, and
/// nothing derives from it.
/// </para>
/// <para>
/// <b>Why not a global alias.</b> An alias holds in the project that declares it and no further, so it would be
/// written into every project, each one paying for the work on every edit. And a generator's output is seen by the
/// compiler but not by the project's other generators: HotChocolate's, reading
/// <c>[ObjectType&lt;TenantsTenancy.KeyReach&gt;]</c> in an API project, would find no such name and write code
/// that does not compile. The class is written once, in the project that declares the classes, and reaches every
/// project above it through the reference, where the generators read it like any other type. A class of its own
/// that wrapped the package's could not do this either: the records nested in the package's class are the package's,
/// and only a class derived from it names them as they are.
/// </para>
/// <para>
/// <b>Not in the project that writes it.</b> There the same holds for the class itself: it is a generator's output,
/// so another generator of that project does not see it, in an attribute or in a method's signature, whose types
/// it reads from the code as written. Mediator's writes the name as it is spelled, which compiles; HotChocolate's
/// does not know the type, and writes <c>typeof(TenantsTenancy.RoleSummary?)</c> for a resolver that may answer
/// nothing, which does not. A module split by layer names the records in such code in its API project, above the
/// domain project, where all is well. A module of one project keeps an alias of exactly that name there,
/// <c>global using TenantsTenancy = ...;</c>, which every generator of that project reads; the generator then
/// stands back for it, and nothing else changes.
/// </para>
/// <para>
/// <b>Where.</b> In a project that declares a class with one of the type's templates. Each <c>[TemplateType]</c>
/// type parameter is filled as a registration's is: with the class of its template the project declares, its id, or
/// a later type argument of its template, and, for a template the project declares no class of, with the class the
/// projects of its module that it references declare (for a project that declares no module, the projects it
/// references that declare none). So in a module split by layer only the domain project gets it, and the
/// application, infrastructure and API projects and the host above it see it there. A project that declares the
/// classes of a module and sees another module's gets its own module's only: the other module's project wrote its.
/// </para>
/// <para>
/// <b>Its name.</b> The package's, with <c>{Module}</c> filled as every generated name of the project is filled:
/// <c>[assembly: Module]</c>, otherwise <c>DDD_Module</c>, otherwise the assembly's name without the dots. The
/// project that declares the classes may name it otherwise, with an <c>[assembly: TemplateFacade]</c> of its own for
/// the same type: a module called Tenancy, which would get <c>TenancyTenancy</c>, says
/// <c>[assembly: TemplateFacade(typeof(TenancyUseCases&lt;,,,,,,,,&gt;), "ShopTenancy")]</c>. A package asks for a class
/// of its own types only, so what another project of the application named stays that project's. The class is
/// written once, so every project that sees it calls it the same. It is declared in the global namespace, so no
/// project needs a using for it; a namespace of that name nearer to the code hides it there, as C# finds the
/// nearer name first.
/// </para>
/// <para>
/// <b>What keeps it from being written</b> is said where the classes are declared, DDD00065, as information: a
/// template with no class, or several, or a class that does not meet the package's constraints. Without it a
/// project above would only hear that the name does not exist. A template whose class a parent of the project
/// needs is the parent's error already, DDD00044 or DDD00045, one a registration the project can call takes from
/// is that registration's, DDD00049 or DDD00045, and a class that cannot be generated has its own diagnostic, so
/// none of them is said twice. Nor is an id without a <c>Create()</c> where the package's class asks for one,
/// <c>where TId : ICreatableEntityId&lt;TId&gt;</c>, and the template whose class the id is of says its package makes
/// the ids (<c>CreatesIds</c>): DDD00067 says it on that class; a template that does not say so has it said here.
/// A type of the name the project declares in a namespace keeps the class
/// out too, with DDD00065 on that type: a class of the name in the global namespace would win over it in every
/// file that imports its namespace with a using. What the application wrote in the global namespace stays its own
/// without a word: a type or a namespace of the name, its own or one it references, or an alias of the name at the
/// top of one of its files, the form every project wrote before. A project above it that still writes such an
/// alias of the very name is told so by the compiler, CS0576, since the class and the alias would be two things of
/// one name.
/// </para>
/// </summary>
internal static class TemplateFacades
{
    /// <summary>What a package's name for the class says the module goes in.</summary>
    private const string ModulePlaceholder = "{Module}";

    /// <summary>The classes to write into this project, one file each, in the order of their names, and what is said of those it does not get.</summary>
    /// <param name="declared">The classes this project declares with a template.</param>
    /// <param name="compilation">The project.</param>
    /// <param name="moduleName">What <c>{Module}</c> is filled with: the module as every generated name of the project has it.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <param name="standingBack">The templates whose class a package's switch was asked to write and could not: DDD00066 says why, and nothing is said again here.</param>
    public static ImmutableArray<FacadeOutcome> Resolve(
        ImmutableArray<EntityDefinition> declared,
        Compilation compilation,
        string moduleName,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? standingBack = null)
    {
        // The common case, and the cheap one: only a project that declares a template class gets a class, so the
        // projects above it, which only see the classes, never read a reference's attributes for it.
        if (declared.IsDefaultOrEmpty)
        {
            return ImmutableArray<FacadeOutcome>.Empty;
        }

        var asked = FacadeTypesIn(compilation, cancellationToken);
        if (asked.Count == 0)
        {
            return ImmutableArray<FacadeOutcome>.Empty;
        }

        // Whether a class of this project can be generated is only known once every template class is resolved.
        ImmutableArray<EntityDefinition>? resolved = null;
        ImmutableArray<EntityDefinition> Resolved() => resolved ??= DefinitionFactory.ResolveTemplates(declared, compilation, cancellationToken);

        // The templates a class of this project takes a type from for its own parent: one missing or declared
        // twice is that class's error, DDD00044 or DDD00045, with its fix, and is not said again here.
        // So is one a registration this project can call takes from: DDD00049 or DDD00045 is said here then. And so is
        // one a package's switch was asked for and could not write: DDD00066 says why.
        var saidElsewhere = new HashSet<string>(
            declared.Where(static definition => definition.CanGenerate && definition.Template is not null)
                .SelectMany(static definition => definition.Template!.Bindings)
                .Select(static binding => binding.SourceKey)
                .Concat(standingBack ?? []),
            StringComparer.Ordinal);
        bool SaidElsewhere(TemplateRegistrations.Take take)
            => saidElsewhere.Contains(take.Key) || TemplateRegistrations.Registers(compilation, take.MetadataName, cancellationToken);

        var module = ModuleBoundary.ModuleOf(compilation.Assembly);
        var candidates = new Dictionary<string, List<DefinitionFactory.TemplateSource>>(StringComparer.Ordinal);
        List<DefinitionFactory.TemplateSource> CandidatesFor(TemplateRegistrations.Take take)
        {
            if (candidates.TryGetValue(take.Key, out var known))
            {
                return known;
            }

            var found = new List<DefinitionFactory.TemplateSource>();
            for (var index = 0; index < declared.Length; index++)
            {
                if (declared[index].TemplateKey == take.Key)
                {
                    found.Add(TemplateRegistrations.SourceOf(declared[index], Resolved()[index].CanGenerate));
                }
            }

            if (found.Count == 0)
            {
                // A class of another module is never taken: a project of a module takes from the projects of its
                // module it references, and a project of none from the projects of none.
                found.AddRange(module is not null
                    ? DefinitionFactory.ReferencedSources(compilation, take.MetadataName, cancellationToken, module)
                    : DefinitionFactory.ReferencedSources(compilation, take.MetadataName, cancellationToken)
                        .Where(source => ModuleBoundary.ModuleOf(source.SymbolIn(compilation)?.ContainingAssembly) is null));
            }

            candidates.Add(take.Key, found);
            return found;
        }

        var outcomes = new List<FacadeOutcome>();
        foreach (var (type, pattern) in asked)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The class the application gets leaves no type parameter open, so each of them takes from a template.
            if (TemplateRegistrations.TakesOf(type.TypeParameters) is not { } read || read.Any(static take => take is null))
            {
                continue;
            }

            var takes = read.Select(static take => take!).ToArray();
            if (declared.FirstOrDefault(definition => definition.TemplateKey is { } key && takes.Any(take => take.Key == key)) is not { } anchor)
            {
                continue;
            }

            var name = pattern.Replace(ModulePlaceholder, moduleName);
            if (!SyntaxFacts.IsValidIdentifier(name) || SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None)
            {
                continue;
            }

            var closing = new Closing(type, name, takes, anchor.Type.Location, module);
            if (Closed(closing, CandidatesFor, SaidElsewhere, compilation, cancellationToken) is { } outcome)
            {
                outcomes.Add(outcome.File is not null && OwnTypeNamed(name, compilation, cancellationToken) is { } own ? HiddenBy(closing, own) : outcome);
            }
        }

        if (outcomes.Count == 0)
        {
            return ImmutableArray<FacadeOutcome>.Empty;
        }

        // Read only now: a project that gets no class and hears nothing never walks its files. What the application
        // keeps in the global namespace stands, and nothing is said of a class it names that way itself.
        var taken = AliasesIn(compilation, cancellationToken);
        return outcomes
            .GroupBy(static outcome => outcome.Name, StringComparer.Ordinal)
            .Where(named => named.Count() == 1 && !taken.Contains(named.Key) && !compilation.GlobalNamespace.GetMembers(named.Key).Any())
            .Select(static named => named.Single())
            .OrderBy(static outcome => outcome.Name, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>What one asked class is closed over, and what is said about it.</summary>
    /// <param name="Type">The package's open generic class.</param>
    /// <param name="Name">What the application's class is called.</param>
    /// <param name="Takes">What each type parameter takes.</param>
    /// <param name="Where">The first class of this project with one of the type's templates: where DDD00065 goes.</param>
    /// <param name="Module">The project's <c>[assembly: Module]</c>, for the class's documentation.</param>
    private sealed record Closing(INamedTypeSymbol Type, string Name, TemplateRegistrations.Take[] Takes, LocationInfo? Where, string? Module);

    /// <summary>
    /// The package's class closed over the one class of each of its templates; DDD00065 instead when a template
    /// has none or several, or when what fills a type parameter does not meet its constraints; and nothing at all
    /// when what is wrong is said elsewhere: a class that cannot be generated or has no entity id where one is
    /// taken, and a template a parent of this project, or a registration it can call, takes from.
    /// </summary>
    private static FacadeOutcome? Closed(
        Closing closing,
        Func<TemplateRegistrations.Take, List<DefinitionFactory.TemplateSource>> candidatesFor,
        Func<TemplateRegistrations.Take, bool> saidElsewhere,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var (type, name, takes, _, module) = closing;
        var parameters = type.TypeParameters;
        var texts = new string[parameters.Length];
        var symbols = new ITypeSymbol?[parameters.Length];
        var shown = new string[parameters.Length];
        for (var position = 0; position < parameters.Length; position++)
        {
            var take = takes[position];
            var found = candidatesFor(take);
            if (found.Count != 1)
            {
                return saidElsewhere(take)
                    ? null
                    : NotWritten(closing, found.Count == 0
                        ? "no class is declared with [" + take.AttributeName + "], in this project or in "
                          + (module is not null ? "a project of the module " + module + " that it references" : "a project it references that declares no module either")
                        : "several classes are declared with [" + take.AttributeName + "], "
                          + DefinitionFactory.Listed(found.Select(static source => "'" + source.FullyQualifiedName.Replace("global::", string.Empty) + "'"))
                          + ", and it is closed over one; " + DefinitionFactory.KeepOne);
            }

            var source = found[0];
            var symbol = source.SymbolIn(compilation);
            if (take.TakeType)
            {
                if (!source.CanGenerate)
                {
                    return null;
                }

                (texts[position], symbols[position], shown[position]) = (source.FullyQualifiedName, symbol, source.Name);
            }
            else if (take.Argument == 0)
            {
                if (!source.IdIsEntityId)
                {
                    return null;
                }

                var id = TemplateRegistrations.IdSymbolOf(source, symbol, compilation);
                (texts[position], symbols[position], shown[position]) = (source.IdType, id, id?.Name ?? TemplateRegistrations.LastNameOf(source.IdType) ?? source.IdType);
            }
            else if (!take.IdOfArgument
                     && (symbol is null || EntityDeclarations.TemplateArgumentOf(symbol, take.Key, take.Argument) is null)
                     && TemplateRegistrations.WrittenArgumentOf(source, take.Argument) is { } written)
            {
                // An id a package's switch writes in this project, which no generator sees as a type: a struct.
                if (parameters[position].HasReferenceTypeConstraint)
                {
                    return null;
                }

                (texts[position], symbols[position], shown[position]) = (written, null, TemplateRegistrations.LastNameOf(written) ?? written);
            }
            else if (symbol is null
                     || EntityDeclarations.TemplateArgumentOf(symbol, take.Key, take.Argument) is not { } argument
                     || TemplateRegistrations.MentionsATypeParameter(argument))
            {
                return null;
            }
            else if (!take.IdOfArgument)
            {
                (texts[position], symbols[position], shown[position]) = (argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), argument, argument.Name);
            }
            else if (argument is not INamedTypeSymbol named
                     || DefinitionFactory.IdOf(named, cancellationToken, out var idText, out var idName, out var idSymbol) != DefinitionFactory.DeclaredIdKind.Found
                     || (idSymbol is null && parameters[position].HasReferenceTypeConstraint))
            {
                // An id still to be generated is a struct.
                return null;
            }
            else
            {
                (texts[position], symbols[position], shown[position]) = (idText, idSymbol, idName);
            }
        }

        // The class derives from what it is closed over, so a constraint that is not met would be the compiler's
        // error inside code nobody wrote. The package's registrations say the same, but in the project that
        // registers it, which in a module split by layer does not build once the projects above fail on the name.
        for (var position = 0; position < parameters.Length; position++)
        {
            var take = takes[position];
            if (symbols[position] is not { } argument)
            {
                continue;
            }

            var unmet = take.TakeType
                ? argument is INamedTypeSymbol taken ? TemplateRegistrations.UnmetConstraint(parameters[position], taken, parameters, symbols, compilation) : null
                : take.Argument == 0 || take.IdOfArgument
                    ? argument is INamedTypeSymbol id ? TemplateRegistrations.UnmetIdConstraint(parameters[position], id) : null
                    : TemplateRegistrations.UnmetByALaterArgument(parameters[position], argument, parameters, symbols, compilation, cancellationToken);
            if (unmet is not null)
            {
                return NotWritten(closing, "it takes '" + shown[position] + "' as '" + parameters[position].Name + "', which requires " + unmet + "; '" + shown[position] + "' does not meet it");
            }

            // An id the package makes new ones of with Create(), which it does not have. The class declared with a
            // template whose package says it makes their ids says so itself, DDD00067; any other is said here.
            if (!take.TakeType && IdCreation.IsUnmet(parameters[position], argument, compilation, cancellationToken))
            {
                return take.Argument == 0 && IdCreation.IsSaidOnTheClass(take.MetadataName, compilation)
                    ? null
                    : NotWritten(closing, "it takes '" + shown[position] + "' as '" + parameters[position].Name + "', which requires ICreatableEntityId<" + shown[position] + ">, an id that makes a new one with Create(); '" + shown[position] + "' " + IdCreation.ShortfallOf((INamedTypeSymbol)argument).Lacks);
            }
        }

        var qualifier = type.ContainingType is { } outer
            ? outer.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "."
            : type.ContainingNamespace is { IsGlobalNamespace: false } scope ? "global::" + scope.ToDisplayString() + "." : "global::";

        // Public where everything it is closed over is, so the projects above see it; otherwise as far as the
        // application's own internal class reaches, which a public class could not derive over.
        var isPublic = IsPublicEverywhere(type) && symbols.All(static symbol => symbol is null || IsPublicEverywhere(symbol));
        var over = module is not null ? "the classes of the module " + module : "the classes of this project";
        return new FacadeOutcome(
            name,
            new FacadeFile(
                HintName: name + ".TemplateFacade.g.cs",
                Name: name,
                IsPublic: isPublic,
                BaseType: qualifier + type.Name + "<" + string.Join(", ", texts) + ">",
                Summary: CodeWriter.XmlText(type.Name + ", closed over " + over + ": " + DefinitionFactory.Listed(shown) + ".")),
            NotWritten: null);
    }

    /// <summary>DDD00065 on the first class of the project with one of the type's templates.</summary>
    private static FacadeOutcome NotWritten(Closing closing, string why)
        => new(closing.Name, File: null, DiagnosticInfo.Create(DiagnosticDescriptors.TemplateFacadeNotWritten, closing.Where, closing.Name, closing.Type.Name, why));

    /// <summary>
    /// A type of the name this project declares in a namespace, not nested in another type: a class of the name in
    /// the global namespace would win over it in every file that imports its namespace with a using, since C# looks
    /// in the global namespace before it looks in what a file imports. A nested type is reached through the type it
    /// is nested in, and is no matter. Null when there is none, which the declarations tell cheaply.
    /// </summary>
    private static INamedTypeSymbol? OwnTypeNamed(string name, Compilation compilation, CancellationToken cancellationToken)
        => !compilation.ContainsSymbolsWithName(name, SymbolFilter.Type, cancellationToken)
            ? null
            : compilation.GetSymbolsWithName(name, SymbolFilter.Type, cancellationToken)
                .OfType<INamedTypeSymbol>()
                .Where(static symbol => symbol.ContainingType is null && !symbol.ContainingNamespace.IsGlobalNamespace)
                .OrderBy(static symbol => symbol.ToDisplayString(), StringComparer.Ordinal)
                .FirstOrDefault();

    /// <summary>DDD00065 on the type of the project's own that the class would hide, with the two ways out.</summary>
    private static FacadeOutcome HiddenBy(Closing closing, INamedTypeSymbol own)
        => new(
            closing.Name,
            File: null,
            DiagnosticInfo.Create(
                DiagnosticDescriptors.TemplateFacadeNotWritten,
                LocationInfo.From(own) ?? closing.Where,
                closing.Name,
                closing.Type.Name,
                "this project declares '" + own.ToDisplayString() + "', which a class of that name in the global namespace would hide in every file that imports '"
                + own.ContainingNamespace.ToDisplayString() + "' with a using; name the class otherwise, with [assembly: TemplateFacade(typeof("
                + closing.Type.Name + "<" + new string(',', closing.Type.Arity - 1) + ">), \"...\")] in this project, or rename '" + own.Name + "'"));

    /// <summary>Whether a type, and every type it is made of, can be seen from any assembly.</summary>
    private static bool IsPublicEverywhere(ITypeSymbol type)
        => type switch
        {
            IArrayTypeSymbol array => IsPublicEverywhere(array.ElementType),
            INamedTypeSymbol named => named.DeclaredAccessibility == Accessibility.Public
                                      && (named.ContainingType is null || IsPublicEverywhere(named.ContainingType))
                                      && named.TypeArguments.All(IsPublicEverywhere),
            _ => true,
        };

    // ------------------------------------------------------------------ finding the classes

    /// <summary>
    /// The classes this project gets: what a package it references asks for its own types, unless this project
    /// names that type itself, with an <c>[assembly: TemplateFacade]</c> of its own, which then stands instead. Each
    /// a generic class that is neither static nor sealed, with a constructor a class of another assembly can call,
    /// not nested in a generic type, and asked with a name that has nothing in braces but <c>{Module}</c>.
    /// <para>
    /// A package asks for its own types only: the attribute another project of the application declares, to name
    /// the class it gets, is that project's, and a project above it is not asked for a second class by it.
    /// </para>
    /// </summary>
    private static List<(INamedTypeSymbol Type, string Pattern)> FacadeTypesIn(Compilation compilation, CancellationToken cancellationToken)
    {
        var found = new List<(INamedTypeSymbol, string)>();
        var seen = new HashSet<(INamedTypeSymbol, string)>(new TypeAndPatternComparer());

        // The project's own are read on every edit: they change as it is edited, and are few.
        var own = FacadeTypesOf(compilation.Assembly);
        var named = new HashSet<INamedTypeSymbol>(own.Select(static each => each.Type), SymbolEqualityComparer.Default);
        foreach (var each in own)
        {
            Add(each);
        }

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var each in ByAssembly.GetValue(assembly, FacadeTypesOf))
            {
                if (SymbolEqualityComparer.Default.Equals(each.Type.ContainingAssembly, assembly) && !named.Contains(each.Type))
                {
                    Add(each);
                }
            }
        }

        return found;

        void Add((INamedTypeSymbol Type, string Pattern) each)
        {
            if (seen.Add(each) && compilation.IsSymbolAccessibleWithin(each.Type, compilation.Assembly))
            {
                found.Add(each);
            }
        }
    }

    /// <summary>
    /// The classes a referenced assembly asks for, read once per assembly, as its registrations are: a referenced
    /// assembly does not change while the project that references it is being edited.
    /// </summary>
    private static readonly ConditionalWeakTable<IAssemblySymbol, (INamedTypeSymbol Type, string Pattern)[]> ByAssembly = new();

    private static (INamedTypeSymbol Type, string Pattern)[] FacadeTypesOf(IAssemblySymbol assembly)
    {
        var asked = new List<(INamedTypeSymbol, string)>();
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass
                || !EntityDeclarations.Is(attributeClass, KnownTypes.TemplateFacadeAttribute)
                || attribute.ConstructorArguments.Length != 2
                || attribute.ConstructorArguments[0] is not { Kind: TypedConstantKind.Type, Value: INamedTypeSymbol { TypeKind: TypeKind.Class, IsGenericType: true, IsStatic: false, IsSealed: false } type }
                || attribute.ConstructorArguments[1].Value is not string pattern
                || !IsAName(pattern)
                || InAGenericType(type)
                || !CanBeDerivedFrom(type.OriginalDefinition))
            {
                continue;
            }

            asked.Add((type.OriginalDefinition, pattern));
        }

        return asked.ToArray();
    }

    /// <summary>Whether a name is one a class can have, once <c>{Module}</c> is filled with a module's name.</summary>
    private static bool IsAName(string pattern)
        => pattern.Replace(ModulePlaceholder, "Module") is { Length: > 0 } filled
           && filled.IndexOf('{') < 0
           && filled.IndexOf('}') < 0
           && SyntaxFacts.IsValidIdentifier(filled);

    private static bool InAGenericType(INamedTypeSymbol type)
    {
        for (var outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            if (outer.IsGenericType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a class of the application, in another assembly, can derive from the type: a constructor without parameters it can call.</summary>
    private static bool CanBeDerivedFrom(INamedTypeSymbol type)
        => type.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0
                                                             && constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal);

    /// <summary>
    /// The names this project gives an alias of its own, global or not, at the top of a file, where a class of the
    /// same name in the global namespace would be the compiler's error. An alias inside a namespace only hides the
    /// class there.
    /// </summary>
    private static HashSet<string> AliasesIn(Compilation compilation, CancellationToken cancellationToken)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (tree.GetRoot(cancellationToken) is not CompilationUnitSyntax unit)
            {
                continue;
            }

            foreach (var directive in unit.Usings)
            {
                if (directive.Alias is { } alias)
                {
                    taken.Add(alias.Name.Identifier.ValueText);
                }
            }
        }

        return taken;
    }

    private sealed class TypeAndPatternComparer : IEqualityComparer<(INamedTypeSymbol Type, string Pattern)>
    {
        public bool Equals((INamedTypeSymbol Type, string Pattern) x, (INamedTypeSymbol Type, string Pattern) y)
            => SymbolEqualityComparer.Default.Equals(x.Type, y.Type) && x.Pattern == y.Pattern;

        public int GetHashCode((INamedTypeSymbol Type, string Pattern) obj)
            => (SymbolEqualityComparer.Default.GetHashCode(obj.Type) * 31) + StringComparer.Ordinal.GetHashCode(obj.Pattern);
    }
}

/// <summary>What one class a package asks for comes to in a project: the file the generator writes, or why it writes none.</summary>
/// <param name="Name">What the class is called, <c>TenantsTenancy</c>.</param>
/// <param name="File">The file, or null when the class is not written.</param>
/// <param name="NotWritten">DDD00065, saying why the class is not written; null when it is, or when what keeps it out is said elsewhere.</param>
internal sealed record FacadeOutcome(string Name, FacadeFile? File, DiagnosticInfo? NotWritten);

/// <summary>One class a project gets: the file the generator writes.</summary>
/// <param name="HintName">The file's name, <c>TenantsTenancy.TemplateFacade.g.cs</c>.</param>
/// <param name="Name">What the class is called, <c>TenantsTenancy</c>.</param>
/// <param name="IsPublic">Whether it is public; otherwise internal, as far as an internal class it is closed over reaches.</param>
/// <param name="BaseType">The package's class closed over the application's, fully qualified.</param>
/// <param name="Summary">What it is closed over, for its documentation, escaped for XML.</param>
internal sealed record FacadeFile(string HintName, string Name, bool IsPublic, string BaseType, string Summary);
