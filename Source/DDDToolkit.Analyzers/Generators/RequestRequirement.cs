using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers;

/// <summary>
/// A command or a query of a module, read for what it says it requires of its caller with attributes: the line the
/// generator writes for it into its module's interface, or what stands in the way of one.
/// </summary>
/// <remarks>
/// A request says what it requires with an attribute whose class a package marked
/// <c>[StatesRequirement(typeof(Factory), "Method")]</c>, such as <c>[SignedIn]</c> or <c>[InTenant]</c>, or with
/// <c>[RequiresKey(key)]</c> and a marker of where the key is held on one of its members, whose class a package marked
/// <c>[KeyMarker(typeof(Factory), "Method")]</c>, such as <c>[AtUnit]</c>; a key without a marker is held where a
/// package says with <c>[assembly: KeyWithoutMarker]</c>. Nothing here knows a package's attribute by its name: the
/// marks say which call to write, so a package of the application's own declares attributes the same way. The line is
/// that call, written out: <c>global::Shop.AddUnit request =&gt; global::...TenancyAccess.AtUnit("units.manage", request.Parent)</c>.
/// <para>
/// It is read from the compilation the request is part of, the classes the package's markers name included, so what it
/// reports is what the project sees. A type declared in parts is read once, at the first of its parts that may be a
/// request, whichever part carries the attributes or the interface.
/// </para>
/// </remarks>
/// <param name="FullyQualifiedName">The request's name from <c>global::</c> on.</param>
/// <param name="Depth">How many classes it derives from: a line for a derived request comes before its base's.</param>
/// <param name="WrittenInto">The module interfaces of this project, partial, that the line goes into.</param>
/// <param name="Arm">The line, without its comma, or <see langword="null"/> where the request states nothing with attributes or cannot.</param>
/// <param name="Diagnostics">What is wrong with what the request says.</param>
internal sealed record RequestRequirement(
    string FullyQualifiedName,
    int Depth,
    EquatableArray<string> WrittenInto,
    string? Arm,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    /// <summary>The name a pattern binds the request to in its line.</summary>
    public const string Designation = "request";

    private static readonly ConditionalWeakTable<Compilation, List<(string Factory, string Method, string Assembly)>> KeysWithoutMarker = new();

    private static readonly ConditionalWeakTable<Compilation, Dictionary<string, HashSet<string>?>> IdsOfTemplates = new();

    private static readonly ConditionalWeakTable<Compilation, Dictionary<string, List<INamedTypeSymbol>>> SourceTemplateClasses = new();

    /// <summary>A class, a record or a struct that implements something or carries an attribute: what may be a request.</summary>
    public static bool MayBeOne(SyntaxNode node, CancellationToken cancellationToken)
        => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax
           && node is TypeDeclarationSyntax { } type
           && (type.BaseList is not null || type.AttributeLists.Count > 0);

    /// <summary>
    /// What the type says it requires, or <see langword="null"/> for a type that is no request and carries no
    /// requirement attribute and no marker.
    /// </summary>
    public static RequestRequirement? Read(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not INamedTypeSymbol type)
        {
            return null;
        }

        // A type declared in parts is read once, and every part's attributes are its. Not at its first part, which may
        // be a bare 'partial record X;' the predicate never hands over: at the first part the predicate does, so the
        // attributes or the interface on any part are read, and a request in parts that states nothing is reported.
        if (type.DeclaringSyntaxReferences.Length > 1 && !IsReadAt(type, context.Node, cancellationToken))
        {
            return null;
        }

        var compilation = context.SemanticModel.Compilation;
        if (compilation.GetTypeByMetadataName(KnownTypes.RequireAccessInterface) is not { } requireAccess
            || requireAccess.GetMembers("RequiredAccess").OfType<IPropertySymbol>().FirstOrDefault() is not { } requiredAccess)
        {
            return null;
        }

        // A type that is no request and carries no requirement attribute is not looked into further: a marker on a
        // member of a type no requirement is read from marks nothing, and most types of a project are such types.
        var modules = type.AllInterfaces.Where(static each => DefinitionFactory.HasAttribute(each, KnownTypes.AccessRequestsAttribute)).ToList();
        var stated = StatedOn(type);
        if (modules.Count == 0 && stated.Count == 0)
        {
            return null;
        }

        var markers = MarkersOf(type, cancellationToken);

        var reading = new Reading(type, compilation, requiredAccess, modules, stated, markers, ((TypeDeclarationSyntax)context.Node).Identifier, cancellationToken);
        return reading.Result();
    }

    /// <summary>Whether <paramref name="node"/> is the part of a type declared in parts that it is read at: the first that <see cref="MayBeOne"/> takes.</summary>
    private static bool IsReadAt(INamedTypeSymbol type, SyntaxNode node, CancellationToken cancellationToken)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax part && MayBeOne(part, cancellationToken))
            {
                return reference.SyntaxTree == node.SyntaxTree && reference.Span == node.Span;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------ what the request carries

    /// <summary>The requirement attributes on the type: those a package marked <c>[StatesRequirement]</c>, and <c>[RequiresKey]</c>.</summary>
    private static List<Stated> StatedOn(INamedTypeSymbol type)
    {
        var found = new List<Stated>();
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass)
            {
                continue;
            }

            if (Is(attributeClass, KnownTypes.RequiresKeyAttribute))
            {
                found.Add(new Stated(attribute, attributeClass, null, "", false, IsKey: true));
                continue;
            }

            var states = attributeClass.OriginalDefinition.GetAttributes().FirstOrDefault(static each => each.AttributeClass is { } meta && Is(meta, KnownTypes.StatesRequirementAttribute));
            if (states is { ConstructorArguments.Length: 2 }
                && states.ConstructorArguments[0].Value is INamedTypeSymbol factory
                && states.ConstructorArguments[1].Value is string method)
            {
                var queriesOnly = states.NamedArguments.Any(static named => named.Key == "QueriesOnly" && named.Value.Value is true);
                found.Add(new Stated(attribute, attributeClass, factory, method, queriesOnly, IsKey: false));
            }
        }

        return found;
    }

    /// <summary>
    /// The markers on the type's members: on a property, its own or one it inherits, and on a parameter of its primary
    /// constructor, which is the property of a positional record.
    /// </summary>
    private static List<Marker> MarkersOf(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var found = new List<Marker>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer || !seen.Add(property.Name))
                {
                    continue;
                }

                foreach (var attribute in property.GetAttributes())
                {
                    if (MarkerOf(attribute) is { } marker)
                    {
                        found.Add(marker with { Name = property.Name, Type = property.Type, Property = property, Location = WhereWritten(attribute, cancellationToken) });
                    }
                }
            }
        }

        foreach (var constructor in type.InstanceConstructors)
        {
            if (!constructor.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax))
            {
                continue;
            }

            // The primary constructor: in a positional record a parameter is the property of its name, which the
            // compiler declares, or which a record it derives from has already and the compiler leaves to it; in a
            // class or a struct it is no member at all.
            foreach (var parameter in constructor.Parameters)
            {
                foreach (var attribute in parameter.GetAttributes())
                {
                    if (MarkerOf(attribute) is { } marker)
                    {
                        var property = type.IsRecord ? PropertyNamed(type, parameter.Name) : null;
                        found.Add(marker with { Name = parameter.Name, Type = parameter.Type, Property = property, OnParameter = true, Location = WhereWritten(attribute, cancellationToken) });
                    }
                }
            }
        }

        return found;
    }

    /// <summary>The instance property of <paramref name="name"/> the type has, its own or the nearest one it inherits.</summary>
    private static IPropertySymbol? PropertyNamed(INamedTypeSymbol type, string name)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (current.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(static property => !property.IsStatic && !property.IsIndexer) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>A marker of where a key is held, or of one more argument of such a marker's call; null for any other attribute.</summary>
    private static Marker? MarkerOf(AttributeData attribute)
    {
        if (attribute.AttributeClass is not { } attributeClass)
        {
            return null;
        }

        foreach (var meta in attributeClass.OriginalDefinition.GetAttributes())
        {
            if (meta.AttributeClass is not { } metaClass)
            {
                continue;
            }

            if (Is(metaClass, KnownTypes.KeyMarkerAttribute)
                && meta.ConstructorArguments.Length == 2
                && meta.ConstructorArguments[0].Value is INamedTypeSymbol factory
                && meta.ConstructorArguments[1].Value is string method)
            {
                var idOf = meta.NamedArguments.FirstOrDefault(static named => named.Key == "IdOf").Value.Value as INamedTypeSymbol;
                var argument = meta.NamedArguments.FirstOrDefault(static named => named.Key == "Argument").Value.Value is int position ? position : 0;
                var idOfArgument = meta.NamedArguments.Any(static named => named.Key == "IdOfArgument" && named.Value.Value is true);
                return new Marker(attributeClass, Place: true, factory, method, null, idOf, argument, idOfArgument);
            }

            if (Is(metaClass, KnownTypes.KeyMarkerArgumentAttribute)
                && meta.ConstructorArguments.Length == 1
                && meta.ConstructorArguments[0].Value is INamedTypeSymbol goesWith)
            {
                return new Marker(attributeClass, Place: false, null, "", goesWith, null, 0, false);
            }
        }

        return null;
    }

    /// <summary>Where an attribute is written, for a diagnostic about it.</summary>
    private static LocationInfo? WhereWritten(AttributeData attribute, CancellationToken cancellationToken)
        => attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is { } syntax ? LocationInfo.From(syntax) : null;

    private static bool Is(INamedTypeSymbol type, string metadataName)
        => EntityDeclarations.MetadataNameOf(type) == metadataName;

    /// <summary>An attribute as a reader writes it, in a message: <c>[RequiresKey]</c>, <c>[SeenWith&lt;DocumentId&gt;]</c>.</summary>
    private static string Bracketed(INamedTypeSymbol attributeClass)
    {
        var name = attributeClass.Name.EndsWith("Attribute", StringComparison.Ordinal) && attributeClass.Name.Length > "Attribute".Length
            ? attributeClass.Name.Substring(0, attributeClass.Name.Length - "Attribute".Length)
            : attributeClass.Name;
        return "[" + name + (attributeClass.TypeArguments.Length > 0
            ? "<" + string.Join(", ", attributeClass.TypeArguments.Select(static argument => argument.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) + ">"
            : string.Empty) + "]";
    }

    /// <summary>A type as a reader writes it, in a message.</summary>
    private static string Written(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    /// <summary>A list of things as a sentence says it: <c>a</c>, <c>a and b</c>, <c>a, b and c</c>.</summary>
    private static string Listed(IEnumerable<string> items) => DefinitionFactory.Listed(items);

    /// <summary>A name as code writes it, with an <c>@</c> where it is a keyword.</summary>
    private static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    /// <summary>An argument of an attribute as C# writes it: a literal, an enum's value, a <c>typeof</c>, an array.</summary>
    private static string Literal(TypedConstant constant)
    {
        if (constant.IsNull)
        {
            return "null";
        }

        switch (constant.Kind)
        {
            case TypedConstantKind.Primitive when constant.Value is string text:
                return SymbolDisplay.FormatLiteral(text, quote: true);
            case TypedConstantKind.Primitive when constant.Value is bool flag:
                return flag ? "true" : "false";
            case TypedConstantKind.Primitive when constant.Value is char character:
                return SymbolDisplay.FormatLiteral(character, quote: true);
            case TypedConstantKind.Primitive:
            case TypedConstantKind.Enum:
                return "(" + constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")(" + SymbolDisplay.FormatPrimitive(constant.Value!, quoteStrings: true, useHexadecimalNumbers: false) + ")";
            case TypedConstantKind.Type:
                return "typeof(" + ((ITypeSymbol)constant.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")";
            case TypedConstantKind.Array:
                return "new " + constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " { " + string.Join(", ", constant.Values.Select(Literal)) + " }";
            default:
                return "default";
        }
    }

    /// <summary>
    /// Where a package says a key without a marker is held, <c>[assembly: KeyWithoutMarker]</c>: the project's own
    /// assembly and every one it references, read once per compilation.
    /// </summary>
    private static List<(string Factory, string Method, string Assembly)> KeyWithoutMarkerIn(Compilation compilation)
        => KeysWithoutMarker.GetValue(compilation, static compilation =>
        {
            var found = new List<(string Factory, string Method, string Assembly)>();
            foreach (var assembly in new[] { compilation.Assembly }.Concat(compilation.SourceModule.ReferencedAssemblySymbols))
            {
                foreach (var attribute in assembly.GetAttributes())
                {
                    if (attribute.AttributeClass is { } attributeClass
                        && Is(attributeClass, KnownTypes.KeyWithoutMarkerAttribute)
                        && attribute.ConstructorArguments.Length == 2
                        && attribute.ConstructorArguments[0].Value is INamedTypeSymbol factory
                        && attribute.ConstructorArguments[1].Value is string method)
                    {
                        found.Add((factory.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), method, assembly.Identity.Name));
                    }
                }
            }

            return found;
        });

    /// <summary>
    /// The ids a member marked with <paramref name="marker"/> may be: the ids of the classes the project sees declared
    /// with the marker's template, or of what those classes name at its argument. <see langword="null"/> where it cannot
    /// tell: no such class, or one whose id is written by a generator this one does not see.
    /// </summary>
    private static HashSet<string>? IdsFor(Marker marker, Compilation compilation, CancellationToken cancellationToken)
    {
        if (marker.IdOf is not { } template)
        {
            return null;
        }

        var key = template.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var known = IdsOfTemplates.GetValue(compilation, static _ => new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal));
        var asked = key + "#" + marker.Argument + (marker.IdOfArgument ? "#id" : string.Empty);
        lock (known)
        {
            if (known.TryGetValue(asked, out var ids))
            {
                return ids;
            }
        }

        var classes = new List<INamedTypeSymbol>();
        var sources = SourceTemplateClasses.GetValue(compilation, static compilation =>
        {
            var found = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
            foreach (var type in DefinitionFactory.TypesIn(compilation.Assembly.GlobalNamespace, CancellationToken.None))
            {
                foreach (var attribute in type.GetAttributes())
                {
                    if (attribute.AttributeClass is { } attributeClass && EntityDeclarations.TemplateOf(attributeClass) is not null)
                    {
                        var definition = attributeClass.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (!found.TryGetValue(definition, out var list))
                        {
                            found.Add(definition, list = []);
                        }

                        list.Add(type);
                    }
                }
            }

            return found;
        });
        if (sources.TryGetValue(key, out var own))
        {
            classes.AddRange(own);
        }

        classes.AddRange(DefinitionFactory.ReferencedSources(compilation, EntityDeclarations.MetadataNameOf(template), cancellationToken)
            .Select(static source => source.Symbol)
            .OfType<INamedTypeSymbol>());

        HashSet<string>? result = new(StringComparer.Ordinal);
        foreach (var declared in classes)
        {
            var argument = EntityDeclarations.TemplateArgumentOf(declared, key, marker.Argument);
            var id = marker.IdOfArgument ? argument is INamedTypeSymbol named ? EntityDeclarations.IdArgumentOf(named) : null : argument;
            if (id is null || id.TypeKind == TypeKind.Error || !DefinitionFactory.IsEntityId(id))
            {
                // An id this generator cannot see, or one a generator writes for a class over a raw value: no telling.
                result = null;
                break;
            }

            result.Add(id.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        if (result is { Count: 0 })
        {
            result = null;
        }

        lock (known)
        {
            known[asked] = result;
        }

        return result;
    }

    /// <summary>A requirement attribute on a request: one a package marked, or <c>[RequiresKey]</c>.</summary>
    private sealed record Stated(AttributeData Attribute, INamedTypeSymbol Class, INamedTypeSymbol? Factory, string Method, bool QueriesOnly, bool IsKey);

    /// <summary>A marker on a member of a request, of where its key is held (<see cref="Place"/>) or of one more argument of that call.</summary>
    private sealed record Marker(
        INamedTypeSymbol Class,
        bool Place,
        INamedTypeSymbol? Factory,
        string Method,
        INamedTypeSymbol? GoesWith,
        INamedTypeSymbol? IdOf,
        int Argument,
        bool IdOfArgument)
    {
        public string Name { get; init; } = "";

        public ITypeSymbol Type { get; init; } = null!;

        public IPropertySymbol? Property { get; init; }

        public bool OnParameter { get; init; }

        public LocationInfo? Location { get; init; }
    }

    /// <summary>One request, read: what it says, what is wrong with it, and its line.</summary>
    private sealed class Reading(
        INamedTypeSymbol type,
        Compilation compilation,
        IPropertySymbol requiredAccess,
        List<INamedTypeSymbol> modules,
        List<Stated> stated,
        List<Marker> markers,
        SyntaxToken identifier,
        CancellationToken cancellationToken)
    {
        private readonly List<DiagnosticInfo> _diagnostics = [];
        private readonly LocationInfo? _location = LocationInfo.From(identifier);
        private readonly string _name = Written(type);

        public RequestRequirement Result()
        {
            var local = modules.Where(module => SymbolEqualityComparer.Default.Equals(module.ContainingAssembly, compilation.Assembly)).ToList();
            var writable = local.Where(IsWritable).ToList();
            var implementation = type.FindImplementationForInterfaceMember(requiredAccess);

            // What answers RequiredAccess for the request, where it is not a module's interface, the line's: the request
            // itself, a class it derives from, or an interface between it and the module's whose own default C# takes
            // over the module's, so a line written for the request would never be read.
            var answersItself = implementation is not null && !modules.Contains(implementation.ContainingType, SymbolEqualityComparer.Default);
            var hasAttributes = stated.Count > 0;

            if (hasAttributes)
            {
                StatedOnce(answersItself ? implementation!.ContainingType : null);
                ReadWhere(local, writable, answered: implementation is not null);
            }

            MarkedRightly();
            QueriesOnly();

            // An attribute the compiler could not bind, a missing using say, is its error to report, not this.
            var unbound = type.GetAttributes().Any(static attribute => attribute.AttributeClass is null or { TypeKind: TypeKind.Error });
            if (!hasAttributes && !answersItself && !unbound && !type.IsAbstract && type.TypeKind != TypeKind.Interface && modules.Count > 0 && !InheritsAStatedRequirement())
            {
                if (writable.Count > 0)
                {
                    Report(DiagnosticDescriptors.RequestStatesNoRequirement, _location, _name, writable[0].Name,
                        "it carries no requirement attribute, and does not answer RequiredAccess itself");
                }
                else if (implementation is { ContainingType: { TypeKind: TypeKind.Interface } declaring }
                         && !SymbolEqualityComparer.Default.Equals(declaring.ContainingAssembly, compilation.Assembly))
                {
                    Report(DiagnosticDescriptors.RequestStatesNoRequirement, _location, _name, declaring.Name,
                        "the interface is declared in '" + declaring.ContainingAssembly.Identity.Name + "', and the RequiredAccess written there knows only the requests of that project");
                }
            }

            var arm = _diagnostics.Count == 0 && hasAttributes && writable.Count > 0 ? Arm() : null;
            return new RequestRequirement(
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                DepthOf(type),
                (arm is null ? [] : writable.Select(static module => module.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))).ToEquatableArray(),
                arm,
                _diagnostics.ToEquatableArray());
        }

        /// <summary>A module interface of this project the generator writes the property into: partial, and of the shape a behavior is written for.</summary>
        private bool IsWritable(INamedTypeSymbol module)
            => AccessRequestsInterface.ProblemWith(module, compilation) is null
               && module.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken) is InterfaceDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

        /// <summary>DDD00079 and DDD00078: one requirement, said one way.</summary>
        /// <param name="answeredBy">What answers RequiredAccess for the request other than a line, or <see langword="null"/>.</param>
        private void StatedOnce(INamedTypeSymbol? answeredBy)
        {
            if (stated.Count > 1)
            {
                Report(DiagnosticDescriptors.RequestCarriesTwoRequirements, _location, _name, Listed(stated.Select(static each => Bracketed(each.Class))));
            }

            if (answeredBy is not null)
            {
                var through = SymbolEqualityComparer.Default.Equals(answeredBy, type) ? string.Empty : ", through '" + answeredBy.Name + "'";
                Report(DiagnosticDescriptors.RequestStatesRequirementTwice, _location, _name, Listed(stated.Select(static each => Bracketed(each.Class))), through);
            }
        }

        /// <summary>DDD00083: an interface the generator writes the line into, that can name the request.</summary>
        /// <param name="local">The module interfaces it implements that this project declares.</param>
        /// <param name="writable">Those of them the generator writes into.</param>
        /// <param name="answered">Whether anything answers RequiredAccess for it: the compiler finds no one answer where two interfaces each give one.</param>
        private void ReadWhere(List<INamedTypeSymbol> local, List<INamedTypeSymbol> writable, bool answered)
        {
            var carried = Listed(stated.Select(static each => Bracketed(each.Class)));
            if (type.TypeParameters.Length > 0 || type.ContainingType is { IsGenericType: true })
            {
                Report(DiagnosticDescriptors.RequirementAttributesNotRead, _location, _name, carried,
                    "it has type parameters, and the line its module's interface gets names one type; answer RequiredAccess instead");
                return;
            }

            if (writable.Count == 0)
            {
                var foreign = modules.FirstOrDefault(module => !local.Contains(module, SymbolEqualityComparer.Default));
                var reason = local.FirstOrDefault() is { } notPartial
                    ? AccessRequestsInterface.ProblemWith(notPartial, compilation) is null
                        ? "'" + notPartial.Name + "' is not partial, and the generator writes RequiredAccess into it from its requests' attributes; declare it '"
                          + (notPartial.DeclaredAccessibility == Accessibility.Public ? "public " : "internal ") + "partial interface " + notPartial.Name + " : IRequireAccess;'"
                        : "'" + notPartial.Name + "' is not an interface a behavior can be written for (DDD00056), so nothing is written into it"
                    : foreign is not null
                        ? "its module's interface '" + foreign.Name + "' is declared in '" + foreign.ContainingAssembly.Identity.Name + "', and the generator writes into an interface of the project it compiles; declare the request there, or answer RequiredAccess"
                        : "it implements no interface marked [AccessRequests], which is where the generator writes what it requires; implement the module's request interface";
                Report(DiagnosticDescriptors.RequirementAttributesNotRead, _location, _name, carried, reason);
                return;
            }

            if (writable.Count > 1 && !answered)
            {
                // Each interface implements the property with its own lines, and neither is more specific than the
                // other: the compiler's CS8705, which says what is ambiguous and not what to do.
                Report(DiagnosticDescriptors.RequirementAttributesNotRead, _location, _name, carried,
                    "it implements " + Listed(writable.Select(static module => "'" + module.Name + "'")) + ", each partial with RequiredAccess written into it, and C# takes neither's over the other's (CS8705); "
                    + "answer RequiredAccess itself, or implement one of them");
                return;
            }

            var hidden = writable.FirstOrDefault(module => !compilation.IsSymbolAccessibleWithin(type, module));
            if (hidden is not null)
            {
                Report(DiagnosticDescriptors.RequirementAttributesNotRead, _location, _name, carried,
                    "'" + hidden.Name + "' cannot name it, nested private as it is" + (type.ContainingType is { } outer ? " in '" + outer.Name + "'" : string.Empty)
                    + "; make it internal, or answer RequiredAccess");
            }
        }

        /// <summary>DDD00080 and DDD00081: markers that say one place for the key, on members the line can read, of the ids they mark.</summary>
        private void MarkedRightly()
        {
            var key = stated.FirstOrDefault(static each => each.IsKey);
            var places = markers.Where(static each => each.Place).ToList();
            var arguments = markers.Where(static each => !each.Place).ToList();

            if (key is null)
            {
                // An abstract request is a shape the requests that derive from it share, a record that carries the unit
                // say: each of them says its key, and is read on its own with the markers it inherits. A marker a base
                // that requires a key declares, or inherits, is read with that base's line.
                var unread = type.IsAbstract ? new List<Marker>() : markers.Where(marker => !ReadWithABase(marker)).ToList();
                if (unread.Count > 0)
                {
                    Report(DiagnosticDescriptors.KeyMarkerOutOfPlace, DeclaredHere(unread[0]) ? unread[0].Location ?? _location : _location, _name,
                        Listed(unread.Select(each => DeclaredHere(each)
                            ? "marks '" + each.Name + "' " + Bracketed(each.Class)
                            : "inherits '" + each.Name + "' " + Bracketed(each.Class) + " from '" + each.Property!.ContainingType.Name + "'"))
                        + ", and requires no key: a marker says where the key of [RequiresKey(key)] is held, so put that on the request, or take the marker off");
                }

                return;
            }

            if (places.Count > 1)
            {
                Report(DiagnosticDescriptors.KeyMarkerOutOfPlace, places[1].Location ?? _location, _name,
                    "marks where its key is held more than once, " + Listed(places.Select(static each => "'" + each.Name + "' " + Bracketed(each.Class)))
                    + ": a key is held in one place, so keep one marker; a key held in one place or another, depending on the request, is RequiredAccess");
                return;
            }

            var place = places.FirstOrDefault();
            foreach (var argument in arguments)
            {
                if (place is null || !SymbolEqualityComparer.Default.Equals(argument.GoesWith?.OriginalDefinition, place.Class.OriginalDefinition))
                {
                    Report(DiagnosticDescriptors.KeyMarkerOutOfPlace, argument.Location ?? _location, _name,
                        "marks '" + argument.Name + "' " + Bracketed(argument.Class) + ", which goes with " + (argument.GoesWith is { } with ? Bracketed(with) : "another marker")
                        + ", and marks no member " + (argument.GoesWith is { } goes ? Bracketed(goes) : "so") + ": the argument goes with the member it is of");
                    return;
                }
            }

            var twice = arguments.GroupBy(static each => each.Class.OriginalDefinition, SymbolEqualityComparer.Default).FirstOrDefault(static same => same.Count() > 1);
            if (twice is not null)
            {
                Report(DiagnosticDescriptors.KeyMarkerOutOfPlace, twice.ElementAt(1).Location ?? _location, _name,
                    "marks " + Listed(twice.Select(static each => "'" + each.Name + "'")) + " " + Bracketed(twice.First().Class) + ": the call takes it once");
                return;
            }

            foreach (var marker in markers)
            {
                if (marker.Property is null)
                {
                    Report(DiagnosticDescriptors.KeyMarkerOutOfPlace, marker.Location ?? _location, _name,
                        "marks the parameter '" + marker.Name + "' of its constructor " + Bracketed(marker.Class) + ", which is no property the requirement can read: mark the property instead");
                    return;
                }

                if (modules.FirstOrDefault(module => SymbolEqualityComparer.Default.Equals(module.ContainingAssembly, compilation.Assembly)) is { } module
                    && (!compilation.IsSymbolAccessibleWithin(marker.Property, module) || marker.Property.GetMethod is not { } getter || !compilation.IsSymbolAccessibleWithin(getter, module)))
                {
                    Report(DiagnosticDescriptors.KeyMarkerOutOfPlace, marker.Location ?? _location, _name,
                        "marks '" + marker.Name + "' " + Bracketed(marker.Class) + ", which '" + module.Name + "', where the requirement is written, cannot read: give it a public or internal getter");
                    return;
                }
            }

            if (place is null)
            {
                var defaults = KeyWithoutMarkerIn(compilation);
                if (defaults.Count != 1)
                {
                    Report(DiagnosticDescriptors.KeyMarkerOutOfPlace, _location, _name,
                        "requires a key and marks no member of where it is held, and " + (defaults.Count == 0
                            ? "no package this project references says where a key without a marker is held"
                            : Listed(defaults.Select(static each => "'" + each.Assembly + "'")) + " each say where a key without a marker is held")
                        + ": mark the member, [AtUnit] for a unit of Tenancy's or [OnResource] for a resource of Membership's");
                }

                return;
            }

            if (place.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                Report(DiagnosticDescriptors.KeyMarkerOnWrongType, place.Location ?? _location, _name, place.Name, Bracketed(place.Class).Trim('[', ']'),
                    "which is '" + Written(place.Type) + "' and may be null: a requirement computed from whether it is given is written as RequiredAccess, answering one case or the other, "
                    + nullable.TypeArguments[0].Name + " is { } given ? ... : ...");
                return;
            }

            if (place.Type.TypeKind != TypeKind.Error && IdsFor(place, compilation, cancellationToken) is { } ids
                && !ids.Contains(place.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            {
                var template = place.IdOf!.OriginalDefinition;
                var plural = ids.Count > 1;
                var of = place.Argument > 0 && place.Argument < template.TypeParameters.Length ? " of the " + template.TypeParameters[place.Argument].Name : string.Empty;
                Report(DiagnosticDescriptors.KeyMarkerOnWrongType, place.Location ?? _location, _name, place.Name, Bracketed(place.Class).Trim('[', ']'),
                    "which is a '" + Written(place.Type) + "': it marks " + Listed(ids.OrderBy(static id => id, StringComparer.Ordinal).Select(static id => "'" + TemplateRegistrations.LastNameOf(id) + "'"))
                    + ", the id" + (plural ? "s" : string.Empty) + of + " of the " + Bracketed(template) + " class" + (plural ? "es" : string.Empty) + " this project sees");
                return;
            }

            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                var parameter = place.Factory!.GetMembers(place.Method).OfType<IMethodSymbol>()
                    .Where(method => method.IsStatic && method.Parameters.Length >= 2 + arguments.Count)
                    .Select(method => method.Parameters[2 + index].Type)
                    .FirstOrDefault(static parameterType => !TemplateRegistrations.MentionsATypeParameter(parameterType));
                if (parameter is not null && argument.Type.TypeKind != TypeKind.Error && !compilation.ClassifyConversion(argument.Type, parameter).IsImplicit)
                {
                    Report(DiagnosticDescriptors.KeyMarkerOnWrongType, argument.Location ?? _location, _name, argument.Name, Bracketed(argument.Class).Trim('[', ']'),
                        "which is a '" + Written(argument.Type) + "': the call takes a '" + Written(parameter) + "' there");
                    return;
                }
            }
        }

        /// <summary>Whether the request declares <paramref name="marker"/> itself, on its constructor or a property of its own, and does not inherit it.</summary>
        private bool DeclaredHere(Marker marker)
            => marker.OnParameter || SymbolEqualityComparer.Default.Equals(marker.Property?.ContainingType, type);

        /// <summary>
        /// Whether an inherited marker is read with the line of a class the request derives from: one between the
        /// request and the class that declares the marker, that one included, requires a key, and its line reads it.
        /// </summary>
        private bool ReadWithABase(Marker marker)
        {
            if (DeclaredHere(marker) || marker.Property?.ContainingType is not { } declaring)
            {
                return false;
            }

            for (var current = type.BaseType; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                if (StatedOn(current).Any(static each => each.IsKey))
                {
                    return true;
                }

                if (SymbolEqualityComparer.Default.Equals(current, declaring))
                {
                    break;
                }
            }

            return false;
        }

        /// <summary>
        /// DDD00082: a requirement its package declares for queries only, on a message of the Mediator library that is
        /// no query: a command, or a plain request, which may change as much as a command does.
        /// </summary>
        private void QueriesOnly()
        {
            if (stated.All(static each => !each.QueriesOnly) || NoQuery() is not { } what)
            {
                return;
            }

            foreach (var only in stated.Where(static each => each.QueriesOnly))
            {
                Report(DiagnosticDescriptors.QueryRequirementOnCommand, _location, _name, Bracketed(only.Class).Trim('[', ']'), what);
            }
        }

        /// <summary>
        /// What the request is where it is a message of the Mediator library and no query of it, as a message says
        /// it: "a command", or "a request of Mediator's that is no query". <see langword="null"/> for a query, and
        /// where the project has no Mediator or the request is none of its messages: then nothing tells.
        /// </summary>
        private string? NoQuery()
        {
            var message = compilation.GetTypeByMetadataName(KnownTypes.MediatorMessage);
            var streamMessage = compilation.GetTypeByMetadataName(KnownTypes.MediatorStreamMessage);
            if (!Implements(message) && !Implements(streamMessage))
            {
                return null;
            }

            if (Implements(compilation.GetTypeByMetadataName(KnownTypes.MediatorBaseQuery)) || Implements(compilation.GetTypeByMetadataName(KnownTypes.MediatorBaseStreamQuery)))
            {
                return null;
            }

            return Implements(compilation.GetTypeByMetadataName(KnownTypes.MediatorBaseCommand)) || Implements(compilation.GetTypeByMetadataName(KnownTypes.MediatorBaseStreamCommand))
                ? "a command"
                : "a request of Mediator's that is no query";
        }

        private bool Implements(INamedTypeSymbol? interfaceType)
            => interfaceType is not null && type.AllInterfaces.Contains(interfaceType, SymbolEqualityComparer.Default);

        /// <summary>Whether a class it derives from states a requirement with attributes: that class's line matches this one too.</summary>
        private bool InheritsAStatedRequirement()
        {
            for (var current = type.BaseType; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                if (StatedOn(current).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The line: the pattern of the request's type, and the call that makes its requirement.</summary>
        private string Arm()
        {
            var pattern = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var only = stated[0];
            if (!only.IsKey)
            {
                var typeArguments = only.Class.TypeArguments.Length > 0
                    ? "<" + string.Join(", ", only.Class.TypeArguments.Select(static argument => argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ">"
                    : string.Empty;
                return pattern + " => " + only.Factory!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + Identifier(only.Method) + typeArguments
                       + "(" + string.Join(", ", only.Attribute.ConstructorArguments.Select(Literal)) + ")";
            }

            var key = only.Attribute.ConstructorArguments.Length > 0 ? Literal(only.Attribute.ConstructorArguments[0]) : "null";
            var place = markers.FirstOrDefault(static each => each.Place);
            if (place is null)
            {
                var (factory, method, _) = KeyWithoutMarkerIn(compilation)[0];
                return pattern + " => " + factory + "." + Identifier(method) + "(" + key + ")";
            }

            var arguments = new List<string> { key, Designation + "." + Identifier(place.Property!.Name) };
            arguments.AddRange(markers.Where(static each => !each.Place).Select(static each => Designation + "." + Identifier(each.Property!.Name)));
            return pattern + " " + Designation + " => " + place.Factory!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + Identifier(place.Method)
                   + "(" + string.Join(", ", arguments) + ")";
        }

        private void Report(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] arguments)
            => _diagnostics.Add(DiagnosticInfo.Create(descriptor, location, arguments));

        private static int DepthOf(INamedTypeSymbol type)
        {
            var depth = 0;
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                depth++;
            }

            return depth;
        }
    }
}
