using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// Closes a package's <c>[TemplateRegistration]</c> methods over the classes a project declares with the
/// package's templates, so the project calls <c>modelBuilder.AddTenancy()</c> rather than naming its nine
/// classes and ids in every call.
/// <para>
/// Each <c>[TemplateType]</c> type parameter is filled exactly as <see cref="DefinitionFactory.ResolveTemplates"/>
/// fills a <c>[TemplateArgument]</c>: from the one class declared with the template, in this project or, when
/// this project declares none, in the projects it references (<see cref="DefinitionFactory.ReferencedSources"/>),
/// taking its id or the class itself. None is DDD00049 and several is DDD00045. A class taken that does not
/// meet the method's constraints is DDD00050, found with the same <see cref="DefinitionFactory.Satisfies"/>
/// that DDD00048 uses. A class that is itself refused leaves the method without a wrapper and without a
/// second report, because its own diagnostic already says why.
/// </para>
/// <para>
/// A registration takes more than the id of a template with several type arguments: <c>Argument = 1</c> hands
/// over the second type argument the class was declared with, and so on. A method that takes the class has to
/// take them all, since the class is its parent closed over exactly those types and over no type parameter
/// the wrapper could leave open. A later argument need not be an entity id, and is held to the method's
/// constraints the way DDD00053 holds it to the parent's. A later argument that names one of the application's
/// entities or aggregate roots hands over that class's id as well, with <c>IdOfArgument = true</c>, so a
/// template that names the aggregate its class belongs to closes a registration over both. Such a class of
/// this project is held to the method's constraints by what the generator will make of it, as a class the
/// method takes is, so a child entity where an aggregate root is asked for is DDD00050 on the class.
/// </para>
/// <para>
/// A method that says what its wrapper is called, <c>[TemplateRegistration(Name = "Add{TTopic}Comments")]</c>,
/// has it called that: the name of the type that fills the type parameter in the braces, as its declaration
/// names it. So a wrapper is named after the thing it is for, and is called the same whether the project has
/// one class of the template or several.
/// </para>
/// <para>
/// Several classes are not always a mistake. A template whose marker says <c>AllowSeveral</c> is declared
/// once per thing the application has, and a method that takes its types from it gets one wrapper per class,
/// named after the class: <c>AddCommentsForSongComment</c> and <c>AddCommentsForVideoComment</c>. Nothing is
/// picked without a word, which is what DDD00045 guards against, because each wrapper says which class it is
/// closed over. With one class the wrapper keeps the method's name. Two templates of one method that each
/// have several classes stay DDD00045: there is no telling which class of the one goes with which of the
/// other. So do two classes of one name, whose wrappers no call could tell apart, and, for a method that
/// names its wrapper, two classes whose wrappers come to one name.
/// </para>
/// <para>
/// A project that declares no class with any of a method's templates is not meant to get it and hears
/// nothing about it: the package's own projects, and the modules of the application that only refer to the
/// classes' ids. A project that declares some of them and sees the method is meant to get it, so a template
/// it lacks is reported there; a package keeps its registrations apart from its templates, so a domain
/// module that declares some of the classes and does not reference the registrations is never asked.
/// </para>
/// <para>
/// A module can be split into projects by layer: its domain project declares the classes and has no Entity
/// Framework, and its infrastructure project holds the context and references the registrations. That project
/// declares no class, and gets the registrations anyway, built from the classes the module's other projects
/// declare: the projects it references with the same <c>[assembly: Module]</c>, and only those. A project of
/// another module, or of none, still gets nothing and hears nothing from here; one of none that carries the name of
/// the project whose classes it would take, in <c>DDD_Module</c>, hears DDD00064 from the toolkit's module generator
/// (<see cref="WrittenForNobody"/>). What a project of the module is told, DDD00049,
/// DDD00045 or DDD00050, is reported on its <c>[assembly: Module]</c> attribute, or at its project file when no file
/// of it that somebody edits declares the module: the build declared it from <c>DDD_Module</c>, or an
/// <c>AssemblyAttribute</c> item wrote it into <c>obj/</c>.
/// </para>
/// <para>
/// Only the lowest such project gets them. A project of the module above it, such as the API project that
/// composes the module and so references the infrastructure project, declares no class either and sees the same
/// registrations through that reference; it gets nothing, because the wrappers are already in the project below
/// it, and wrappers of its own would make it name the registrations' package, Entity Framework included, in code
/// nobody wrote there. It calls the lower project's own public registration instead.
/// </para>
/// </summary>
internal static class TemplateRegistrations
{
    /// <summary>What a wrapper for one class of a template that allows several has between the method's name and the class's.</summary>
    private const string PerClassInfix = "For";

    /// <summary>The files to write, one per registration method name of a declaring type, with what they report.</summary>
    /// <param name="declared">The classes this project declares with a template.</param>
    /// <param name="compilation">The project.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <param name="only">
    /// When given, the methods to close, and no others: for a package's own generator that writes more for the
    /// classes one of its registrations is closed over, and so asks which those are, exactly as the wrappers
    /// of that registration were written. It reads what it is answered and leaves the reporting to the
    /// generator that writes the wrappers.
    /// </param>
    /// <param name="projectFile">
    /// Where a project of a module hears about the classes of the module's other projects when no
    /// <c>[assembly: Module]</c> in a file of it says which module it is: the build declared it from
    /// <c>DDD_Module</c>, or an <c>AssemblyAttribute</c> item did, in a file under <c>obj/</c>.
    /// </param>
    public static ImmutableArray<RegistrationFile> Resolve(
        ImmutableArray<EntityDefinition> declared,
        Compilation compilation,
        CancellationToken cancellationToken,
        Func<IMethodSymbol, bool>? only = null,
        LocationInfo? projectFile = null)
    {
        if (declared.IsDefaultOrEmpty)
        {
            // The common case, and the cheap one: a project that declares no template class and no module needs
            // nothing more. One that declares a module may be the project of it that holds the context, next to
            // the domain project that declares the classes.
            if (ModuleBoundary.ModuleOf(compilation.Assembly) is not { } module)
            {
                return ImmutableArray<RegistrationFile>.Empty;
            }

            LocationInfo? moduleAttribute = null;
            LocationInfo? ModuleAttribute() => moduleAttribute ??= ModuleBoundary.WhereTheModuleIsDeclared(compilation, projectFile, cancellationToken);
            return Collapsed(Files(compilation, method => ResolveModuleMethod(method, module, ModuleAttribute, compilation, cancellationToken), only, cancellationToken));
        }

        // Whether a class can be generated is only known once every template class is resolved, and only a
        // class a method takes as itself needs to know; resolved once, the first time that happens.
        ImmutableArray<EntityDefinition>? resolved = null;
        ImmutableArray<EntityDefinition> Resolved() => resolved ??= DefinitionFactory.ResolveTemplates(declared, compilation, cancellationToken);

        // The templates a class of this project takes a type from for its own parent. One nobody declares is
        // DDD00044 on that class, with the fix that declares it: the registrations that need it as well stand
        // back, rather than say the same once more for each.
        var takenByAParent = new HashSet<string>(
            declared.Where(static definition => definition.CanGenerate && definition.Template is not null)
                .SelectMany(static definition => definition.Template!.Bindings)
                .Select(static binding => binding.SourceKey),
            StringComparer.Ordinal);

        return Collapsed(Files(compilation, method => ResolveMethod(method, declared, Resolved, takenByAParent, compilation, cancellationToken), only, cancellationToken));
    }

    /// <summary>
    /// Whether this project can call a registration that takes a type from the template whose attribute has
    /// <paramref name="templateMetadataName"/>: one of its classes declared with that template is closed into a
    /// registration here, and what keeps it from being closed, two classes whose registrations come to one
    /// name (DDD00045) or a class that does not meet the method's constraints (DDD00050), is reported here. A
    /// package's own generator that would say the same of those classes stands back where this is true.
    /// </summary>
    /// <param name="compilation">The project.</param>
    /// <param name="templateMetadataName">The template attribute's metadata name, <c>Namespace.MemberAttribute`4</c>.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    public static bool Registers(Compilation compilation, string templateMetadataName, CancellationToken cancellationToken)
        => MethodsIn(compilation, cancellationToken).Any(method => TakesOf(method) is { } takes && takes.Any(take => take?.MetadataName == templateMetadataName));

    /// <summary>
    /// The registrations this project can call that take a class <paramref name="referenced"/> declares with a
    /// template, that <paramref name="referenced"/> cannot call itself, and that take a class of none of their
    /// templates from this project: by name, each once, in order. These are the ones written for those classes
    /// nowhere when the two are projects of no module. A project gets a registration for its own classes where it
    /// sees the registration, and takes the classes it lacks from the projects it references; a project that
    /// declares none of them takes them only from the projects of its own module. Empty when
    /// <paramref name="referenced"/> declares no class with a template, which is every project built before
    /// templates existed.
    /// </summary>
    /// <param name="compilation">The project.</param>
    /// <param name="referenced">A project it references.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    public static EquatableArray<string> WrittenForNobody(Compilation compilation, IAssemblySymbol referenced, CancellationToken cancellationToken)
    {
        if (!DefinitionFactory.DeclaresTemplateClasses(referenced, cancellationToken))
        {
            return EquatableArray<string>.Empty;
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var method in MethodsIn(compilation, cancellationToken))
        {
            var registrations = method.ContainingAssembly;
            if (TakesOf(method) is not { } takes
                || !takes.Any(take => take is not null && DefinitionFactory.DeclaresClassesWith(referenced, take.Key, cancellationToken))
                || takes.Any(take => take is not null && DefinitionFactory.DeclaresClassesWith(compilation.Assembly, take.Key, cancellationToken))
                || SymbolEqualityComparer.Default.Equals(registrations, referenced)
                || referenced.Modules.Any(module => module.ReferencedAssemblySymbols.Any(reference => reference.Identity.Name == registrations.Identity.Name)))
            {
                continue;
            }

            names.Add(method.Name);
        }

        return names.ToEquatableArray();
    }

    /// <summary>
    /// What the files report, said once for the project, where the methods of several files would each say the
    /// same: one missing template is DDD00049 once, naming every registration that needs it; the same classes
    /// refused for the same reason, DDD00045 or DDD00050, are reported once, by the first method that refused
    /// them. A cause is reported once, and the methods it stops stand back behind it.
    /// </summary>
    private static ImmutableArray<RegistrationFile> Collapsed(ImmutableArray<RegistrationFile> files)
    {
        if (files.Length < 2 && files.All(static file => file.Diagnostics.Count < 2))
        {
            return files;
        }

        // Every registration a missing template stops, by the template, in the order the files have them.
        var missing = new Dictionary<(LocationInfo? Where, string Template), List<string>>();
        foreach (var diagnostic in files.SelectMany(static file => file.Diagnostics))
        {
            if (diagnostic.Descriptor.Id == DiagnosticDescriptors.TemplateRegistrationSourceMissing.Id)
            {
                var key = (diagnostic.Location, diagnostic.MessageArguments[1]);
                if (!missing.TryGetValue(key, out var stopped))
                {
                    missing.Add(key, stopped = []);
                }

                if (!stopped.Contains(diagnostic.MessageArguments[0]))
                {
                    stopped.Add(diagnostic.MessageArguments[0]);
                }
            }
        }

        var said = new HashSet<string>(StringComparer.Ordinal);
        var collapsed = ImmutableArray.CreateBuilder<RegistrationFile>(files.Length);
        foreach (var file in files)
        {
            var kept = new List<DiagnosticInfo>();
            foreach (var diagnostic in file.Diagnostics)
            {
                if (!said.Add(CauseOf(diagnostic)))
                {
                    continue;
                }

                kept.Add(diagnostic.Descriptor.Id == DiagnosticDescriptors.TemplateRegistrationSourceMissing.Id
                    ? DiagnosticInfo.Create(
                          diagnostic.Descriptor,
                          diagnostic.Location,
                          DefinitionFactory.Listed(missing[(diagnostic.Location, diagnostic.MessageArguments[1])]),
                          diagnostic.MessageArguments[1]) with
                      {
                          Properties = diagnostic.Properties,
                      }
                    : diagnostic);
            }

            collapsed.Add(kept.Count == file.Diagnostics.Count && !kept.Any(static diagnostic => diagnostic.Descriptor.Id == DiagnosticDescriptors.TemplateRegistrationSourceMissing.Id)
                ? file
                : file with { Diagnostics = kept.ToEquatableArray() });
        }

        return collapsed.MoveToImmutable();
    }

    /// <summary>
    /// What a diagnostic is about, without the method that reported it: two methods that refuse the same classes
    /// for the same reason, or that miss the same template, report one cause.
    /// </summary>
    private static string CauseOf(DiagnosticInfo diagnostic)
    {
        var where = diagnostic.Location is { } location ? location.FilePath + "@" + location.TextSpan.Start : string.Empty;
        var arguments = diagnostic.MessageArguments;
        var about = diagnostic.Descriptor.Id switch
        {
            // The template; the template and the classes; the class taken, the type parameter and what it lacks.
            "DDD00049" => arguments[1],
            "DDD00045" => arguments[3] + "|" + arguments[4],
            "DDD00050" => arguments[0] + "|" + arguments[2] + "|" + arguments[3],
            _ => string.Join("|", arguments),
        };

        return diagnostic.Descriptor.Id + "|" + where + "|" + about;
    }

    /// <summary>
    /// One file per method name of each declaring type this project can call, with the wrappers <paramref name="close"/>
    /// makes of its overloads and what they report. A name none of whose overloads concern the project is left out.
    /// </summary>
    private static ImmutableArray<RegistrationFile> Files(
        Compilation compilation,
        Func<IMethodSymbol, MethodClosing?> close,
        Func<IMethodSymbol, bool>? only,
        CancellationToken cancellationToken)
    {
        var methods = MethodsIn(compilation, cancellationToken);
        if (only is not null)
        {
            methods.RemoveAll(method => !only(method));
        }

        if (methods.Count == 0)
        {
            return ImmutableArray<RegistrationFile>.Empty;
        }

        var files = ImmutableArray.CreateBuilder<RegistrationFile>();
        foreach (var group in methods.GroupBy(static method => (Type: method.ContainingType, method.Name), new TypeAndNameComparer()))
        {
            var wrappers = new List<RegistrationWrapper>();
            var diagnostics = new List<DiagnosticInfo>();
            var relevant = false;

            foreach (var method in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (close(method) is not { } outcome)
                {
                    continue;
                }

                relevant = true;
                diagnostics.AddRange(outcome.Diagnostics);
                wrappers.AddRange(outcome.Wrappers);
            }

            if (!relevant)
            {
                continue;
            }

            var type = group.Key.Type;
            var qualified = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            files.Add(new RegistrationFile(
                HintName: TypeDeclarationInfo.HintNameFor(type.Name, qualified, "." + group.Key.Name + ".Registration"),
                Namespace: type.ContainingNamespace is { IsGlobalNamespace: false } scope ? scope.ToDisplayString() : string.Empty,
                ClassName: "Generated" + type.Name,
                DeclaringType: type.Name,
                Wrappers: wrappers.ToEquatableArray(),
                Diagnostics: diagnostics.Distinct().ToEquatableArray()));
        }

        return files.ToImmutable();
    }

    // ------------------------------------------------------------------ finding the methods

    /// <summary>
    /// The registration methods this project can call: those of the types named by
    /// <c>[assembly: TemplateRegistrations(typeof(X))]</c>, in this project and in every reference.
    /// </summary>
    private static List<IMethodSymbol> MethodsIn(Compilation compilation, CancellationToken cancellationToken)
    {
        var found = new List<IMethodSymbol>();

        // A type named twice, by two attributes or two assemblies, still has each method once.
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        Add(RegistrationMethodsOf(compilation.Assembly));
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Add(MethodsByAssembly.GetValue(assembly, RegistrationMethodsOf));
        }

        return found;

        void Add(IMethodSymbol[] methods)
        {
            foreach (var method in methods)
            {
                if (seen.Add(method) && compilation.IsSymbolAccessibleWithin(method, compilation.Assembly))
                {
                    found.Add(method);
                }
            }
        }
    }

    /// <summary>
    /// The registration methods of a referenced assembly. Reading an assembly's attributes decodes its
    /// metadata, and a referenced assembly does not change while the project that references it is being
    /// edited: the compiler keeps the same symbol for it across compilations, so each is read once.
    /// </summary>
    private static readonly ConditionalWeakTable<IAssemblySymbol, IMethodSymbol[]> MethodsByAssembly = new();

    private static IMethodSymbol[] RegistrationMethodsOf(IAssemblySymbol assembly)
    {
        var methods = new List<IMethodSymbol>();
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass
                || !EntityDeclarations.Is(attributeClass, KnownTypes.TemplateRegistrationsAttribute)
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0] is not { Kind: TypedConstantKind.Type, Value: INamedTypeSymbol { TypeKind: not TypeKind.Error } declaringType })
            {
                continue;
            }

            foreach (var member in declaringType.GetMembers())
            {
                if (member is IMethodSymbol { IsStatic: true, IsGenericMethod: true, ReturnsByRef: false, ReturnsByRefReadonly: false } method
                    && DefinitionFactory.HasAttribute(method, KnownTypes.TemplateRegistrationAttribute))
                {
                    methods.Add(method);
                }
            }
        }

        return methods.ToArray();
    }

    // ------------------------------------------------------------------ closing one method

    /// <summary>What a type parameter's <c>[TemplateType]</c> says.</summary>
    /// <param name="Key">The template attribute's open definition, fully qualified.</param>
    /// <param name="AttributeName">The attribute as the author writes it, for diagnostics.</param>
    /// <param name="MetadataName">The attribute's metadata name, for finding its classes in the references.</param>
    /// <param name="TakeType">True when the class itself is taken.</param>
    /// <param name="Argument">Which type argument of the attribute is taken when the class is not: 0 is the id.</param>
    /// <param name="IdOfArgument">True when the id of the class a later type argument names is taken, rather than that class.</param>
    /// <param name="AllowSeveral">True when the template's marker says an application may declare several classes with it.</param>
    private sealed record Take(string Key, string AttributeName, string MetadataName, bool TakeType, int Argument, bool IdOfArgument, bool AllowSeveral);

    /// <summary>
    /// What a method's <c>[TemplateRegistration(Name = ...)]</c> says its wrapper is called: text, and between
    /// two pieces of it the name of the type that fills a type parameter.
    /// </summary>
    /// <param name="Text">The pieces of text, one more than there are type parameters named.</param>
    /// <param name="Positions">The type parameters named, by position, in the order the name has them.</param>
    private sealed record WrapperName(string[] Text, int[] Positions);

    /// <summary>
    /// What one method comes to in this project: its wrapper, one per class for a template that allows several,
    /// and the diagnostics that say why one is missing.
    /// </summary>
    private sealed record MethodClosing(List<RegistrationWrapper> Wrappers, List<DiagnosticInfo> Diagnostics);

    /// <summary>A class a type parameter can be filled from, and where what is wrong with it is reported.</summary>
    private readonly record struct Candidate(DefinitionFactory.TemplateSource Source, LocationInfo? Location);

    /// <summary>
    /// The method closed over this project's classes, or null when this project declares no class with any
    /// of its templates, or when the method is not one the generator can close: a <c>[TemplateType]</c> that
    /// names no template attribute is the package's mistake, and shows in the package's own tests.
    /// </summary>
    private static MethodClosing? ResolveMethod(
        IMethodSymbol method,
        ImmutableArray<EntityDefinition> declared,
        Func<ImmutableArray<EntityDefinition>> resolved,
        HashSet<string> takenByAParent,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var parameters = method.TypeParameters;
        if (TakesOf(method) is not { } takes || !NamingOf(method, takes, out var naming))
        {
            return null;
        }

        var keys = new HashSet<string>(takes.OfType<Take>().Select(static take => take.Key), StringComparer.Ordinal);
        var anchorIndex = -1;
        for (var index = 0; index < declared.Length && anchorIndex < 0; index++)
        {
            if (declared[index].TemplateKey is { } key && keys.Contains(key))
            {
                anchorIndex = index;
            }
        }

        if (anchorIndex < 0)
        {
            return null;
        }

        var anchor = declared[anchorIndex];
        var candidates = new List<Candidate>?[parameters.Length];
        for (var position = 0; position < parameters.Length; position++)
        {
            if (takes[position] is not { } take)
            {
                continue;
            }

            var local = Enumerable.Range(0, declared.Length).Where(index => declared[index].TemplateKey == take.Key).ToList();
            candidates[position] = local.Count > 0
                ? local.Select(index => new Candidate(SourceOf(declared[index], take.TakeType ? resolved()[index].CanGenerate : true), declared[index].Type.Location)).ToList()
                : DefinitionFactory.ReferencedSources(compilation, take.MetadataName, cancellationToken).Select(source => new Candidate(source, anchor.Type.Location)).ToList();
        }

        return Closing(
            method,
            takes,
            naming,
            candidates,
            anchor.Type.Location,
            take => MissingTemplateClass.Properties(compilation, take.MetadataName, anchor.Type.Name, anchor.Template?.AttributeName),
            take => takenByAParent.Contains(take.Key),
            compilation,
            cancellationToken);
    }

    /// <summary>
    /// The method closed over the classes the other projects of this project's module declare, or null when none
    /// of them declares a class with any of its templates. This is the project of a module that holds what the
    /// registration is for, such as the context, next to the domain project that declares the classes and needs
    /// no Entity Framework to do so.
    /// <para>
    /// Only the assemblies that declare the same <c>[Module]</c> are looked in, for the anchor and for every
    /// template: a class of another module is never taken, and neither is one of a package, which declares no
    /// module. So a project of another module gets nothing and hears nothing, and a project that references the
    /// classes of two modules is never told they are two. What it reports it reports on the project's
    /// <c>[assembly: Module]</c> attribute, or at its project file where none is in a file somebody edits, since the
    /// classes are not in this project; DDD00049 then carries nothing for the code fix, which would declare the
    /// class in the wrong project.
    /// </para>
    /// </summary>
    private static MethodClosing? ResolveModuleMethod(
        IMethodSymbol method,
        string module,
        Func<LocationInfo?> moduleAttribute,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var parameters = method.TypeParameters;
        if (TakesOf(method) is not { } takes || !NamingOf(method, takes, out var naming))
        {
            return null;
        }

        var sources = new List<DefinitionFactory.TemplateSource>?[parameters.Length];
        var anchored = false;
        for (var position = 0; position < parameters.Length; position++)
        {
            if (takes[position] is { } take)
            {
                sources[position] = DefinitionFactory.ReferencedSources(compilation, take.MetadataName, cancellationToken, module);
                anchored |= sources[position]!.Count > 0;
            }
        }

        if (!anchored || HeldBelow(method, sources, module, compilation, cancellationToken))
        {
            return null;
        }

        var at = moduleAttribute();
        var candidates = new List<Candidate>?[parameters.Length];
        for (var position = 0; position < parameters.Length; position++)
        {
            candidates[position] = sources[position]?.Select(source => new Candidate(source, at)).ToList();
        }

        return Closing(method, takes, naming, candidates, at, static _ => EquatableArray<string>.Empty, static _ => false, compilation, cancellationToken);
    }

    /// <summary>
    /// Whether a project of this module that this one references already holds the wrappers of
    /// <paramref name="method"/>: one that declares no template class itself, sees the method, and references a
    /// project of the module that declares one of the classes the method takes. That is the project the rule
    /// above wrote them into, such as the infrastructure project below an API project that composes the module.
    /// <para>
    /// A lower project that declares classes of its own does not count, whatever it references: what it gets it
    /// gets for its own classes, by the other rule, so a context project above a domain project that happens to
    /// see the registrations still gets the wrappers it calls.
    /// </para>
    /// </summary>
    private static bool HeldBelow(
        IMethodSymbol method,
        List<DefinitionFactory.TemplateSource>?[] sources,
        string module,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var registrations = method.ContainingAssembly.Identity.Name;
        var declaring = new HashSet<string>(StringComparer.Ordinal);
        foreach (var found in sources)
        {
            foreach (var source in found ?? Enumerable.Empty<DefinitionFactory.TemplateSource>())
            {
                if (source.SymbolIn(compilation)?.ContainingAssembly is { } assembly)
                {
                    declaring.Add(assembly.Identity.Name);
                }
            }
        }

        foreach (var lower in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(ModuleBoundary.ModuleOf(lower), module, StringComparison.Ordinal)
                || DefinitionFactory.DeclaresTemplateClasses(lower, cancellationToken))
            {
                continue;
            }

            var seesTheMethod = false;
            var seesAClass = false;
            foreach (var reference in lower.Modules.SelectMany(static part => part.ReferencedAssemblySymbols))
            {
                seesTheMethod |= reference.Identity.Name == registrations;
                seesAClass |= declaring.Contains(reference.Identity.Name);
            }

            if (seesTheMethod && seesAClass)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What <c>[TemplateType]</c> says of each type parameter of a method, or null when one of them names no
    /// template attribute, a type argument the attribute does not have, or one together with the class itself:
    /// that is the package's mistake, and shows in the package's own tests. The id of an argument is asked of
    /// a later argument only: the first is the id already, so there it says nothing.
    /// </summary>
    private static Take?[]? TakesOf(IMethodSymbol method)
    {
        var parameters = method.TypeParameters;
        var takes = new Take?[parameters.Length];
        for (var position = 0; position < parameters.Length; position++)
        {
            foreach (var attribute in parameters[position].GetAttributes())
            {
                if (attribute.AttributeClass is not { } attributeClass || !EntityDeclarations.Is(attributeClass, KnownTypes.TemplateTypeAttribute))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length != 1
                    || attribute.ConstructorArguments[0] is not { Kind: TypedConstantKind.Type, Value: INamedTypeSymbol { TypeKind: not TypeKind.Error } template }
                    || EntityDeclarations.TemplateOf(template) is not { } marker)
                {
                    return null;
                }

                // TemplateArgumentKind.Type is 1; an enum argument arrives as its underlying value.
                var takeType = attribute.NamedArguments.Any(static named => named.Key == "Take" && named.Value.Value is 1);
                var argument = 0;
                var idOfArgument = false;
                foreach (var named in attribute.NamedArguments)
                {
                    if (named.Key == "Argument" && named.Value.Value is int chosen)
                    {
                        argument = chosen;
                    }
                    else if (named.Key == "IdOfArgument" && named.Value.Value is bool asked)
                    {
                        idOfArgument = asked;
                    }
                }

                if (argument != 0 && (takeType || argument < 0 || argument >= template.OriginalDefinition.TypeParameters.Length))
                {
                    return null;
                }

                takes[position] = new Take(
                    template.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    DefinitionFactory.AttributeNameOf(template),
                    EntityDeclarations.MetadataNameOf(template),
                    takeType,
                    argument,
                    idOfArgument && argument != 0,
                    marker.AllowSeveral);
            }
        }

        return takes;
    }

    /// <summary>
    /// What <c>[TemplateRegistration(Name = ...)]</c> says the wrapper of a method is called, or null when it
    /// says nothing and the wrapper has the method's name. False when the name is one no wrapper can have:
    /// a brace that is not closed, braces around something that is not a type parameter of the method closed
    /// by a <c>[TemplateType]</c>, or text that could not be part of a name. That is the package's mistake,
    /// and the method is passed over, as for a <c>[TemplateType]</c> that names no template.
    /// </summary>
    private static bool NamingOf(IMethodSymbol method, Take?[] takes, out WrapperName? naming)
    {
        naming = null;
        string? pattern = null;
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass || !EntityDeclarations.Is(attributeClass, KnownTypes.TemplateRegistrationAttribute))
            {
                continue;
            }

            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "Name" && named.Value.Value is string said)
                {
                    pattern = said;
                }
            }
        }

        if (pattern is null)
        {
            return true;
        }

        var text = new List<string>();
        var positions = new List<int>();
        var index = 0;
        while (true)
        {
            var open = pattern.IndexOf('{', index);
            if (open < 0)
            {
                text.Add(pattern.Substring(index));
                break;
            }

            var close = pattern.IndexOf('}', open + 1);
            if (close < 0)
            {
                return false;
            }

            var name = pattern.Substring(open + 1, close - open - 1);
            var position = -1;
            for (var candidate = 0; candidate < method.TypeParameters.Length && position < 0; candidate++)
            {
                if (method.TypeParameters[candidate].Name == name && takes[candidate] is not null)
                {
                    position = candidate;
                }
            }

            if (position < 0)
            {
                return false;
            }

            text.Add(pattern.Substring(index, open - index));
            positions.Add(position);
            index = close + 1;
        }

        // With a name in the place of every type parameter it has to be a name: the text between is the package's own.
        if (!SyntaxFacts.IsValidIdentifier(string.Join("X", text)))
        {
            return false;
        }

        naming = new WrapperName(text.ToArray(), positions.ToArray());
        return true;
    }

    /// <summary>
    /// The wrappers of a method, or the diagnostics that say why there is none, from the classes found for each
    /// of its <c>[TemplateType]</c>s.
    /// <para>
    /// A template nobody declared a class with is DDD00049 and one with several is DDD00045, both reported at
    /// <paramref name="where"/>, and either leaves the method without a wrapper. Several classes of the one
    /// template that allows them are not a mistake (<see cref="RepeatedTemplate"/>): the method is then closed
    /// once per class, and what is wrong with one class leaves the others their wrapper. Beside a template
    /// that is reported, the one that allows several gets no wrapper either, and no report of its own.
    /// </para>
    /// <para>
    /// A method that names its wrapper names each class's wrapper after what that class fills the name with.
    /// Classes whose wrappers come to one name are DDD00045 too, and get none: a call could not say which of
    /// them it means. The others keep theirs.
    /// </para>
    /// </summary>
    /// <param name="method">The package's method.</param>
    /// <param name="takes">What each of its type parameters takes; null for one that stays open.</param>
    /// <param name="naming">What the method says its wrapper is called, or null when it has the method's name.</param>
    /// <param name="candidates">The classes found for each type parameter that takes one.</param>
    /// <param name="where">Where a missing or ambiguous template is reported.</param>
    /// <param name="missingProperties">What the code fix for DDD00049 needs to declare the class of a template.</param>
    /// <param name="saidElsewhere">
    /// Whether a missing template is reported already, as DDD00044 on a class whose parent takes a type from it:
    /// the method then has no wrapper, and says nothing more.
    /// </param>
    /// <param name="compilation">The project.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    private static MethodClosing Closing(
        IMethodSymbol method,
        Take?[] takes,
        WrapperName? naming,
        List<Candidate>?[] candidates,
        LocationInfo? where,
        Func<Take, EquatableArray<string>> missingProperties,
        Func<Take, bool> saidElsewhere,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var parameters = method.TypeParameters;
        var display = method.ContainingType.Name + "." + method.Name;
        var diagnostics = new List<DiagnosticInfo>();
        var wrappers = new List<RegistrationWrapper>();
        var repeated = RepeatedTemplate(takes, candidates, namedAfterItsClass: naming is null, out var refused);
        var stopped = false;

        for (var position = 0; position < parameters.Length; position++)
        {
            if (takes[position] is not { } take || candidates[position] is not { } found)
            {
                continue;
            }

            if (found.Count == 0)
            {
                stopped = true;
                if (!saidElsewhere(take))
                {
                    diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TemplateRegistrationSourceMissing, where, "'" + display + "'", take.AttributeName) with
                    {
                        Properties = missingProperties(take),
                    });
                }
            }
            else if (found.Count > 1 && refused.Contains(take.Key))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.TemplateArgumentSourceAmbiguous,
                    where,
                    display,
                    "TemplateRegistration",
                    "'" + parameters[position].Name + "'",
                    take.AttributeName,
                    DefinitionFactory.Listed(found.Select(static candidate => "'" + candidate.Source.Name + "'")),
                    AdviceFor(take, found, namedAfterItsClass: naming is null)));
            }
        }

        if (stopped || diagnostics.Count > 0)
        {
            return new MethodClosing(wrappers, diagnostics);
        }

        IReadOnlyList<DefinitionFactory.TemplateSource?> classes = repeated is { } several ? several.Classes : SingleClosing;
        var closed = new List<(DefinitionFactory.TemplateSource? Class, string Name, RegistrationWrapper Wrapper, ITypeSymbol? NamedAfter)>();
        foreach (var each in classes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ClosedOver(method, takes, naming, candidates, repeated?.Key, each, diagnostics, compilation, cancellationToken) is { } wrapper)
            {
                closed.Add((each, wrapper.Name, wrapper.Wrapper, wrapper.NamedAfter));
            }
        }

        if (naming is not null && repeated is { } perClass)
        {
            // What the name is taken from is the application's to write, so two classes can come to one name:
            // two classes that name the same thing, or things of one name in two namespaces.
            var position = Array.FindIndex(takes, take => take?.Key == perClass.Key);
            foreach (var same in closed.GroupBy(static each => each.Name, StringComparer.Ordinal).Where(static named => named.Count() > 1).ToList())
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.TemplateArgumentSourceAmbiguous,
                    ToFix([.. same.Select(static each => (each.Class!, each.NamedAfter))], candidates[position]!, compilation) ?? where,
                    display,
                    "TemplateRegistration",
                    "'" + parameters[position].Name + "'",
                    takes[position]!.AttributeName,
                    DefinitionFactory.Listed(same.Select(static each => "'" + each.Class!.Name + "'")),
                    "the registration of each would be called '" + same.Key + "', and a call could not tell them apart; let what it is named after differ between them, or call the method itself for these, with its type arguments written out"));
                closed.RemoveAll(each => each.Name == same.Key);
            }
        }

        wrappers.AddRange(closed.Select(static each => each.Wrapper));
        return new MethodClosing(wrappers, diagnostics);
    }

    /// <summary>The one closing of a method none of whose templates is declared several times: no class to name it after.</summary>
    private static readonly DefinitionFactory.TemplateSource?[] SingleClosing = [null];

    /// <summary>
    /// The template a method is closed once per class of, with those classes: the one template among its
    /// <c>[TemplateType]</c>s that allows several classes and has several. Null when no template has several,
    /// and when one of them is refused.
    /// <para>
    /// <paramref name="refused"/> holds the templates whose several classes the caller reports as DDD00045,
    /// each for a reason of its own: a template that does not allow several, classes that share a name, since
    /// the wrappers are named after them, and two templates that each could have a wrapper per class, since
    /// there is no telling which class of the one goes with which of the other. A template that could have a
    /// wrapper per class is not refused for another that is: once that one is put right there is nothing wrong
    /// with it, so it is not named as a mistake beside it.
    /// </para>
    /// <para>
    /// Classes that share a name are in the way only where the wrappers are named after their classes
    /// (<paramref name="namedAfterItsClass"/>). A method that names its wrapper itself is judged by the names
    /// its wrappers come to, once they are known.
    /// </para>
    /// </summary>
    private static (string Key, List<DefinitionFactory.TemplateSource> Classes)? RepeatedTemplate(
        Take?[] takes,
        List<Candidate>?[] candidates,
        bool namedAfterItsClass,
        out HashSet<string> refused)
    {
        refused = new HashSet<string>(StringComparer.Ordinal);
        var perClass = new List<(string Key, List<DefinitionFactory.TemplateSource> Classes)>();
        for (var position = 0; position < takes.Length; position++)
        {
            if (takes[position] is not { } take
                || candidates[position] is not { Count: > 1 } found
                || refused.Contains(take.Key)
                || perClass.Any(each => each.Key == take.Key))
            {
                continue;
            }

            if (take.AllowSeveral && (!namedAfterItsClass || EachHasANameOfItsOwn(found)))
            {
                perClass.Add((take.Key, found.Select(static candidate => candidate.Source).ToList()));
            }
            else
            {
                refused.Add(take.Key);
            }
        }

        if (perClass.Count > 1)
        {
            foreach (var each in perClass)
            {
                refused.Add(each.Key);
            }
        }

        return refused.Count == 0 && perClass.Count == 1 ? perClass[0] : null;
    }

    /// <summary>Whether the classes found for a template can each have a wrapper named after it.</summary>
    private static bool EachHasANameOfItsOwn(List<Candidate> found)
        => found.Select(static candidate => candidate.Source.Name).Distinct(StringComparer.Ordinal).Count() == found.Count;

    /// <summary>
    /// What to do about several classes <see cref="RepeatedTemplate"/> refused, phrased to end DDD00045: keep
    /// one, for a template that is declared once; otherwise what stands in the way of a wrapper per class.
    /// </summary>
    private static string AdviceFor(Take take, List<Candidate> found, bool namedAfterItsClass)
        => !take.AllowSeveral ? DefinitionFactory.KeepOne
            : namedAfterItsClass && !EachHasANameOfItsOwn(found)
                ? "a registration is named after its class, so give each a name of its own"
                : "a registration is written once per class of one template, and this one takes two templates that each have several; call the method itself for these, with its type arguments written out";

    /// <summary>
    /// The method closed over one class of each of its templates, with what the wrapper is called, or null with
    /// what says why: a class or an id that does not meet the method's constraints is DDD00050, reported where
    /// its candidate says, and a class that is itself refused leaves the wrapper out without a second report.
    /// So is a type argument whose id is taken and that has none, and a type the wrapper is named after that
    /// has no name.
    /// </summary>
    /// <param name="method">The package's method.</param>
    /// <param name="takes">What each of its type parameters takes.</param>
    /// <param name="naming">What the method says its wrapper is called, or null when it has the method's name.</param>
    /// <param name="candidates">The classes found for each type parameter that takes one, none of them empty.</param>
    /// <param name="repeatedKey">The template the method is closed once per class of, or null.</param>
    /// <param name="each">The class of that template this wrapper is closed over, and named after; null when there is one closing.</param>
    /// <param name="diagnostics">Where DDD00050 is added.</param>
    /// <param name="compilation">The project.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    private static (string Name, RegistrationWrapper Wrapper, ITypeSymbol? NamedAfter)? ClosedOver(
        IMethodSymbol method,
        Take?[] takes,
        WrapperName? naming,
        List<Candidate>?[] candidates,
        string? repeatedKey,
        DefinitionFactory.TemplateSource? each,
        List<DiagnosticInfo> diagnostics,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var parameters = method.TypeParameters;
        var display = method.ContainingType.Name + "." + method.Name;
        var texts = new string?[parameters.Length];
        var symbols = new ITypeSymbol?[parameters.Length];
        var locations = new LocationInfo?[parameters.Length];

        // What each type is called where a wrapper is named after it: as its declaration names it.
        var names = new string?[parameters.Length];

        // An id that is still to be generated has no symbol to ask, and is always a struct.
        var generatedIds = new bool[parameters.Length];
        var met = true;
        for (var position = 0; position < parameters.Length; position++)
        {
            if (takes[position] is not { } take || candidates[position] is not { } found)
            {
                continue;
            }

            // Every type parameter that names the repeated template is filled from the same class of it.
            var chosen = each is not null && take.Key == repeatedKey
                ? found.FindIndex(candidate => candidate.Source.FullyQualifiedName == each.FullyQualifiedName)
                : 0;
            if (chosen < 0)
            {
                return null;
            }

            var source = found[chosen].Source;
            var symbol = source.SymbolIn(compilation);
            locations[position] = found[chosen].Location;

            if (take.TakeType)
            {
                if (!source.CanGenerate)
                {
                    return null;
                }

                texts[position] = source.FullyQualifiedName;
                symbols[position] = symbol;
                names[position] = source.Name;
            }
            else if (take.Argument == 0)
            {
                if (!source.IdIsEntityId)
                {
                    return null;
                }

                texts[position] = source.IdType;
                symbols[position] = symbol is null ? null : EntityDeclarations.IdArgumentOf(symbol);
                names[position] = symbols[position]?.Name ?? LastNameOf(source.IdType);
            }
            else
            {
                // A later type argument is the application's to choose. One the compiler could not bind it has
                // reported, and one that names a type parameter is on a generic class, which its own diagnostic refuses.
                if (symbol is null
                    || EntityDeclarations.TemplateArgumentOf(symbol, take.Key, take.Argument) is not { } argument
                    || MentionsATypeParameter(argument))
                {
                    return null;
                }

                if (!take.IdOfArgument)
                {
                    texts[position] = argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    symbols[position] = argument;
                    names[position] = argument.Name;
                    continue;
                }

                // The id of the class the argument names. A class that is declared an entity and whose id cannot
                // be had is refused by its own diagnostic, which is the one to fix.
                var idText = string.Empty;
                var idName = string.Empty;
                ITypeSymbol? idSymbol = null;
                var itsId = argument is INamedTypeSymbol named
                    ? DefinitionFactory.IdOf(named, cancellationToken, out idText, out idName, out idSymbol)
                    : DefinitionFactory.DeclaredIdKind.NotAnEntity;
                if (itsId == DefinitionFactory.DeclaredIdKind.Unavailable)
                {
                    return null;
                }

                var requirement = itsId == DefinitionFactory.DeclaredIdKind.NotAnEntity ? "an entity or an aggregate root, whose id it takes"
                    : idSymbol is null && parameters[position].HasReferenceTypeConstraint ? "an id that is a class, and the id generated for it is a struct"
                    : null;
                if (requirement is not null)
                {
                    met = false;
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.TemplateRegistrationMissesConstraint,
                        locations[position],
                        ShortNameOf(argument, take),
                        display,
                        parameters[position].Name,
                        requirement));
                    continue;
                }

                texts[position] = idText;
                symbols[position] = idSymbol;
                names[position] = idName;
                generatedIds[position] = idSymbol is null;
            }
        }

        for (var position = 0; position < parameters.Length; position++)
        {
            if (takes[position] is not { } take || symbols[position] is not { } argument)
            {
                continue;
            }

            var requirement = take.TakeType
                ? argument is INamedTypeSymbol taken ? UnmetConstraint(parameters[position], taken, parameters, symbols, compilation) : null
                : take.Argument == 0 || take.IdOfArgument
                    ? argument is INamedTypeSymbol id ? UnmetIdConstraint(parameters[position], id) : null
                    : UnmetByALaterArgument(parameters[position], argument, parameters, symbols, compilation, cancellationToken);
            if (requirement is null)
            {
                continue;
            }

            met = false;
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.TemplateRegistrationMissesConstraint,
                locations[position],
                ShortNameOf(argument, take),
                display,
                parameters[position].Name,
                requirement));
        }

        if (!met)
        {
            return null;
        }

        var name = each is null ? method.Name : method.Name + PerClassInfix + each.Name;
        ITypeSymbol? namedAfter = null;
        if (naming is not null)
        {
            namedAfter = naming.Positions.Length > 0 ? symbols[naming.Positions[0]] : null;
            var called = new StringBuilder(naming.Text[0]);
            for (var index = 0; index < naming.Positions.Length; index++)
            {
                var position = naming.Positions[index];
                if (names[position] is not { Length: > 0 } part || !SyntaxFacts.IsValidIdentifier(part))
                {
                    // An array, say: whatever the application wrote there, and nothing a method can be called after.
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.TemplateRegistrationMissesConstraint,
                        locations[position],
                        symbols[position]?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? texts[position] ?? parameters[position].Name,
                        display,
                        parameters[position].Name,
                        "a type with a name of its own, which the registration is named after"));
                    return null;
                }

                called.Append(part).Append(naming.Text[index + 1]);
            }

            name = called.ToString();
        }

        return (name, Wrapper(method, name, texts, symbols, generatedIds, takes), namedAfter);
    }

    /// <summary>
    /// Where several classes whose wrappers come to one name are reported: on the class to fix, so the report
    /// stands where the author has to act. The type a wrapper is named after, the resource of a member class
    /// say, usually holds one of them already, in a member of its own: that one belongs, and the class to fix
    /// is one it does not hold. Where that does not tell them apart, the class declared last, most likely the
    /// one just added. Null where the classes are not this project's, which leaves the report where it was.
    /// </summary>
    /// <param name="same">The classes, each with the type its wrapper is named after.</param>
    /// <param name="found">The classes found for the template, with where each is reported, in the order they are declared.</param>
    /// <param name="compilation">The project.</param>
    private static LocationInfo? ToFix(List<(DefinitionFactory.TemplateSource Class, ITypeSymbol? NamedAfter)> same, List<Candidate> found, Compilation compilation)
    {
        var located = same
            .Select(each => (each.Class, each.NamedAfter, At: found.FindIndex(candidate => candidate.Source.FullyQualifiedName == each.Class.FullyQualifiedName)))
            .Where(static each => each.At >= 0)
            .OrderBy(static each => each.At)
            .ToList();
        if (located.Count == 0 || located.Select(each => found[each.At].Location).Distinct().Count() < located.Count)
        {
            return null;
        }

        var held = located
            .Where(each => each.Class.SymbolIn(compilation) is { } symbol && each.NamedAfter is INamedTypeSymbol owner && Mentions(owner, symbol))
            .ToList();
        var toFix = held.Count == 1 ? located.Where(each => each.At != held[0].At).ToList() : located;
        return toFix.Count == 0 ? null : found[toFix[toFix.Count - 1].At].Location;
    }

    /// <summary>Whether a property or field of <paramref name="owner"/> has <paramref name="type"/> in its type: as itself, an element or a type argument.</summary>
    private static bool Mentions(INamedTypeSymbol owner, INamedTypeSymbol type)
    {
        foreach (var member in owner.GetMembers())
        {
            var memberType = member switch
            {
                IPropertySymbol property => property.Type,
                IFieldSymbol field => field.Type,
                _ => null,
            };

            if (memberType is not null && Has(memberType))
            {
                return true;
            }
        }

        return false;

        bool Has(ITypeSymbol candidate)
            => SymbolEqualityComparer.Default.Equals(candidate, type)
               || (candidate is INamedTypeSymbol { IsGenericType: true } generic && generic.TypeArguments.Any(Has))
               || (candidate is IArrayTypeSymbol array && Has(array.ElementType));
    }

    /// <summary>
    /// The last name of a type written out in full, <c>SongCommentId</c> of <c>global::Sample.SongCommentId</c>,
    /// for an id whose symbol cannot be had; null for one with type arguments, which has no one name.
    /// </summary>
    private static string? LastNameOf(string fullyQualified)
        => fullyQualified.IndexOf('<') >= 0
            ? null
            : fullyQualified.Substring(Math.Max(fullyQualified.LastIndexOf('.'), fullyQualified.LastIndexOf(':')) + 1);

    /// <summary>Whether a type is, or is closed over, a type parameter: nothing a wrapper outside the class could name.</summary>
    private static bool MentionsATypeParameter(ITypeSymbol type)
        => type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => MentionsATypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(MentionsATypeParameter) || (named.ContainingType is { } outer && MentionsATypeParameter(outer)),
            _ => false,
        };

    /// <summary>
    /// A type taken, as a message and a wrapper's documentation name it: a class and its id by their names, and a
    /// later type argument as its author wrote it, since that need not be a type with a name of its own.
    /// </summary>
    private static string ShortNameOf(ITypeSymbol type, Take take)
        => take.TakeType || take.Argument == 0 ? type.Name : type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static DefinitionFactory.TemplateSource SourceOf(EntityDefinition definition, bool canGenerate)
        => new(
            definition.Type.Name,
            definition.Type.FullyQualifiedName,
            definition.IdType,
            definition.TemplateIdIsEntityId,
            canGenerate,
            definition.MetadataName,
            symbol: null);

    /// <summary>
    /// What an id taken by a registration does not meet, phrased to follow "which requires", or null when it
    /// meets what can be told. Only whether it is a struct or a class is judged: an id declared in this project
    /// has its interfaces, <c>IEntityId</c> and <c>IEquatable</c>, written by the generator, so the compilation
    /// being generated does not show them yet, and the generator's own shape check already asks for them.
    /// </summary>
    private static string? UnmetIdConstraint(ITypeParameterSymbol parameter, INamedTypeSymbol argument)
        => argument.TypeKind == TypeKind.Error ? null
            : parameter.HasValueTypeConstraint && !argument.IsValueType ? "a struct"
            : parameter.HasReferenceTypeConstraint && !argument.IsReferenceType ? "a class"
            : null;

    /// <summary>
    /// What a class taken by a registration does not meet, phrased to follow "which requires", or null when it
    /// meets every constraint. A class declared here is judged by what the generator will make it, as DDD00048
    /// judges it: the parent its template derives it from, closed over its own id.
    /// </summary>
    private static string? UnmetConstraint(
        ITypeParameterSymbol parameter,
        INamedTypeSymbol argument,
        ImmutableArray<ITypeParameterSymbol> parameters,
        ITypeSymbol?[] arguments,
        Compilation compilation)
    {
        if (parameter.HasValueTypeConstraint && !argument.IsValueType)
        {
            return "a struct";
        }

        // The parameterless constructor the generator writes is never public, so only a class from a
        // referenced assembly can have a public one.
        if (parameter.HasConstructorConstraint
            && (argument.IsAbstract
                || !argument.DeclaringSyntaxReferences.IsEmpty
                || !argument.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public)))
        {
            return "a public parameterless constructor";
        }

        foreach (var constraint in parameter.ConstraintTypes)
        {
            if (DefinitionFactory.Substitute(constraint, parameters, arguments, compilation) is { } expected
                && !DefinitionFactory.Satisfies(argument, expected, compilation))
            {
                return "'" + expected.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "'";
            }
        }

        return null;
    }

    /// <summary>
    /// What a later type argument of a template does not meet, phrased to follow "which requires", or null when
    /// it meets what can be told.
    /// <para>
    /// It is whatever type the application wrote there, and is judged as such
    /// (<see cref="DefinitionFactory.UnmetByAnArgument"/>), with one exception: a class this project declares an
    /// entity or an aggregate root, such as the aggregate a template names its class as belonging to. What that
    /// class will be is the generator's own to say, so it is judged by it, as a class the method takes is
    /// (<see cref="UnmetConstraint"/>), rather than left to the compiler: a child entity where the method asks
    /// for an aggregate root would otherwise be an error inside the registration written for the project.
    /// </para>
    /// </summary>
    private static string? UnmetByALaterArgument(
        ITypeParameterSymbol parameter,
        ITypeSymbol argument,
        ImmutableArray<ITypeParameterSymbol> parameters,
        ITypeSymbol?[] arguments,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (argument is not INamedTypeSymbol { TypeKind: TypeKind.Class } declared
            || declared.DeclaringSyntaxReferences.IsEmpty
            || !EntityDeclarations.IsEntityOrAggregateRoot(declared))
        {
            return DefinitionFactory.UnmetByAnArgument(parameter, argument, parameters, arguments, compilation, cancellationToken);
        }

        // Told from the declaration alone, so it holds for a class whose id is still to be generated as well,
        // where the constraint cannot be closed over that id yet.
        if (!EntityDeclarations.IsAggregateRoot(declared) && parameter.ConstraintTypes.Any(IsTheAggregateRootBase))
        {
            return "an aggregate root";
        }

        return UnmetConstraint(parameter, declared, parameters, arguments, compilation);
    }

    /// <summary>Whether a constraint is the toolkit's base class of an aggregate root, over whatever id.</summary>
    private static bool IsTheAggregateRootBase(ITypeSymbol constraint)
        => constraint is INamedTypeSymbol { OriginalDefinition: { MetadataName: "AggregateRoot`1" } definition }
           && definition.ContainingNamespace.ToDisplayString() == "DDDToolkit.BaseTypes";

    // ------------------------------------------------------------------ writing the wrapper

    /// <summary>A type argument of the method, closed over one of this project's types.</summary>
    private readonly record struct Closed(string Text, bool IsValueType);

    /// <summary>The wrapper called <paramref name="wrapperName"/> that forwards to <paramref name="method"/> closed over <paramref name="texts"/>.</summary>
    private static RegistrationWrapper Wrapper(IMethodSymbol method, string wrapperName, string?[] texts, ITypeSymbol?[] symbols, bool[] generatedIds, Take?[] takes)
    {
        var closed = new Dictionary<ITypeParameterSymbol, Closed>(SymbolEqualityComparer.Default);
        var open = new List<ITypeParameterSymbol>();
        var typeArguments = new List<string>();
        for (var position = 0; position < method.TypeParameters.Length; position++)
        {
            var parameter = method.TypeParameters[position];
            if (takes[position] is not null && texts[position] is { } text)
            {
                closed.Add(parameter, new Closed(text, symbols[position]?.IsValueType ?? generatedIds[position]));
                typeArguments.Add(text);
            }
            else
            {
                open.Add(parameter);
                typeArguments.Add(Identifier(parameter.Name));
            }
        }

        var signature = new StringBuilder("public static ")
            .Append(method.ReturnsVoid ? "void" : Render(method.ReturnType, closed))
            .Append(' ')
            .Append(Identifier(wrapperName));
        if (open.Count > 0)
        {
            signature.Append('<').Append(string.Join(", ", open.Select(static parameter => Identifier(parameter.Name)))).Append('>');
        }

        var declaredParameters = new List<string>();
        var passed = new List<string>();
        foreach (var parameter in method.Parameters)
        {
            var modifiers = (parameter.Ordinal == 0 && method.IsExtensionMethod ? "this " : string.Empty)
                            + (parameter.IsParams ? "params " : string.Empty)
                            + RefKindOf(parameter.RefKind);
            var name = Identifier(parameter.Name);
            declaredParameters.Add(modifiers + Render(parameter.Type, closed) + " " + name
                                   + (parameter.HasExplicitDefaultValue ? " = " + DefaultValueOf(parameter, closed) : string.Empty));
            passed.Add(RefKindOf(parameter.RefKind is RefKind.RefReadOnlyParameter ? RefKind.In : parameter.RefKind) + name);
        }

        signature.Append('(').Append(string.Join(", ", declaredParameters)).Append(')');

        var constraints = open
            .Select(parameter => ConstraintClause(parameter, closed))
            .OfType<string>()
            .ToEquatableArray();

        var declaring = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var call = declaring + "." + Identifier(method.Name) + "<" + string.Join(", ", typeArguments) + ">(" + string.Join(", ", passed) + ")";

        var over = method.TypeParameters
            .Select((parameter, position) => takes[position] is not { } take ? null
                : symbols[position] is { } symbol ? ShortNameOf(symbol, take)
                : generatedIds[position] ? LastNameOf(texts[position]!)
                : null)
            .OfType<string>();
        var summary = "Calls " + method.ContainingType.Name + "." + method.Name + " closed over this project's "
                      + DefinitionFactory.Listed(over) + (open.Count > 0 ? ", with " + DefinitionFactory.Listed(open.Select(static parameter => parameter.Name)) + " still to choose." : ".");

        return new RegistrationWrapper(Escape(summary), signature.ToString(), constraints, call, wrapperName, typeArguments.ToEquatableArray());
    }

    /// <summary>The constraint clause of a type parameter the wrapper keeps open, or null when it has none.</summary>
    private static string? ConstraintClause(ITypeParameterSymbol parameter, Dictionary<ITypeParameterSymbol, Closed> closed)
    {
        var parts = new List<string>();
        if (parameter.HasReferenceTypeConstraint)
        {
            parts.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
        }
        else if (parameter.HasUnmanagedTypeConstraint)
        {
            parts.Add("unmanaged");
        }
        else if (parameter.HasValueTypeConstraint)
        {
            parts.Add("struct");
        }
        else if (parameter.HasNotNullConstraint)
        {
            parts.Add("notnull");
        }

        parts.AddRange(parameter.ConstraintTypes.Select(constraint => Render(constraint, closed)));

        if (parameter.HasConstructorConstraint)
        {
            parts.Add("new()");
        }

        return parts.Count == 0 ? null : "where " + Identifier(parameter.Name) + " : " + string.Join(", ", parts);
    }

    /// <summary>
    /// A type as the wrapper spells it: fully qualified, with its nullable annotations, and with every type
    /// parameter of the method the project fills replaced by the type that fills it. The display the compiler
    /// offers cannot do the replacement, and a type such as <c>TenancyOptions&lt;TTenantId, ...&gt;</c> needs it
    /// inside its type arguments.
    /// </summary>
    private static string Render(ITypeSymbol type, Dictionary<ITypeParameterSymbol, Closed> closed)
    {
        var annotated = type.NullableAnnotation == NullableAnnotation.Annotated;
        switch (type)
        {
            case ITypeParameterSymbol parameter when closed.TryGetValue(parameter, out var argument):
                // An unconstrained T? over a value type is that value type, not a Nullable of it.
                return argument.Text + (annotated && !argument.IsValueType ? "?" : string.Empty);

            case ITypeParameterSymbol parameter:
                return Identifier(parameter.Name) + (annotated ? "?" : string.Empty);

            case IArrayTypeSymbol array:
                return Render(array.ElementType, closed) + "[" + new string(',', array.Rank - 1) + "]" + (annotated ? "?" : string.Empty);

            case INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable:
                return Render(nullable.TypeArguments[0], closed) + "?";

            case INamedTypeSymbol { IsTupleType: true } tuple:
                return "(" + string.Join(", ", tuple.TupleElements.Select(element =>
                           Render(element.Type, closed) + (element.IsExplicitlyNamedTupleElement ? " " + Identifier(element.Name) : string.Empty)))
                       + ")" + (annotated ? "?" : string.Empty);

            case INamedTypeSymbol named when named.TypeArguments.Length > 0 || named.ContainingType is not null:
                var qualifier = named.ContainingType is { } outer
                    ? Render(outer, closed) + "."
                    : named.ContainingNamespace is { IsGlobalNamespace: false } scope ? "global::" + scope.ToDisplayString() + "." : "global::";
                var arguments = named.TypeArguments.Length == 0
                    ? string.Empty
                    : "<" + string.Join(", ", named.TypeArguments.Select(argument => Render(argument, closed))) + ">";
                return qualifier + Identifier(named.Name) + arguments + (annotated && !named.IsValueType ? "?" : string.Empty);

            default:
                return type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                       + (annotated && !type.IsValueType ? "?" : string.Empty);
        }
    }

    /// <summary>
    /// A parameter's default value as C# the wrapper can repeat, so a call that leaves it out means what it
    /// means on the package's method.
    /// </summary>
    private static string DefaultValueOf(IParameterSymbol parameter, Dictionary<ITypeParameterSymbol, Closed> closed)
    {
        var value = parameter.ExplicitDefaultValue;
        var type = parameter.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : parameter.Type;

        if (value is null)
        {
            return parameter.Type.IsReferenceType || parameter.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? "null" : "default";
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumeration)
        {
            var name = Render(enumeration.WithNullableAnnotation(NullableAnnotation.None), closed);
            var member = enumeration.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field => field.HasConstantValue && Equals(field.ConstantValue, value));
            return member is not null
                ? name + "." + Identifier(member.Name)
                : "(" + name + ")(" + SymbolDisplay.FormatPrimitive(value, quoteStrings: false, useHexadecimalNumbers: false) + ")";
        }

        return value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            char character => SymbolDisplay.FormatLiteral(character, quote: true),
            bool flag => flag ? "true" : "false",
            float single when float.IsNaN(single) => "float.NaN",
            float single when float.IsPositiveInfinity(single) => "float.PositiveInfinity",
            float single when float.IsNegativeInfinity(single) => "float.NegativeInfinity",
            float single => single.ToString("R", CultureInfo.InvariantCulture) + "F",
            double number when double.IsNaN(number) => "double.NaN",
            double number when double.IsPositiveInfinity(number) => "double.PositiveInfinity",
            double number when double.IsNegativeInfinity(number) => "double.NegativeInfinity",
            double number => number.ToString("R", CultureInfo.InvariantCulture) + "D",
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
            long number => number.ToString(CultureInfo.InvariantCulture) + "L",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
            uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
            _ => SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false),
        };
    }

    private static string RefKindOf(RefKind kind) => kind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        RefKind.RefReadOnlyParameter => "ref readonly ",
        _ => string.Empty,
    };

    /// <summary>A name as an identifier: a keyword gets an <c>@</c>.</summary>
    private static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    /// <summary>Text for an XML doc comment.</summary>
    private static string Escape(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private sealed class TypeAndNameComparer : IEqualityComparer<(INamedTypeSymbol Type, string Name)>
    {
        public bool Equals((INamedTypeSymbol Type, string Name) x, (INamedTypeSymbol Type, string Name) y)
            => SymbolEqualityComparer.Default.Equals(x.Type, y.Type) && x.Name == y.Name;

        public int GetHashCode((INamedTypeSymbol Type, string Name) obj)
            => (SymbolEqualityComparer.Default.GetHashCode(obj.Type) * 31) + StringComparer.Ordinal.GetHashCode(obj.Name);
    }
}

/// <summary>
/// The registrations of one method name of one declaring type, closed over this project's classes: the file
/// the generator writes, and what it reports. A file whose every method was refused has no wrappers and is
/// not written, but still reports.
/// </summary>
/// <param name="HintName">The file's name, <c>TenancyModelBuilderExtensions.AddTenancy.Registration.{hash}.g.cs</c>.</param>
/// <param name="Namespace">The declaring type's namespace, where the wrapper goes, so a call needs no extra using.</param>
/// <param name="ClassName">The wrapper class, <c>Generated{declaring type}</c>, internal to the project.</param>
/// <param name="DeclaringType">The declaring type's name, for the class's documentation.</param>
/// <param name="Wrappers">One wrapper per overload that could be closed, and per class of a template that allows several.</param>
/// <param name="Diagnostics">DDD00045, DDD00049 and DDD00050, as they apply.</param>
internal sealed record RegistrationFile(
    string HintName,
    string Namespace,
    string ClassName,
    string DeclaringType,
    EquatableArray<RegistrationWrapper> Wrappers,
    EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>One registration method, closed over this project's classes.</summary>
/// <param name="Summary">Its documentation, escaped for XML.</param>
/// <param name="Signature">Everything up to and including the parameter list.</param>
/// <param name="Constraints">The constraint clauses of the type parameters it keeps open.</param>
/// <param name="Call">The call to the package's method it forwards to.</param>
/// <param name="Name">What it is called: the method's name, or what the method or its class names it.</param>
/// <param name="TypeArguments">
/// The method's type arguments as the call passes them, in the method's order: a type of this project, fully
/// qualified, for each one the wrapper is closed over, and the type parameter's own name for each it leaves open.
/// </param>
internal sealed record RegistrationWrapper(
    string Summary,
    string Signature,
    EquatableArray<string> Constraints,
    string Call,
    string Name,
    EquatableArray<string> TypeArguments);
