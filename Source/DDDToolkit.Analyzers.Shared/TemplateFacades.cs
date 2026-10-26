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
/// declares with their templates, as a class of the project's own, named as the package's class is without its type
/// parameters, that derives from it: Tenancy's <c>TenancyUseCases&lt;...&gt;</c> gives <c>TenancyUseCases</c>, and
/// every project that references the project writes <c>TenancyUseCases.SeatCommands</c> rather than nine type
/// arguments.
/// <para>
/// <b>A name, not a second type.</b> C# finds a type nested in a class through every class that derives from it,
/// so <c>TenancyUseCases.SeatOverview</c> is the package's own nested type, closed over the module's classes: the
/// type the package's registration added to the container, the one reflection and the compiler's messages show,
/// with the package's documentation. The class is abstract with a private constructor: nothing makes one, and
/// nothing derives from it.
/// </para>
/// <para>
/// <b>Why not a global alias.</b> An alias holds in the project that declares it and no further, so it would be
/// written into every project, each one paying for the work on every edit. And a generator's output is seen by the
/// compiler but not by the project's other generators: HotChocolate's, reading
/// <c>[ObjectType&lt;TenancyUseCases.KeyReach&gt;]</c> in an API project, would find no such name and write code
/// that does not compile. The class is written once, in the project that declares the classes, and reaches every
/// project above it through the reference, where the generators read it like any other type. A class of its own
/// that wrapped the package's could not do this either: the records nested in the package's class are the package's,
/// and only a class derived from it names them as they are.
/// </para>
/// <para>
/// <b>Not in the project that writes it.</b> There the same holds for the class itself: it is a generator's output,
/// so another generator of that project does not see it, in an attribute or in a method's signature, whose types
/// it reads from the code as written. Mediator's writes the name as it is spelled, which compiles; HotChocolate's
/// does not know the type, and writes <c>typeof(TenancyUseCases.RoleSummary?)</c> for a resolver that may answer
/// nothing, which does not. A module split by layer names the records in such code in its API project, above the
/// domain project, where all is well. A module of one project keeps an alias of exactly that name there,
/// <c>global using TenancyUseCases = ...;</c>, which every generator of that project reads; the generator then
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
/// <b>Its name.</b> The package's class's, without the type parameters: the one name the package's documentation
/// uses, whatever the module is called, and no clash with the package's class, which C# tells apart by its type
/// parameters. The project that declares the classes may give it another with
/// <c>[assembly: TemplateFacadeName("TenancyUseCases", "CustomersTenancy")]</c>, which names the class by the
/// package's name for it, or by its full name where two packages' classes share one; a module whose classes are split
/// over two projects may say it in either, and the project that writes the class reads it in the projects of its
/// module it takes classes from. The class is written once, so every project that sees it calls it the same. It is
/// declared in the global namespace, so no project needs a using for it; a namespace of that name nearer to the code
/// hides it there, as C# finds the nearer name first.
/// </para>
/// <para>
/// <b>Two of one name</b> are DDD00075, a warning, with the line that names one of them written out, or, where the
/// application named it already, the line to give another name: two modules of an application that each declare the
/// classes of one package get a class of one name each, and they meet in every project that sees both. That is where
/// it is said. A project that sees two such classes the generator wrote into the projects it references, the host
/// first, says it at its project file, since no line of its code is wrong. One that declares the classes itself and
/// sees another module's class of its name gets its own all the same and says it on its first class: C# binds the
/// name in its own code to its own class, CS0436, and every project above it sees both, is told so there and cannot
/// name either, CS0433. Left without one, its code and every project above would be closed over the other module's
/// classes, with nothing said above it. Two classes one project gets that come to one name, two packages' classes of
/// one name or a name given that another one has, are both left out, and said the same way. A module whose projects
/// see no other module's class is told nothing.
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
/// without a word: a type or a namespace of the package's name, its own or one it references, or an alias of the name
/// at the top of one of its files, the form every project wrote before. A project above it that still writes such an
/// alias of the very name is told so by the compiler, CS0576, since the class and the alias would be two things of
/// one name. A <c>[assembly: TemplateFacadeName]</c> that changes nothing is DDD00076, at the line: among them one that
/// gives a name a namespace or a type in the global namespace has already, which would leave the class out with no
/// word, and the projects above with only CS0234; an alias of that name the project writes itself stays its own.
/// </para>
/// </summary>
internal static class TemplateFacades
{
    /// <summary>The classes to write into this project, one file each, in the order of their names, and what is said of those it does not get.</summary>
    /// <param name="declared">The classes this project declares with a template.</param>
    /// <param name="compilation">The project.</param>
    /// <param name="moduleName">The module as every generated name of the project has it: what the name DDD00075 suggests for this project's class starts with.</param>
    /// <param name="projectFile">The project file: where DDD00075 goes for two classes the projects this one references have.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <param name="standingBack">The templates whose class a package's switch was asked to write and could not: DDD00066 says why, and nothing is said again here.</param>
    public static ImmutableArray<FacadeOutcome> Resolve(
        ImmutableArray<EntityDefinition> declared,
        Compilation compilation,
        string moduleName,
        LocationInfo? projectFile,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? standingBack = null)
    {
        // Most projects reference no package that asks for a class and name none, and are done here, having read the
        // attributes of their references once each.
        var asked = FacadeTypesIn(compilation, cancellationToken);
        var given = NamesGivenIn(compilation, cancellationToken);
        if (asked.Count == 0 && given.Count == 0)
        {
            return ImmutableArray<FacadeOutcome>.Empty;
        }

        var outcomes = new List<FacadeOutcome>();
        var names = NamesOf(asked, given, outcomes);

        // What the projects this one references were given, by name: read once per referenced assembly, as their
        // attributes are, so a project above the classes pays for a look at its references and nothing more.
        var written = asked.Count == 0
            ? new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal)
            : WrittenInReferences(compilation, cancellationToken);

        var own = declared.IsDefaultOrEmpty || asked.Count == 0
            ? new List<(Closing Closing, FacadeOutcome? Outcome)>()
            : Own(declared, compilation, asked, names, outcomes, cancellationToken, standingBack);

        // A line that names a class of a package this project declares none of the classes of names nothing here: the
        // class is written where they are.
        var anchored = new HashSet<INamedTypeSymbol>(own.Select(static each => each.Closing.Type), SymbolEqualityComparer.Default);
        foreach (var pair in names)
        {
            if (!anchored.Contains(pair.Key))
            {
                outcomes.Add(NamesNothing(pair.Value, "this project declares no class with the templates of " + pair.Key.Name + ", so it gets no class of it to name; the line goes in the project that declares them, where the class is written"));
            }
        }

        // Read only now: a project that gets no class and hears nothing never walks its files. What the application
        // keeps in the global namespace stands, and nothing is said of a class it names that way itself.
        var said = own.Where(static each => each.Outcome is not null).Select(static each => (each.Closing, Outcome: each.Outcome!)).ToList();
        var taken = said.Count == 0 ? new HashSet<string>(StringComparer.Ordinal) : AliasesIn(compilation, cancellationToken);
        var metHere = new HashSet<string>(StringComparer.Ordinal);
        foreach (var named in said.GroupBy(static each => each.Outcome.Name, StringComparer.Ordinal))
        {
            var name = named.Key;
            var mine = named.ToList();
            if (taken.Contains(name))
            {
                continue;
            }

            // What the global namespace has of the name already stays: the package's name stands back for it without
            // a word, as the application's own way of naming the classes. A name the application gave is another
            // matter: the line would leave the class out and say nothing, so it is told at the line.
            if (TakenInTheGlobalNamespace(name, compilation, written) is { } what)
            {
                foreach (var each in mine)
                {
                    if (each.Closing.Line is { } line)
                    {
                        outcomes.Add(NamesNothing(line, line.Where ?? each.Closing.Where, "'" + name + "' is the name of " + what + " this project sees, which the class cannot have as well; give another name"));
                    }
                }

                continue;
            }

            // Two classes of this project that come to one name: neither could be told from the other.
            var files = mine.Where(static each => each.Outcome.File is not null).ToList();
            if (files.Count > 1)
            {
                outcomes.Add(MetInThisProject(name, files.Select(static each => each.Closing).ToList()));
                outcomes.AddRange(mine.Where(static each => each.Outcome.File is null).Select(static each => each.Outcome));
                continue;
            }

            // Another module's class of the name, which this project sees. Its own is written all the same: C# binds
            // the name in this project's code to the class of its own source, CS0436, so its code names its own
            // module's classes, and every project above sees two, which DDD00075 says there and CS0433 stops at the
            // first use. Without its own, its code and every project above would name the other module's classes,
            // and only this warning would say so. It is said here, on the classes.
            if (files.Count == 1 && written.TryGetValue(name, out var others))
            {
                metHere.Add(name);
                outcomes.AddRange(mine.Select(static each => each.Outcome));
                outcomes.Add(MetWithAReference(files[0].Closing, moduleName, others));
                continue;
            }

            outcomes.AddRange(mine.Select(static each => each.Outcome));
        }

        // Two classes of one name the projects this one references have, which meet here, where it sees both.
        foreach (var pair in written)
        {
            if (pair.Value.Count > 1 && !metHere.Contains(pair.Key))
            {
                outcomes.Add(MetInReferences(pair.Key, pair.Value, projectFile));
            }
        }

        return outcomes.Count == 0
            ? ImmutableArray<FacadeOutcome>.Empty
            : outcomes
                .OrderBy(static outcome => outcome.Name, StringComparer.Ordinal)
                .ThenBy(static outcome => outcome.Diagnostic?.Descriptor.Id, StringComparer.Ordinal)
                .ToImmutableArray();
    }

    /// <summary>
    /// The classes this project gets, each with its outcome before the names are compared: the class's file, DDD00065
    /// for one that cannot be written, or none when what is wrong is said elsewhere. Every class whose templates the
    /// project declares a class of is here, so a line that names one of them names something.
    /// </summary>
    private static List<(Closing Closing, FacadeOutcome? Outcome)> Own(
        ImmutableArray<EntityDefinition> declared,
        Compilation compilation,
        List<INamedTypeSymbol> asked,
        Dictionary<INamedTypeSymbol, GivenName> names,
        List<FacadeOutcome> outcomes,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? standingBack)
    {
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

        var own = new List<(Closing, FacadeOutcome?)>();
        foreach (var type in asked)
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

            // A line of this project names the class; so does one in a project of its module it takes classes from: a
            // module whose classes are split over two projects says it beside the module in either, and the class is
            // written in the second. A second name for one class changes nothing, and is told.
            var line = names.TryGetValue(type, out var here) ? here : null;
            foreach (var there in LinesWhereItTakesFrom(type, takes, asked, CandidatesFor, compilation))
            {
                if (line is null)
                {
                    line = there;
                }
                else if (!string.Equals(line.Name, there.Name, StringComparison.Ordinal))
                {
                    outcomes.Add(NamesNothing(there, line.Where ?? anchor.Type.Location, (line.In is null ? "a line of this project" : "a line in '" + line.In + "'")
                                                                                         + " names it '" + line.Name + "' already, and that one stands; keep one"));
                }
            }

            var name = line?.Name ?? type.Name;
            var closing = new Closing(type, name, takes, anchor.Type.Location, module, line);
            var outcome = Closed(closing, CandidatesFor, SaidElsewhere, compilation, cancellationToken);
            own.Add((closing, outcome?.File is not null && OwnTypeNamed(name, compilation, cancellationToken) is { } mine ? HiddenBy(closing, mine) : outcome));
        }

        return own;
    }

    /// <summary>What one asked class is closed over, and what is said about it.</summary>
    /// <param name="Type">The package's open generic class.</param>
    /// <param name="Name">What the application's class is called.</param>
    /// <param name="Takes">What each type parameter takes.</param>
    /// <param name="Where">The first class of this project with one of the type's templates: where DDD00065 and DDD00075 go.</param>
    /// <param name="Module">The project's <c>[assembly: Module]</c>, for the class's documentation.</param>
    /// <param name="Line">
    /// The <c>[assembly: TemplateFacadeName]</c> the application named the class with, in this project or in a project
    /// of its module it takes classes from; null when it is named as the package's class is.
    /// </param>
    private sealed record Closing(INamedTypeSymbol Type, string Name, TemplateRegistrations.Take[] Takes, LocationInfo? Where, string? Module, GivenName? Line);

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
        var (type, name, takes, _, module, line) = closing;
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
                Summary: CodeWriter.XmlText(type.Name + ", closed over " + over + ": " + DefinitionFactory.Listed(shown) + "."),
                Naming: CodeWriter.XmlText(line is not null
                    ? "Named by " + line.Written + "."
                    : "Named as the package's class is; [assembly: TemplateFacadeName(\"" + type.Name + "\", \"...\")] in this project names it otherwise.")),
            Diagnostic: null);
    }

    /// <summary>DDD00065 on the first class of the project with one of the type's templates.</summary>
    private static FacadeOutcome NotWritten(Closing closing, string why)
        => new(closing.Name, File: null, DiagnosticInfo.Create(DiagnosticDescriptors.TemplateFacadeNotWritten, closing.Where, closing.Name, closing.Type.Name, why));

    /// <summary>
    /// A type of the name this project declares in a namespace, not nested in another type and without type
    /// parameters: a class of the name in the global namespace would win over it in every file that imports its
    /// namespace with a using, since C# looks in the global namespace before it looks in what a file imports. A nested
    /// type is reached through the type it is nested in, and a generic one, the package's own class among them where
    /// the package is compiled with the application, is told apart by its type parameters, so neither is any matter.
    /// Null when there is none, which the declarations tell cheaply.
    /// </summary>
    private static INamedTypeSymbol? OwnTypeNamed(string name, Compilation compilation, CancellationToken cancellationToken)
        => !compilation.ContainsSymbolsWithName(name, SymbolFilter.Type, cancellationToken)
            ? null
            : compilation.GetSymbolsWithName(name, SymbolFilter.Type, cancellationToken)
                .OfType<INamedTypeSymbol>()
                .Where(static symbol => symbol.ContainingType is null && symbol.Arity == 0 && !symbol.ContainingNamespace.IsGlobalNamespace)
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
                + own.ContainingNamespace.ToDisplayString() + "' with a using; name the class otherwise, with "
                + (closing.Line is { } line ? "another name in " + line.Written : NameLine(closing.Type.Name, "...") + " in this project")
                + ", or rename '" + own.Name + "'"));

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

    // ------------------------------------------------------------------ two classes of one name

    /// <summary>
    /// What the global namespace has of a name already, which the class cannot have as well: a namespace, or a type
    /// without type parameters this project can see, its own or a referenced one, that the generator did not write for
    /// another module. A generic type of the name is told apart from the class by its type parameters, and one the
    /// project cannot see is in nobody's way. Null when the name is free.
    /// </summary>
    private static string? TakenInTheGlobalNamespace(string name, Compilation compilation, Dictionary<string, List<INamedTypeSymbol>> written)
        => compilation.GlobalNamespace.GetMembers(name)
            .Select(member => member switch
            {
                INamespaceSymbol => "a namespace",
                INamedTypeSymbol { Arity: 0 } type when compilation.IsSymbolAccessibleWithin(type, compilation.Assembly)
                                                        && (!written.TryGetValue(type.Name, out var classes) || !classes.Contains(type, SymbolEqualityComparer.Default))
                    => "a type in the global namespace",
                _ => null,
            })
            .FirstOrDefault(static what => what is not null);

    /// <summary>
    /// DDD00075 on the first of this project's classes: two classes it gets come to one name, and neither is written.
    /// The line it writes out names one the application has not named yet where there is one, since a second line for
    /// a class would change nothing.
    /// </summary>
    private static FacadeOutcome MetInThisProject(string name, List<Closing> closings)
    {
        var first = closings
            .OrderBy(static closing => closing.Line is not null)
            .ThenBy(static closing => closing.Type.ToDisplayString(), StringComparer.Ordinal)
            .First();
        return new FacadeOutcome(
            name,
            File: null,
            DiagnosticInfo.Create(
                DiagnosticDescriptors.TemplateFacadeNameMet,
                first.Where,
                name,
                "it would get " + DefinitionFactory.Listed(closings.Select(static closing => "the one for " + OpenName(closing.Type)).OrderBy(static text => text, StringComparer.Ordinal))
                + ", and gets " + (closings.Count == 2 ? "neither" : "none of them"),
                closings.Count == 2 ? "one of them" : "each of them but one",
                first.Line is { } line
                    ? "another name in " + line.Written
                    : NameLine(closings.Count(closing => closing.Type.Name == first.Type.Name) > 1 ? FullName(first.Type) : first.Type.Name, "...") + " in this project"));
    }

    /// <summary>
    /// DDD00075 on the first of this project's classes: it gets a class of the name another module's project it
    /// references has. Its own code names its own, and every project above it sees both.
    /// </summary>
    private static FacadeOutcome MetWithAReference(Closing closing, string moduleName, List<INamedTypeSymbol> others)
        => new(
            closing.Name,
            File: null,
            DiagnosticInfo.Create(
                DiagnosticDescriptors.TemplateFacadeNameMet,
                closing.Where,
                closing.Name,
                "it gets one " + (closing.Module is not null ? "for the module " + closing.Module : "for its classes")
                + ", which its own code names, and sees " + DefinitionFactory.Listed(others.Select(HadBy))
                + ", which every project above it sees beside it",
                others.Count == 1 ? "one of them" : "each of them but one",
                closing.Line is { } line
                    ? "another name in " + line.Written
                    : NameLine(closing.Type.Name, moduleName + closing.Type.Name) + " in this project"));

    /// <summary>
    /// DDD00075 at the project file: the projects this one references have classes of one name, which meet here. No
    /// line of this project is wrong, and the fix is in one of theirs, which the message names: a line to add, or,
    /// where the application named that class already, the line to give another name.
    /// </summary>
    private static FacadeOutcome MetInReferences(string name, List<INamedTypeSymbol> classes, LocationInfo? projectFile)
    {
        var first = classes[0];
        var package = first.BaseType!.OriginalDefinition;
        var assembly = first.ContainingAssembly;
        var fix = LinesByAssembly.GetValue(assembly, LinesOf).FirstOrDefault(line => Names(package, line.Facade) && line.Name == first.Name) is { } given
            ? "another name in " + given.Written
            : first.Name != package.Name
                ? "another name in the line that names the one '" + assembly.Name + "' has, " + NameLine(package.Name, first.Name)
                : NameLine(package.Name, DDDOptions.Default.ResolveModuleName(ModuleBoundary.ModuleOf(assembly), assembly.Name) + package.Name) + " in '" + assembly.Name + "'";
        return new FacadeOutcome(
            name,
            File: null,
            DiagnosticInfo.Create(
                DiagnosticDescriptors.TemplateFacadeNameMet,
                projectFile,
                name,
                "it sees " + DefinitionFactory.Listed(classes.Select(HadBy)),
                classes.Count == 2 ? "one of them" : "each of them but one",
                fix));
    }

    /// <summary>"the one 'Customers.Domain' has for the module Customers": a class the generator wrote into a referenced project.</summary>
    private static string HadBy(INamedTypeSymbol written)
        => ModuleBoundary.ModuleOf(written.ContainingAssembly) is { } module
            ? "the one '" + written.ContainingAssembly.Name + "' has for the module " + module
            : "the one '" + written.ContainingAssembly.Name + "' has";

    /// <summary>The line that names a class, written out: <c>[assembly: TemplateFacadeName("TenancyUseCases", "CustomersTenancyUseCases")]</c>.</summary>
    private static string NameLine(string facade, string name) => "[assembly: TemplateFacadeName(\"" + facade + "\", \"" + name + "\")]";

    /// <summary>The package's class as the application writes it in a typeof: <c>TenancyUseCases&lt;,,,,,,,,&gt;</c>, with its namespace.</summary>
    private static string OpenName(INamedTypeSymbol type) => FullName(type) + "<" + new string(',', type.Arity - 1) + ">";

    /// <summary>The package's class with its namespace and the classes it is nested in, without type parameters: what a line names where two packages' classes share a name.</summary>
    private static string FullName(INamedTypeSymbol type)
        => (type.ContainingType is { } outer ? FullName(outer) : type.ContainingNamespace is { IsGlobalNamespace: false } scope ? scope.ToDisplayString() : null) is { } before
            ? before + "." + type.Name
            : type.Name;

    /// <summary>
    /// The classes the generator wrote into the projects this one references, by name: a class without type
    /// parameters in the global namespace of a referenced assembly that derives from a class its package asks for,
    /// and that this project can see. Two of one name are two modules' classes, which meet here.
    /// </summary>
    private static Dictionary<string, List<INamedTypeSymbol>> WrittenInReferences(Compilation compilation, CancellationToken cancellationToken)
    {
        var written = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var type in WrittenByAssembly.GetValue(assembly, WrittenIn))
            {
                if (!compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
                {
                    continue;
                }

                if (!written.TryGetValue(type.Name, out var classes))
                {
                    written.Add(type.Name, classes = []);
                }

                classes.Add(type);
            }
        }

        foreach (var classes in written.Values)
        {
            classes.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.ContainingAssembly.Name, right.ContainingAssembly.Name));
        }

        return written;
    }

    /// <summary>The classes a referenced assembly was given, read once per assembly: it does not change while the project that references it is edited.</summary>
    private static readonly ConditionalWeakTable<IAssemblySymbol, INamedTypeSymbol[]> WrittenByAssembly = new();

    private static INamedTypeSymbol[] WrittenIn(IAssemblySymbol assembly)
        => assembly.GlobalNamespace.GetTypeMembers()
            .Where(static type => type is { TypeKind: TypeKind.Class, IsAbstract: true, Arity: 0, BaseType: { IsGenericType: true, OriginalDefinition: { ContainingAssembly: { } package } definition } }
                                  && ByAssembly.GetValue(package, FacadeTypesOf).Contains(definition, SymbolEqualityComparer.Default))
            .ToArray();

    // ------------------------------------------------------------------ the names the application gives

    /// <summary>One <c>[assembly: TemplateFacadeName]</c>: the class it names, the name it gives, and where the line is.</summary>
    /// <param name="Facade">The class it names, as the line writes it.</param>
    /// <param name="Name">The name it gives.</param>
    /// <param name="Where">The line, in this project; null for a line of a referenced one.</param>
    /// <param name="In">The referenced project the line is in; null for a line of this project.</param>
    private sealed record GivenName(string Facade, string Name, LocationInfo? Where, string? In = null)
    {
        /// <summary>The line and the project it is in, as a message names it: what to give another name in.</summary>
        public string Written => NameLine(Facade, Name) + (In is null ? " in this project" : " in '" + In + "'");
    }

    /// <summary>This project's <c>[assembly: TemplateFacadeName]</c> lines, in the order the compiler reads them.</summary>
    private static List<GivenName> NamesGivenIn(Compilation compilation, CancellationToken cancellationToken)
        => LinesIn(compilation.Assembly, here: true, cancellationToken);

    /// <summary>A referenced project's lines, read once per assembly: it does not change while the project that references it is edited.</summary>
    private static readonly ConditionalWeakTable<IAssemblySymbol, GivenName[]> LinesByAssembly = new();

    private static GivenName[] LinesOf(IAssemblySymbol assembly) => LinesIn(assembly, here: false, CancellationToken.None).ToArray();

    private static List<GivenName> LinesIn(IAssemblySymbol assembly, bool here, CancellationToken cancellationToken)
    {
        var given = new List<GivenName>();
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is { } attributeClass
                && EntityDeclarations.Is(attributeClass, KnownTypes.TemplateFacadeNameAttribute)
                && attribute.ConstructorArguments.Length == 2
                && attribute.ConstructorArguments[0].Value is string facade
                && attribute.ConstructorArguments[1].Value is string name)
            {
                given.Add(here
                    ? new GivenName(facade.Trim(), name.Trim(), attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is { } syntax ? LocationInfo.From(syntax) : null)
                    : new GivenName(facade.Trim(), name.Trim(), Where: null, In: assembly.Name));
            }
        }

        return given;
    }

    /// <summary>
    /// The lines that name the class in the projects this one takes classes of its templates from, the projects of its
    /// module it references, in the order of their names: a module whose classes are split over two projects names the
    /// class beside the module in either. Of each, the first line that names this class and no other, with a name a
    /// class can have; what that project's other lines change nothing of, DDD00076, it says itself.
    /// </summary>
    private static IEnumerable<GivenName> LinesWhereItTakesFrom(
        INamedTypeSymbol type,
        TemplateRegistrations.Take[] takes,
        List<INamedTypeSymbol> asked,
        Func<TemplateRegistrations.Take, List<DefinitionFactory.TemplateSource>> candidatesFor,
        Compilation compilation)
    {
        var assemblies = new List<IAssemblySymbol>();
        foreach (var take in takes)
        {
            var found = candidatesFor(take);
            if (found.Count == 1
                && found[0].SymbolIn(compilation)?.ContainingAssembly is { } assembly
                && !SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly)
                && !assemblies.Contains(assembly, SymbolEqualityComparer.Default))
            {
                assemblies.Add(assembly);
            }
        }

        foreach (var assembly in assemblies.OrderBy(static assembly => assembly.Name, StringComparer.Ordinal))
        {
            if (LinesByAssembly.GetValue(assembly, LinesOf).FirstOrDefault(line => Names(type, line.Facade) && asked.Count(other => Names(other, line.Facade)) == 1 && IsAName(line.Name)) is { } there)
            {
                yield return there;
            }
        }
    }

    /// <summary>Whether a line names the package's class: by its name without type parameters, or with its namespace.</summary>
    private static bool Names(INamedTypeSymbol type, string facade) => type.Name == facade || FullName(type) == facade;

    /// <summary>Whether a class can have the name: an identifier, and no keyword.</summary>
    private static bool IsAName(string name) => SyntaxFacts.IsValidIdentifier(name) && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;

    /// <summary>
    /// The name each line gives the class it names, by the package's class; DDD00076 for a line that names no class,
    /// or two, a class a line before it names already, or gives a name no class can have.
    /// </summary>
    private static Dictionary<INamedTypeSymbol, GivenName> NamesOf(List<INamedTypeSymbol> asked, List<GivenName> given, List<FacadeOutcome> outcomes)
    {
        var names = new Dictionary<INamedTypeSymbol, GivenName>(SymbolEqualityComparer.Default);
        foreach (var line in given)
        {
            var named = asked.Where(type => Names(type, line.Facade)).ToList();
            if (named.Count == 0)
            {
                outcomes.Add(NamesNothing(line, asked.Count == 0
                    ? "no package this project references asks for a class of that name, and none asks for any"
                    : "no package this project references asks for a class of that name; it can name "
                      + DefinitionFactory.Listed(asked.Select(static type => "'" + type.Name + "'").Distinct().OrderBy(static text => text, StringComparer.Ordinal))));
            }
            else if (named.Count > 1)
            {
                outcomes.Add(NamesNothing(line, "it names the classes of " + DefinitionFactory.Listed(named.Select(static type => "'" + FullName(type) + "'").OrderBy(static text => text, StringComparer.Ordinal))
                                                + "; name the one meant with its namespace"));
            }
            else if (!IsAName(line.Name))
            {
                outcomes.Add(NamesNothing(line, "'" + line.Name + "' is no name a class can have"));
            }
            else if (names.TryGetValue(named[0], out var before))
            {
                outcomes.Add(NamesNothing(line, "a line before it names " + named[0].Name + " '" + before.Name + "' already, and that one stands; keep one"));
            }
            else
            {
                names.Add(named[0], line);
            }
        }

        return names;
    }

    /// <summary>DDD00076 at a line of this project that changes nothing.</summary>
    private static FacadeOutcome NamesNothing(GivenName line, string why) => NamesNothing(line, line.Where, why);

    /// <summary>
    /// DDD00076 for a line that changes nothing, said where given: at the line, or, for a line of a project of the module
    /// this one takes classes from, on this project's first class or its own line, with the project the line is in.
    /// </summary>
    private static FacadeOutcome NamesNothing(GivenName line, LocationInfo? where, string why)
        => new(
            line.Name,
            File: null,
            DiagnosticInfo.Create(
                DiagnosticDescriptors.TemplateFacadeNameNamesNothing,
                where,
                line.Facade,
                line.Name,
                (line.In is null ? string.Empty : "it is in '" + line.In + "', a project of the module this one takes classes from, and names the class written here; ") + why));

    // ------------------------------------------------------------------ finding the classes

    /// <summary>
    /// The classes a package asks for, of the types it declares itself: the project's own, read on every edit as it
    /// changes, and every referenced assembly's, read once per assembly. Each a generic class that is neither static
    /// nor sealed, with a constructor a class of another assembly can call, not nested in a generic type. An
    /// attribute one assembly declares for another's type asks nothing.
    /// </summary>
    private static List<INamedTypeSymbol> FacadeTypesIn(Compilation compilation, CancellationToken cancellationToken)
    {
        var found = new List<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        Add(compilation.Assembly, FacadeTypesOf(compilation.Assembly));
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Add(assembly, ByAssembly.GetValue(assembly, FacadeTypesOf));
        }

        return found;

        void Add(IAssemblySymbol assembly, INamedTypeSymbol[] types)
        {
            foreach (var type in types)
            {
                if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, assembly)
                    && seen.Add(type)
                    && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
                {
                    found.Add(type);
                }
            }
        }
    }

    /// <summary>
    /// The classes a referenced assembly asks for, read once per assembly, as its registrations are: a referenced
    /// assembly does not change while the project that references it is being edited.
    /// </summary>
    private static readonly ConditionalWeakTable<IAssemblySymbol, INamedTypeSymbol[]> ByAssembly = new();

    private static INamedTypeSymbol[] FacadeTypesOf(IAssemblySymbol assembly)
    {
        var asked = new List<INamedTypeSymbol>();
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass
                || !EntityDeclarations.Is(attributeClass, KnownTypes.TemplateFacadeAttribute)
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0] is not { Kind: TypedConstantKind.Type, Value: INamedTypeSymbol { TypeKind: TypeKind.Class, IsGenericType: true, IsStatic: false, IsSealed: false } type }
                || InAGenericType(type)
                || !CanBeDerivedFrom(type.OriginalDefinition)
                || asked.Contains(type.OriginalDefinition, SymbolEqualityComparer.Default))
            {
                continue;
            }

            asked.Add(type.OriginalDefinition);
        }

        return asked.ToArray();
    }

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
}

/// <summary>What one class a package asks for comes to in a project: the file the generator writes, or what is said instead.</summary>
/// <param name="Name">What the class is called, <c>TenancyUseCases</c>.</param>
/// <param name="File">The file, or null when the class is not written.</param>
/// <param name="Diagnostic">
/// What is said: DDD00065 for why the class is not written, DDD00075 for two classes of one name, DDD00076 for a line
/// that names nothing; null when the class is written, or when what keeps it out is said elsewhere.
/// </param>
internal sealed record FacadeOutcome(string Name, FacadeFile? File, DiagnosticInfo? Diagnostic);

/// <summary>One class a project gets: the file the generator writes.</summary>
/// <param name="HintName">The file's name, <c>TenancyUseCases.TemplateFacade.g.cs</c>.</param>
/// <param name="Name">What the class is called, <c>TenancyUseCases</c>.</param>
/// <param name="IsPublic">Whether it is public; otherwise internal, as far as an internal class it is closed over reaches.</param>
/// <param name="BaseType">The package's class closed over the application's, fully qualified.</param>
/// <param name="Summary">What it is closed over, for its documentation, escaped for XML.</param>
/// <param name="Naming">Where its name comes from, and how to give it another, for its documentation, escaped for XML.</param>
internal sealed record FacadeFile(string HintName, string Name, bool IsPublic, string BaseType, string Summary, string Naming);
