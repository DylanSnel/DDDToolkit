using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
/// package says with <c>[assembly: KeyWithoutMarker]</c>. <c>[OrOwn]</c> on a member beside the key makes the caller's
/// own its alternative, and <c>[OwnOnly]</c> with <c>[Own]</c> on a member says the caller's own alone. Nothing here
/// knows a package's attribute by its name: the marks say which call to write, so a package of the application's own
/// declares attributes the same way. Attributes stacked on one request all hold, so the line is the call of each, in
/// the order they are written, inside <c>AccessRequirement.All(...)</c> where there are several:
/// <c>global::Shop.AddUnit request =&gt; global::...TenancyAccess.AtUnit("units.manage", request.Parent)</c>.
/// <para>
/// It is read from the compilation the request is part of, the classes the package's markers name included, so what it
/// reports is what the project sees. A type declared in parts is read once, at the first of its parts that may be a
/// request, whichever part carries the attributes or the interface.
/// </para>
/// <para>
/// A requirement attribute on the module's interface, <c>[AccessRequests, InTenant]</c>, is the module's default: a
/// request of it that states nothing of its own, by attribute, by answering the property or through a request it
/// derives from, gets a line with the default's call, <c>global::Shop.ListUnits =&gt; ...TenancyAccess.InTenant()</c>.
/// The default replaces nothing and stacks onto nothing a request states itself. The interface is read here too, for
/// what is wrong with its default (DDD00083, DDD00084, DDD00089), and gets no line of its own.
/// </para>
/// </remarks>
/// <param name="FullyQualifiedName">The request's name from <c>global::</c> on.</param>
/// <param name="Depth">How many classes it derives from: a line for a derived request comes before its base's.</param>
/// <param name="WrittenInto">The module interfaces of this project, partial, that the line goes into.</param>
/// <param name="Arm">The line, without its comma, or <see langword="null"/> where the request states nothing with attributes or cannot.</param>
/// <param name="Diagnostics">What is wrong with what the request says.</param>
/// <param name="OwnersAsked">
/// The ids, from <c>global::</c> on, of the aggregates whose owner the line asks about: <c>AccessRequirement.OwnerOf</c>
/// of a member marked <c>[Own]</c> or <c>[OrOwn]</c>. Empty where no line is written.
/// </param>
internal sealed record RequestRequirement(
    string FullyQualifiedName,
    int Depth,
    EquatableArray<string> WrittenInto,
    string? Arm,
    EquatableArray<DiagnosticInfo> Diagnostics,
    EquatableArray<string> OwnersAsked)
{
    /// <summary>The name a pattern binds the request to in its line.</summary>
    public const string Designation = "request";

    private static readonly ConditionalWeakTable<Compilation, List<KeyWithoutMarker>> KeysWithoutMarker = new();

    private static readonly ConditionalWeakTable<Compilation, Dictionary<string, HashSet<string>?>> IdsOfTemplates = new();

    private static readonly ConditionalWeakTable<Compilation, Dictionary<string, List<INamedTypeSymbol>>> SourceTemplateClasses = new();

    private static readonly ConditionalWeakTable<Compilation, OwnTypes> OwnTypesOf = new();

    private static readonly ConditionalWeakTable<IAssemblySymbol, string[]> HandlesByAssembly = new();

    /// <summary>
    /// A class, a record or a struct that implements something or carries an attribute: what may be a request; and an
    /// interface that carries an attribute, which may be a module's interface with a default.
    /// </summary>
    public static bool MayBeOne(SyntaxNode node, CancellationToken cancellationToken)
        => node switch
        {
            ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax => node is TypeDeclarationSyntax type && (type.BaseList is not null || type.AttributeLists.Count > 0),
            InterfaceDeclarationSyntax declaration => declaration.AttributeLists.Count > 0,
            _ => false,
        };

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

        var identifier = ((BaseTypeDeclarationSyntax)context.Node).Identifier;

        // An interface is no request: a requirement attribute on it is the default of a module, read for what is wrong
        // with it. One that carries none is nothing of this.
        if (type.TypeKind == TypeKind.Interface)
        {
            var defaults = StatedOn(type);
            return defaults.Count == 0
                ? null
                : new Reading(type, compilation, requiredAccess, [.. type.AllInterfaces.Where(static each => DefinitionFactory.HasAttribute(each, KnownTypes.AccessRequestsAttribute))], defaults, [], identifier, cancellationToken).AsADefault();
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

        // A request with attributes of its own holds to those of the requests it derives from as well, theirs first, as
        // several [Authorize] do on a controller and the one it derives from: what it carries adds to what its base
        // requires, and never takes its place. One without attributes of its own is matched by its base's line.
        if (stated.Count > 0)
        {
            stated = [.. InheritedBy(type), .. stated];
        }

        var reading = new Reading(type, compilation, requiredAccess, modules, stated, markers, identifier, cancellationToken);
        return reading.Result();
    }

    // ------------------------------------------------------------------ the default of a module

    /// <summary>
    /// The call the default of <paramref name="module"/> stands for, the requirement attributes on the interface, each
    /// call in the order they are written and <c>AccessRequirement.All</c> of them where there are several;
    /// <see langword="null"/> where the interface carries none, or one that may not be a default, which is reported at
    /// the interface.
    /// </summary>
    internal static string? DefaultOf(INamedTypeSymbol module, Compilation compilation)
    {
        var stated = StatedOn(module);
        if (stated.Count == 0
            || stated.Any(each => RefusedAsADefault(each) is not null)
            || (stated.Any(static each => each.IsKey) && KeyWithoutMarkerIn(compilation).Count != 1))
        {
            return null;
        }

        var calls = stated.Select(each => each.IsKey ? KeyWithoutAMarker(each, compilation) : CallOf(each)).ToList();
        return calls.Count == 1 ? calls[0] : KnownTypes.AccessRequirementUsage + ".All(" + string.Join(", ", calls) + ")";
    }

    /// <summary>The requirement attributes on a module's interface as a reader writes them: <c>[InTenant]</c>, <c>[SignedIn] and [InTenant]</c>.</summary>
    internal static string DefaultNamed(INamedTypeSymbol module) => Listed(StatedOn(module).Select(static each => Bracketed(each.Class)));

    /// <summary>
    /// Why a requirement attribute on a module's interface may not be its default, as a message goes on after naming it;
    /// <see langword="null"/> where it may. A default is taken by every request of the module that forgets its own, so
    /// what it lets through, a request that forgot lets through as well.
    /// </summary>
    private static string? RefusedAsADefault(Stated each)
    {
        if (each.IsAnonymous)
        {
            return "a request of the module that forgets to say what it requires would let anyone send it, and a forgotten requirement never opens a door; "
                   + "put [AllowAnonymous] on each request anyone may send";
        }

        if (each.Kind == StatedKind.OwnOnly)
        {
            return "it reads the member of a request marked [Own], and a default reads no member of a request; put [OwnOnly] on each request about the caller's own";
        }

        if (each.QueriesOnly)
        {
            return "its package declares it for queries only, and a command of the module that says nothing would take it too, where it holds the caller to nothing the command changes (DDD00082); "
                   + "put " + Bracketed(each.Class) + " on each query";
        }

        // AttributeUsage leaves the attribute off an interface with CS0592; this says what makes it a default.
        var usage = each.Class.OriginalDefinition.GetAttributes()
            .FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == "System.AttributeUsageAttribute");
        return usage is { ConstructorArguments.Length: > 0 } && usage.ConstructorArguments[0].Value is int targets && (targets & (int)AttributeTargets.Interface) == 0
            ? "the AttributeUsage of its class leaves out interfaces (CS0592), and a requirement attribute is a default where its class allows AttributeTargets.Interface; add it there, or put the attribute on each request"
            : null;
    }

    /// <summary>The call of a requirement attribute a package marked <c>[StatesRequirement]</c>: its method, with the attribute's arguments.</summary>
    private static string CallOf(Stated each)
    {
        var typeArguments = each.Class.TypeArguments.Length > 0
            ? "<" + string.Join(", ", each.Class.TypeArguments.Select(static argument => argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ">"
            : string.Empty;
        return each.Factory!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + Identifier(each.Method) + typeArguments
               + "(" + string.Join(", ", each.Attribute.ConstructorArguments.Select(Literal)) + ")";
    }

    /// <summary>The key of <c>[RequiresKey]</c>, as C# writes it.</summary>
    private static string KeyOf(Stated each)
        => each.Attribute.ConstructorArguments.Length > 0 ? Literal(each.Attribute.ConstructorArguments[0]) : "null";

    /// <summary>
    /// The call of <c>[RequiresKey]</c> where no member says where the key is held: the one a package says with
    /// <c>[assembly: KeyWithoutMarker]</c>, which there is one of, Tenancy's for the whole tenant.
    /// </summary>
    private static string KeyWithoutAMarker(Stated each, Compilation compilation)
    {
        var only = KeyWithoutMarkerIn(compilation)[0];
        return only.Factory + "." + Identifier(only.Method) + "(" + KeyOf(each) + ")";
    }

    /// <summary>
    /// The requirement attributes of the classes <paramref name="type"/> derives from, the furthest first, each in the
    /// order it is written, marked with the class that carries it.
    /// </summary>
    private static List<Stated> InheritedBy(INamedTypeSymbol type)
    {
        var levels = new List<List<Stated>>();
        for (var current = type.BaseType; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            var carried = current;
            levels.Add(StatedOn(current).Select(each => each with { InheritedFrom = carried }).ToList());
        }

        levels.Reverse();
        return levels.SelectMany(static level => level).ToList();
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

    /// <summary>
    /// The requirement attributes on the type, in the order they are written: those a package marked
    /// <c>[StatesRequirement]</c>, <c>[RequiresKey]</c> and <c>[OwnOnly]</c>.
    /// </summary>
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
                found.Add(new Stated(attribute, attributeClass, null, "", false, StatedKind.Key, ImmutableArray<INamedTypeSymbol>.Empty));
                continue;
            }

            if (Is(attributeClass, KnownTypes.OwnOnlyAttribute))
            {
                found.Add(new Stated(attribute, attributeClass, null, "", false, StatedKind.OwnOnly, ImmutableArray<INamedTypeSymbol>.Empty));
                continue;
            }

            var states = attributeClass.OriginalDefinition.GetAttributes().FirstOrDefault(static each => each.AttributeClass is { } meta && Is(meta, KnownTypes.StatesRequirementAttribute));
            if (states is { ConstructorArguments.Length: 2 }
                && states.ConstructorArguments[0].Value is INamedTypeSymbol factory
                && states.ConstructorArguments[1].Value is string method)
            {
                var queriesOnly = states.NamedArguments.Any(static named => named.Key == "QueriesOnly" && named.Value.Value is true);
                found.Add(new Stated(attribute, attributeClass, factory, method, queriesOnly, StatedKind.Call, ImpliesOf(states)));
            }
        }

        return found;
    }

    /// <summary>The requirement attributes a package's mark says its requirement already requires: its <c>Implies</c>.</summary>
    private static ImmutableArray<INamedTypeSymbol> ImpliesOf(AttributeData mark)
        => mark.NamedArguments.FirstOrDefault(static named => named.Key == "Implies").Value is { Kind: TypedConstantKind.Array, IsNull: false } implied
            ? implied.Values.Select(static each => each.Value).OfType<INamedTypeSymbol>().ToImmutableArray()
            : ImmutableArray<INamedTypeSymbol>.Empty;

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

        // The primary constructors, the request's own and those of the records it derives from that the project declares:
        // in a positional record a parameter is the property of its name, which the compiler declares, or which a record
        // it derives from has already and the compiler leaves to it; in a class or a struct it is no member at all. A
        // marker on the parameter of a record the request derives from is read with it, as one on its property is, so a
        // request that adds attributes of its own to its base's reads the base's markers as the base's line does.
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            var here = SymbolEqualityComparer.Default.Equals(current, type);
            if (!here && !current.IsRecord)
            {
                continue;
            }

            foreach (var constructor in current.InstanceConstructors)
            {
                if (!constructor.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax))
                {
                    continue;
                }

                foreach (var parameter in constructor.Parameters)
                {
                    foreach (var attribute in parameter.GetAttributes())
                    {
                        // A nearer record that marks the same parameter the same way says it already.
                        if (MarkerOf(attribute) is not { } marker
                            || (!here && found.Any(each => each.Name == parameter.Name && SymbolEqualityComparer.Default.Equals(each.Class.OriginalDefinition, marker.Class.OriginalDefinition))))
                        {
                            continue;
                        }

                        var property = current.IsRecord ? PropertyNamed(type, parameter.Name) : null;
                        found.Add(marker with { Name = parameter.Name, Type = parameter.Type, Property = property, OnParameter = here, Location = WhereWritten(attribute, cancellationToken) });
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

    /// <summary>
    /// A marker of where a key is held, of one more argument of such a marker's call, or of the caller's own; null for
    /// any other attribute.
    /// </summary>
    private static Marker? MarkerOf(AttributeData attribute)
    {
        if (attribute.AttributeClass is not { } attributeClass)
        {
            return null;
        }

        if (Is(attributeClass, KnownTypes.OrOwnAttribute))
        {
            return new Marker(attributeClass, MarkerKind.OrOwn, null, "", null, null, 0, false, ImmutableArray<INamedTypeSymbol>.Empty, null);
        }

        if (Is(attributeClass, KnownTypes.OwnAttribute))
        {
            return new Marker(attributeClass, MarkerKind.Own, null, "", null, null, 0, false, ImmutableArray<INamedTypeSymbol>.Empty, null);
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
                var ownAlternative = meta.NamedArguments.FirstOrDefault(static named => named.Key == "OwnAlternative").Value.Value is string { Length: > 0 } alternative ? alternative : null;
                return new Marker(attributeClass, MarkerKind.Place, factory, method, null, idOf, argument, idOfArgument, ImpliesOf(meta), ownAlternative);
            }

            if (Is(metaClass, KnownTypes.KeyMarkerArgumentAttribute)
                && meta.ConstructorArguments.Length == 1
                && meta.ConstructorArguments[0].Value is INamedTypeSymbol goesWith)
            {
                return new Marker(attributeClass, MarkerKind.Argument, null, "", goesWith, null, 0, false, ImmutableArray<INamedTypeSymbol>.Empty, null);
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
    private static List<KeyWithoutMarker> KeyWithoutMarkerIn(Compilation compilation)
        => KeysWithoutMarker.GetValue(compilation, static compilation =>
        {
            var found = new List<KeyWithoutMarker>();
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
                        found.Add(new KeyWithoutMarker(factory.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), method, assembly.Identity.Name, ImpliesOf(attribute)));
                    }
                }
            }

            return found;
        });

    /// <summary>
    /// The ids a member may be that is one of the classes the project sees declared with <paramref name="template"/>, or
    /// of what those classes name at <paramref name="argument"/>. <see langword="null"/> where it cannot tell: no such
    /// class, or one whose id is written by a generator this one does not see.
    /// </summary>
    private static HashSet<string>? IdsFor(INamedTypeSymbol template, int argument, bool idOfArgument, Compilation compilation, CancellationToken cancellationToken)
    {
        var key = template.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var known = IdsOfTemplates.GetValue(compilation, static _ => new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal));
        var asked = key + "#" + argument + (idOfArgument ? "#id" : string.Empty);
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
            var named = EntityDeclarations.TemplateArgumentOf(declared, key, argument);
            var id = idOfArgument ? named is INamedTypeSymbol entity ? EntityDeclarations.IdArgumentOf(entity) : null : named;
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


    /// <summary>
    /// What the project can tell of the types something says the caller's own of: those a package names with
    /// <c>[assembly: CallersOwn]</c>, by type or by the template of a class an application declares, with what the caller's
    /// own of each already requires, and those an <c>ICallersOwn&lt;TValue&gt;</c> of a concrete type is declared for, in the
    /// project or one it references. Read once per compilation, and a referenced assembly once.
    /// </summary>
    private static OwnTypes OwnTypesIn(Compilation compilation, CancellationToken cancellationToken)
        => OwnTypesOf.GetValue(compilation, static compilation =>
        {
            var exact = new Dictionary<string, ImmutableArray<INamedTypeSymbol>>(StringComparer.Ordinal);
            var templates = new List<OwnTemplate>();
            var handle = compilation.GetTypeByMetadataName(KnownTypes.CallersOwnInterface);

            foreach (var assembly in new[] { compilation.Assembly }.Concat(compilation.SourceModule.ReferencedAssemblySymbols))
            {
                foreach (var attribute in assembly.GetAttributes())
                {
                    if (attribute.AttributeClass is not { } attributeClass || !Is(attributeClass, KnownTypes.CallersOwnAttribute))
                    {
                        continue;
                    }

                    var implies = ImpliesOf(attribute);
                    if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is ITypeSymbol of)
                    {
                        exact[of.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = implies;
                    }

                    if (attribute.NamedArguments.FirstOrDefault(static named => named.Key == "IdOf").Value.Value is INamedTypeSymbol template)
                    {
                        templates.Add(new OwnTemplate(template, implies));
                    }
                }
            }

            if (handle is not null)
            {
                // An application's own handle says nothing of what its caller's own requires already: none is counted.
                foreach (var name in HandlesIn(compilation.Assembly, handle))
                {
                    if (!exact.ContainsKey(name))
                    {
                        exact.Add(name, ImmutableArray<INamedTypeSymbol>.Empty);
                    }
                }

                foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
                {
                    // Only an assembly that references the toolkit can declare a handle; the framework's are not looked into.
                    if (assembly.Modules.Any(module => module.ReferencedAssemblySymbols.Contains(handle.ContainingAssembly, SymbolEqualityComparer.Default)))
                    {
                        foreach (var name in HandlesByAssembly.GetValue(assembly, each => HandlesIn(each, handle)))
                        {
                            if (!exact.ContainsKey(name))
                    {
                        exact.Add(name, ImmutableArray<INamedTypeSymbol>.Empty);
                    }
                        }
                    }
                }
            }

            return new OwnTypes(exact, templates);
        });

    /// <summary>The types an <c>ICallersOwn&lt;TValue&gt;</c> of <paramref name="assembly"/> is declared for, over a type it names.</summary>
    private static string[] HandlesIn(IAssemblySymbol assembly, INamedTypeSymbol handle)
    {
        var found = new List<string>();
        foreach (var type in DefinitionFactory.TypesIn(assembly.GlobalNamespace, CancellationToken.None))
        {
            if (type.IsAbstract || type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                continue;
            }

            foreach (var implemented in type.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, handle)
                    && implemented.TypeArguments.Length == 1
                    && implemented.TypeArguments[0] is { TypeKind: not TypeKind.TypeParameter and not TypeKind.Error } value)
                {
                    found.Add(value.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
            }
        }

        return [.. found];
    }

    /// <summary>
    /// Whether something says the caller's own of <paramref name="type"/>: <see langword="true"/> or
    /// <see langword="false"/> where the project can tell, and <see langword="null"/> where a package names it by a
    /// template whose classes the project does not see.
    /// </summary>
    private static bool? HasOwn(ITypeSymbol type, Compilation compilation, CancellationToken cancellationToken)
    {
        var owned = OwnTypesIn(compilation, cancellationToken);
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (owned.Exact.ContainsKey(name))
        {
            return true;
        }

        var unknown = false;
        foreach (var template in owned.Templates)
        {
            if (IdsFor(template.Template, 0, false, compilation, cancellationToken) is not { } ids)
            {
                unknown = true;
            }
            else if (ids.Contains(name))
            {
                return true;
            }
        }

        return unknown ? null : false;
    }

    /// <summary>
    /// Whether no handle could ever say the caller's own of <paramref name="type"/>, whatever the project sees
    /// (<see cref="OwnerDeclarations.CannotHaveOwn"/>).
    /// </summary>
    private static bool CannotHaveOwn(ITypeSymbol type) => OwnerDeclarations.CannotHaveOwn(type);

    /// <summary>
    /// The requirement attributes the caller's own of <paramref name="type"/> already requires, as its package says with
    /// <c>Implies</c> on <c>[assembly: CallersOwn]</c>: none where nothing says.
    /// </summary>
    private static ImmutableArray<INamedTypeSymbol> ImpliedByOwn(ITypeSymbol type, Compilation compilation, CancellationToken cancellationToken)
    {
        var owned = OwnTypesIn(compilation, cancellationToken);
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (owned.Exact.TryGetValue(name, out var implies))
        {
            return implies;
        }

        foreach (var template in owned.Templates)
        {
            if (IdsFor(template.Template, 0, false, compilation, cancellationToken) is { } ids && ids.Contains(name))
            {
                return template.Implies;
            }
        }

        return ImmutableArray<INamedTypeSymbol>.Empty;
    }

    /// <summary>What a requirement attribute on a request is.</summary>
    private enum StatedKind
    {
        /// <summary>One a package marked <c>[StatesRequirement]</c>: its call.</summary>
        Call,

        /// <summary><c>[RequiresKey]</c>, held where a marker says.</summary>
        Key,

        /// <summary><c>[OwnOnly]</c>: the caller's own, the member marked <c>[Own]</c>.</summary>
        OwnOnly,
    }

    /// <summary>What a marker on a member of a request marks.</summary>
    private enum MarkerKind
    {
        /// <summary>Where the key of <c>[RequiresKey]</c> is held.</summary>
        Place,

        /// <summary>One more argument of the call of a marker of where.</summary>
        Argument,

        /// <summary>The caller's own, which the key is the alternative of: <c>[OrOwn]</c>.</summary>
        OrOwn,

        /// <summary>The caller's own, of an <c>[OwnOnly]</c> request: <c>[Own]</c>.</summary>
        Own,
    }

    /// <summary>A requirement attribute on a request: one a package marked, <c>[RequiresKey]</c> or <c>[OwnOnly]</c>.</summary>
    private sealed record Stated(AttributeData Attribute, INamedTypeSymbol Class, INamedTypeSymbol? Factory, string Method, bool QueriesOnly, StatedKind Kind, ImmutableArray<INamedTypeSymbol> Implies)
    {
        public bool IsKey => Kind == StatedKind.Key;

        public bool IsAnonymous => Is(Class, KnownTypes.AllowAnonymousAttribute);

        /// <summary>The class the request derives from that carries it, or <see langword="null"/> for one the request carries itself.</summary>
        public INamedTypeSymbol? InheritedFrom { get; init; }

        /// <summary>The attribute as a message names it: <c>[InTenant]</c>, or <c>[InTenant] of 'Report'</c> for one a base carries.</summary>
        public string Named => Bracketed(Class) + (InheritedFrom is { } from ? " of '" + from.Name + "'" : string.Empty);
    }

    /// <summary>
    /// A marker on a member of a request: of where its key is held (<see cref="MarkerKind.Place"/>), of one more argument
    /// of that call, or of the caller's own.
    /// </summary>
    private sealed record Marker(
        INamedTypeSymbol Class,
        MarkerKind Kind,
        INamedTypeSymbol? Factory,
        string Method,
        INamedTypeSymbol? GoesWith,
        INamedTypeSymbol? IdOf,
        int Argument,
        bool IdOfArgument,
        ImmutableArray<INamedTypeSymbol> Implies,
        string? OwnAlternative)
    {
        public bool Place => Kind == MarkerKind.Place;

        public bool IsOwn => Kind is MarkerKind.OrOwn or MarkerKind.Own;

        public string Name { get; init; } = "";

        public ITypeSymbol Type { get; init; } = null!;

        public IPropertySymbol? Property { get; init; }

        public bool OnParameter { get; init; }

        public LocationInfo? Location { get; init; }
    }

    /// <summary>Where a package says a key without a marker is held, and what that key requires already.</summary>
    private sealed record KeyWithoutMarker(string Factory, string Method, string Assembly, ImmutableArray<INamedTypeSymbol> Implies);

    /// <summary>
    /// The types something says the caller's own of, each with what its caller's own already requires: by name, and by the
    /// template of a class an application declares.
    /// </summary>
    private sealed record OwnTypes(Dictionary<string, ImmutableArray<INamedTypeSymbol>> Exact, List<OwnTemplate> Templates);

    /// <summary>A template whose classes' ids a package says the caller's own of, and what that own already requires.</summary>
    private sealed record OwnTemplate(INamedTypeSymbol Template, ImmutableArray<INamedTypeSymbol> Implies);

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

        /// <summary>
        /// The members marked <c>[Own]</c> or <c>[OrOwn]</c> that name the id of an aggregate whose owner it marks
        /// <c>[Owner]</c>, by name, each with that aggregate: the owner of the aggregate is the caller's own of the line.
        /// </summary>
        private readonly Dictionary<string, OwnedAggregate> _ownerOf = new(StringComparer.Ordinal);
        private readonly LocationInfo? _location = LocationInfo.From(identifier);
        private readonly string _name = Written(type);
        private int _errors;

        /// <summary>The markers of where the key is held and of the arguments of its call, without those of the caller's own.</summary>
        private IEnumerable<Marker> KeyMarkers => markers.Where(static each => !each.IsOwn);

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
            OwnMarkedRightly();
            QueriesOnly();
            Redundant();

            // An attribute the compiler could not bind, a missing using say, is its error to report, not this.
            var unbound = type.GetAttributes().Any(static attribute => attribute.AttributeClass is null or { TypeKind: TypeKind.Error });
            var statesNothing = !hasAttributes && !answersItself && !unbound && !type.IsAbstract && type.TypeKind != TypeKind.Interface && modules.Count > 0 && !InheritsAStatedRequirement();

            // A module with a default answers for a request that states nothing: on a line of its own where the interface
            // can name it, and in the last arm otherwise, a request of another project's included.
            if (statesNothing && !modules.Any(static module => StatedOn(module).Count > 0))
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

            // A warning says what reads oddly and leaves the line as it is; an error means no line, which nothing reads.
            string? arm = null;
            if (_errors == 0 && hasAttributes && writable.Count > 0)
            {
                arm = Arm(out var requirement);
                SameAsTheDefault(writable, requirement, answersItself);
            }
            else if (_errors == 0 && statesNothing && writable.Count == 1 && CanBeNamedBy(writable[0]) && DefaultOf(writable[0], compilation) is { } byDefault)
            {
                arm = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " => " + byDefault;
            }

            return new RequestRequirement(
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                DepthOf(type),
                (arm is null ? [] : writable.Select(static module => module.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))).ToEquatableArray(),
                arm,
                _diagnostics.ToEquatableArray(),
                (arm is null || writable.Count == 0 ? Enumerable.Empty<string>() : _ownerOf.Values.Select(static each => each.Id.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Distinct().OrderBy(static id => id, StringComparer.Ordinal)).ToEquatableArray());
        }

        /// <summary>A module interface of this project the generator writes the property into: partial, and of the shape a behavior is written for.</summary>
        private bool IsWritable(INamedTypeSymbol module)
            => AccessRequestsInterface.ProblemWith(module, compilation) is null
               && module.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken) is InterfaceDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

        /// <summary>
        /// Whether a line of <paramref name="module"/>'s switch can name the request: no type parameters, which a line
        /// cannot close, and visible from the interface. One that cannot takes the module's default in the last arm.
        /// </summary>
        private bool CanBeNamedBy(INamedTypeSymbol module)
            => type.TypeParameters.Length == 0
               && type.ContainingType is not { IsGenericType: true }
               && compilation.IsSymbolAccessibleWithin(type, module);

        /// <summary>
        /// DDD00084: requirement attributes of the request's own that say what its module's default says, call for call,
        /// in the same order, so the request would require the same without them. Only on a request nothing derives from:
        /// one that derives from it holds to its attributes beside its own, and would require less without them.
        /// </summary>
        private void SameAsTheDefault(List<INamedTypeSymbol> writable, string requirement, bool answersItself)
        {
            if (answersItself
                || writable.Count != 1
                || !(type.IsSealed || type.IsValueType)
                || stated.Any(static each => each.InheritedFrom is not null)
                || DefaultOf(writable[0], compilation) is not { } byDefault
                || !string.Equals(byDefault, requirement, StringComparison.Ordinal))
            {
                return;
            }

            Report(DiagnosticDescriptors.RedundantRequirement, WhereWritten(stated[0].Attribute, cancellationToken) ?? _location, _name,
                Listed(stated.Select(static each => Bracketed(each.Class))),
                "the default of its module, " + Listed(StatedOn(writable[0]).Select(static each => Bracketed(each.Class))) + " on '" + writable[0].Name + "'");
        }

        /// <summary>
        /// The interface read for its requirement attributes: on the module's interface, marked <c>[AccessRequests]</c> and
        /// partial, they are the default of its requests, and nothing is wrong with them where each may be one (DDD00089),
        /// a key without a marker is held where a package says (DDD00080), and none says what another beside it requires
        /// already (DDD00084). On any other interface nothing reads them (DDD00083). No line is written for an interface.
        /// </summary>
        public RequestRequirement AsADefault()
        {
            var carried = Listed(stated.Select(static each => Bracketed(each.Class)));
            if (!DefinitionFactory.HasAttribute(type, KnownTypes.AccessRequestsAttribute))
            {
                Report(DiagnosticDescriptors.RequirementAttributesNotRead, _location, _name, carried,
                    "a requirement attribute on an interface is the default of a module's requests, which only the module's interface has, the one marked [AccessRequests]"
                    + (modules.FirstOrDefault() is { } module ? ", '" + module.Name + "' here" : string.Empty)
                    + "; put it on each request of '" + type.Name + "', or answer RequiredAccess in '" + type.Name + "', which C# takes over the module's for its requests");
            }
            else if (AccessRequestsInterface.ProblemWith(type, compilation) is not null)
            {
                Report(DiagnosticDescriptors.RequirementAttributesNotRead, _location, _name, carried,
                    "'" + type.Name + "' is not an interface a behavior can be written for (DDD00056), so nothing is written into it");
            }
            else if (!IsWritable(type))
            {
                Report(DiagnosticDescriptors.RequirementAttributesNotRead, _location, _name, carried,
                    "'" + type.Name + "' is not partial, and the generator writes RequiredAccess into it, with its default for the requests that state none; declare it '"
                    + (type.DeclaredAccessibility == Accessibility.Public ? "public " : "internal ") + "partial interface " + type.Name + " : IRequireAccess;'");
            }
            else
            {
                foreach (var each in stated)
                {
                    if (RefusedAsADefault(each) is { } why)
                    {
                        Report(DiagnosticDescriptors.DefaultRequirementRefused, WhereWritten(each.Attribute, cancellationToken) ?? _location, _name, Bracketed(each.Class), why);
                    }
                }

                var defaults = KeyWithoutMarkerIn(compilation);
                if (stated.Any(static each => each.IsKey) && defaults.Count != 1)
                {
                    Report(DiagnosticDescriptors.KeyMarkerOutOfPlace, _location, _name,
                        "requires a key as the default of its module, and " + (defaults.Count == 0
                            ? "no package this project references says where a key without a marker is held"
                            : Listed(defaults.Select(static each => "'" + each.Assembly + "'")) + " each say where a key without a marker is held")
                        + ": a default reads no member of a request, so its key is one without a marker; put [RequiresKey(key)] on each request, with the marker of where it is held");
                }

                if (_errors == 0)
                {
                    Redundant();
                }
            }

            return new RequestRequirement(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), 0, [], null, _diagnostics.ToEquatableArray(), []);
        }

        /// <summary>
        /// What a message about a marker read by nothing adds where the request's module has a default with a key: that
        /// key is held where a key without a marker is, and reads no member of a request.
        /// </summary>
        private string KeyOfTheDefault()
        {
            var local = modules.Where(module => SymbolEqualityComparer.Default.Equals(module.ContainingAssembly, compilation.Assembly) && IsWritable(module)).ToList();
            return local.Count == 1 && StatedOn(local[0]).Any(static each => each.IsKey)
                ? "; the key of its module's default, on '" + local[0].Name + "', reads no member of a request"
                : string.Empty;
        }

        /// <summary>DDD00079 and DDD00078: no attribute that opens the door beside one that closes it, and the requirement said one way.</summary>
        /// <param name="answeredBy">What answers RequiredAccess for the request other than a line, or <see langword="null"/>.</param>
        private void StatedOnce(INamedTypeSymbol? answeredBy)
        {
            if (stated.FirstOrDefault(static each => each.IsAnonymous) is { } anonymous && stated.Count > 1)
            {
                // Where the request it derives from is the one that carries [AllowAnonymous], the message says so.
                var beside = Listed(stated.Where(static each => !each.IsAnonymous).Select(static each => each.Named))
                             + (anonymous.InheritedFrom is { } from ? ", deriving from '" + from.Name + "', which carries [AllowAnonymous]" : string.Empty);
                Report(DiagnosticDescriptors.AnonymousBesideARequirement, _location, _name, beside);
            }

            if (answeredBy is not null)
            {
                var through = SymbolEqualityComparer.Default.Equals(answeredBy, type) ? string.Empty : ", through '" + answeredBy.Name + "'";
                Report(DiagnosticDescriptors.RequestStatesRequirementTwice, _location, _name, Listed(stated.Where(static each => each.InheritedFrom is null).Select(static each => Bracketed(each.Class))), through);
            }
        }

        /// <summary>DDD00083: an interface the generator writes the line into, that can name the request.</summary>
        /// <param name="local">The module interfaces it implements that this project declares.</param>
        /// <param name="writable">Those of them the generator writes into.</param>
        /// <param name="answered">Whether anything answers RequiredAccess for it: the compiler finds no one answer where two interfaces each give one.</param>
        private void ReadWhere(List<INamedTypeSymbol> local, List<INamedTypeSymbol> writable, bool answered)
        {
            var carried = Listed(stated.Where(static each => each.InheritedFrom is null).Select(static each => Bracketed(each.Class)));
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
            var keys = stated.Where(static each => each.IsKey).ToList();
            if (keys.Count > 1)
            {
                // A request and the one it derives from each requiring a key: both would hold, and one marker says where
                // for one key, so which key it says where of could not be told.
                Report(DiagnosticDescriptors.KeyMarkerOutOfPlace, _location, _name,
                    "requires two keys, " + Listed(keys.Select(static each => each.Named)) + ": the requirement attributes of a request and of the one it derives from all hold, "
                    + "and a marker says where one key is held; two keys are written as RequiredAccess, AccessRequirement.All of the two");
                return;
            }

            var keyMarkers = KeyMarkers.ToList();
            var places = keyMarkers.Where(static each => each.Place).ToList();
            var arguments = keyMarkers.Where(static each => each.Kind == MarkerKind.Argument).ToList();

            if (key is null)
            {
                // An abstract request is a shape the requests that derive from it share, a record that carries the unit
                // say: each of them says its key, and is read on its own with the markers it inherits. A marker a base
                // that requires a key declares, or inherits, is read with that base's line.
                var unread = type.IsAbstract ? new List<Marker>() : keyMarkers.Where(marker => !ReadWithABase(marker, StatedKind.Key)).ToList();
                if (unread.Count > 0)
                {
                    Report(DiagnosticDescriptors.KeyMarkerOutOfPlace, DeclaredHere(unread[0]) ? unread[0].Location ?? _location : _location, _name,
                        Listed(unread.Select(each => DeclaredHere(each)
                            ? "marks '" + each.Name + "' " + Bracketed(each.Class)
                            : "inherits '" + each.Name + "' " + Bracketed(each.Class) + " from '" + each.Property!.ContainingType.Name + "'"))
                        + ", and requires no key: a marker says where the key of [RequiresKey(key)] is held, so put that on the request, or take the marker off"
                        + KeyOfTheDefault());
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

            if (Unreadable(keyMarkers, DiagnosticDescriptors.KeyMarkerOutOfPlace))
            {
                return;
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

            if (place.Type.TypeKind != TypeKind.Error && place.IdOf is { } template && IdsFor(template, place.Argument, place.IdOfArgument, compilation, cancellationToken) is { } ids
                && !ids.Contains(place.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            {
                var definition = template.OriginalDefinition;
                var plural = ids.Count > 1;
                var of = place.Argument > 0 && place.Argument < definition.TypeParameters.Length ? " of the " + definition.TypeParameters[place.Argument].Name : string.Empty;
                Report(DiagnosticDescriptors.KeyMarkerOnWrongType, place.Location ?? _location, _name, place.Name, Bracketed(place.Class).Trim('[', ']'),
                    "which is a '" + Written(place.Type) + "': it marks " + Listed(ids.OrderBy(static id => id, StringComparer.Ordinal).Select(static id => "'" + TemplateRegistrations.LastNameOf(id) + "'"))
                    + ", the id" + (plural ? "s" : string.Empty) + of + " of the " + Bracketed(definition) + " class" + (plural ? "es" : string.Empty) + " this project sees");
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

        /// <summary>
        /// DDD00085 and DDD00086: <c>[OrOwn]</c> beside a key that may have it, <c>[Own]</c> with <c>[OwnOnly]</c>, one of each
        /// at most, on a member the line can read, of a type something says the caller's own of.
        /// </summary>
        private void OwnMarkedRightly()
        {
            var orOwn = markers.Where(static each => each.Kind == MarkerKind.OrOwn).ToList();
            var own = markers.Where(static each => each.Kind == MarkerKind.Own).ToList();
            var ownOnly = stated.FirstOrDefault(static each => each.Kind == StatedKind.OwnOnly);
            var key = stated.Any(static each => each.IsKey);

            if (ownOnly is not null && own.Count == 0)
            {
                Report(DiagnosticDescriptors.OwnMarkerOutOfPlace, _location, _name,
                    "carries [OwnOnly] and marks no member [Own]: [OwnOnly] says the request is about the caller's own, so mark the member that names it, [Own] SeatId Seat say");
                return;
            }

            if (orOwn.Concat(own).GroupBy(static each => each.Name, StringComparer.Ordinal).FirstOrDefault(static same => same.Select(static each => each.Kind).Distinct().Count() > 1) is { } both)
            {
                Report(DiagnosticDescriptors.OwnMarkerOutOfPlace, both.First().Location ?? _location, _name,
                    "marks '" + both.Key + "' [Own] and [OrOwn]: [Own] says the caller's own alone and [OrOwn] the caller's own or the key, so keep the one it means");
                return;
            }

            if (!type.IsAbstract)
            {
                var unread = orOwn.Where(marker => !key && !ReadWithABase(marker, StatedKind.Key)).Select(marker => (marker, "requires no key: [OrOwn] makes the caller's own the alternative of the key of [RequiresKey(key)], so put that on the request; for the caller's own alone, put [OwnOnly] on the request and mark the member [Own]"))
                    .Concat(own.Where(marker => ownOnly is null && !ReadWithABase(marker, StatedKind.OwnOnly)).Select(marker => (marker, "is not [OwnOnly]: [Own] names the caller's own of a request about it alone, so put [OwnOnly] on the request; for the caller's own or a key, mark it [OrOwn] beside [RequiresKey(key)]")))
                    .ToList();
                if (unread.Count > 0)
                {
                    var (marker, why) = unread[0];
                    Report(DiagnosticDescriptors.OwnMarkerOutOfPlace, DeclaredHere(marker) ? marker.Location ?? _location : _location, _name,
                        (DeclaredHere(marker)
                            ? "marks '" + marker.Name + "' " + Bracketed(marker.Class)
                            : "inherits '" + marker.Name + "' " + Bracketed(marker.Class) + " from '" + marker.Property!.ContainingType.Name + "'")
                        + ", and " + why);
                    return;
                }
            }

            foreach (var kind in new[] { orOwn, own })
            {
                if (kind.Count > 1)
                {
                    Report(DiagnosticDescriptors.OwnMarkerOutOfPlace, kind[1].Location ?? _location, _name,
                        "marks the caller's own more than once, " + Listed(kind.Select(static each => "'" + each.Name + "' " + Bracketed(each.Class)))
                        + ": the caller's own is one value, which the check compares with the caller's; a request about one of two computes its requirement as RequiredAccess");
                    return;
                }
            }

            if (orOwn.Count > 0 && key && KeyMarkers.FirstOrDefault(static each => each.Place) is { OwnAlternative: null } place)
            {
                Report(DiagnosticDescriptors.OwnMarkerOutOfPlace, orOwn[0].Location ?? _location, _name,
                    "marks '" + orOwn[0].Name + "' [OrOwn] beside '" + place.Name + "' " + Bracketed(place.Class)
                    + ", which says where the key is checked and not what the request acts on: the caller would send one where it holds the key for another subject, so a key held there would act on any; compute the requirement as RequiredAccess where the request needs it");
                return;
            }

            var marked = orOwn.Concat(own).ToList();
            if (marked.Count == 0 || Unreadable(marked, DiagnosticDescriptors.OwnMarkerOutOfPlace))
            {
                return;
            }

            foreach (var marker in marked)
            {
                if (marker.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
                {
                    Report(DiagnosticDescriptors.OwnOnATypeWithoutOwn, marker.Location ?? _location, _name, marker.Name, Bracketed(marker.Class).Trim('[', ']'),
                        "which is '" + Written(marker.Type) + "' and may be null: where it is left out the request names no value of the caller's, so compute the requirement from whether it is given, as RequiredAccess");
                    return;
                }

                // Whatever the project sees: the case compares a value that equals itself, so a class, a string or an enum
                // never has an own, and the line written for it would not compile.
                if (CannotHaveOwn(marker.Type))
                {
                    var written = Written(marker.Type);
                    Report(DiagnosticDescriptors.OwnOnATypeWithoutOwn, marker.Location ?? _location, _name, marker.Name, Bracketed(marker.Class).Trim('[', ']'),
                        "which is a '" + written + "': the caller's own is a value compared with the caller's at the door, a struct that is IEquatable of itself, such as a Guid or an id declared with [EntityId], "
                        + "so nothing can say the caller's own " + written + "; mark the member that names the caller's id");
                    return;
                }

                var hasOwn = marker.Type.TypeKind == TypeKind.Error ? null : HasOwn(marker.Type, compilation, cancellationToken);
                if (ReadAsAnOwner(marker, hasOwn) is { } read)
                {
                    if (!read)
                    {
                        return;
                    }

                    continue;
                }

                if (hasOwn == false)
                {
                    var written = Written(marker.Type);
                    var aggregate = DefinitionFactory.IsEntityId(marker.Type) ? OwnerDeclarations.AggregatesWithId(marker.Type, compilation).FirstOrDefault() : null;
                    Report(DiagnosticDescriptors.OwnOnATypeWithoutOwn, marker.Location ?? _location, _name, marker.Name, Bracketed(marker.Class).Trim('[', ']'),
                        "which is a '" + written + "': nothing says what the caller's own " + written + " is. A Guid is the caller's verified identity and Tenancy's seat id the caller's seat; for a kind of the application's own, "
                        + "declare a small ICallersOwn<" + written + "> beside the requests and register it with services.AddCallersOwn<" + written + ", ...>()"
                        + (aggregate is null ? string.Empty : "; for the id of '" + aggregate.Aggregate.Name + "', a request about its owner, mark the property of '" + aggregate.Aggregate.Name + "' that names its owner [Owner]"));
                    return;
                }
            }
        }

        /// <summary>
        /// Reads <paramref name="marker"/> as the id of an aggregate whose owner it marks <c>[Owner]</c>, where nothing says the
        /// caller's own of the id itself: the line is then <c>AccessRequirement.OwnerOf</c> of the member, and the owner's own
        /// is the caller's own the request is about. <see langword="null"/> where the member is no such id, so it is read as
        /// a value of the caller's own; <see langword="true"/> where it is one and is read so; <see langword="false"/> where
        /// it is one and is reported (DDD00087, DDD00088), and nothing is written.
        /// </summary>
        /// <remarks>
        /// The caller's own of the id wins where something says it, an <c>ICallersOwn</c> of the id's own, or a package's
        /// template the aggregate is declared with, as Tenancy's seat: that one reads nothing, and is what the id says first.
        /// </remarks>
        /// <param name="marker">The member marked <c>[Own]</c> or <c>[OrOwn]</c>.</param>
        /// <param name="hasOwn">Whether something says the caller's own of the member's type, where the project can tell.</param>
        private bool? ReadAsAnOwner(Marker marker, bool? hasOwn)
        {
            if (hasOwn == true || marker.Type.TypeKind == TypeKind.Error || !DefinitionFactory.IsEntityId(marker.Type))
            {
                return null;
            }

            var owned = OwnerDeclarations.AggregatesWithId(marker.Type, compilation).Where(static each => each.Owner is not null).ToList();
            var templates = OwnTypesIn(compilation, cancellationToken).Templates;
            if (owned.Count == 0 || owned.Any(each => templates.Any(template => DeclaredWith(each.Aggregate, template.Template))))
            {
                return null;
            }

            var marked = "marks '" + marker.Name + "' " + Bracketed(marker.Class);
            if (owned.Count > 1)
            {
                Report(DiagnosticDescriptors.OwnerOutOfPlace, marker.Location ?? _location, _name,
                    marked + ", the id of " + Listed(owned.Select(static each => "'" + each.Aggregate.Name + "'")) + ", which each mark an [Owner]: which aggregate's owner the request is about cannot be told, so give each aggregate an id of its own");
                return false;
            }

            var aggregate = owned[0];
            var owner = aggregate.Owner!;
            var ownerType = OwnerDeclarations.Unwrapped(owner.Type);
            var written = Written(ownerType);
            if (ownerType.TypeKind != TypeKind.Error && (CannotHaveOwn(ownerType) || HasOwn(ownerType, compilation, cancellationToken) == false))
            {
                Report(DiagnosticDescriptors.OwnerOfATypeWithoutOwn, marker.Location ?? _location, _name,
                    marked + ", the id of '" + aggregate.Aggregate.Name + "', whose [Owner] '" + owner.Name + "' is a '" + written + "': " + (CannotHaveOwn(ownerType)
                        ? "the owner is compared with the caller's own of its type, a struct that is IEquatable of itself, so nothing can say the caller's own " + written
                        : "nothing says what the caller's own " + written + " is. A Guid is the caller's verified identity and Tenancy's seat id the caller's seat; for a kind of the application's own, "
                          + "declare a small ICallersOwn<" + written + "> beside the requests and register it with services.AddCallersOwn<" + written + ", ...>()"));
                return false;
            }

            _ownerOf[marker.Name] = aggregate;
            return true;
        }

        /// <summary>Whether <paramref name="aggregate"/> is declared with the template attribute <paramref name="template"/>.</summary>
        private static bool DeclaredWith(INamedTypeSymbol aggregate, INamedTypeSymbol template)
            => aggregate.OriginalDefinition.GetAttributes().Any(attribute => attribute.AttributeClass is { } attributeClass
                && SymbolEqualityComparer.Default.Equals(attributeClass.OriginalDefinition, template.OriginalDefinition));

        /// <summary>
        /// The type whose caller's own the member names: its own, or, for the id of an aggregate whose owner it marks, the
        /// owner's (<see cref="ReadAsAnOwner"/>).
        /// </summary>
        private ITypeSymbol OwnTypeOf(Marker marker)
            => _ownerOf.TryGetValue(marker.Name, out var aggregate) ? OwnerDeclarations.Unwrapped(aggregate.Owner!.Type) : marker.Type;

        /// <summary>
        /// Reports the first of <paramref name="marked"/> the line cannot read: a parameter of a class's primary
        /// constructor, which is no property, and a property the module's interface cannot read.
        /// </summary>
        private bool Unreadable(List<Marker> marked, DiagnosticDescriptor descriptor)
        {
            foreach (var marker in marked)
            {
                if (marker.Property is null)
                {
                    Report(descriptor, marker.Location ?? _location, _name,
                        "marks the parameter '" + marker.Name + "' of its constructor " + Bracketed(marker.Class) + ", which is no property the requirement can read: mark the property instead");
                    return true;
                }

                if (modules.FirstOrDefault(module => SymbolEqualityComparer.Default.Equals(module.ContainingAssembly, compilation.Assembly)) is { } module
                    && (!compilation.IsSymbolAccessibleWithin(marker.Property, module) || marker.Property.GetMethod is not { } getter || !compilation.IsSymbolAccessibleWithin(getter, module)))
                {
                    Report(descriptor, marker.Location ?? _location, _name,
                        "marks '" + marker.Name + "' " + Bracketed(marker.Class) + ", which '" + module.Name + "', where the requirement is written, cannot read: give it a public or internal getter");
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether the request declares <paramref name="marker"/> itself, on its constructor or a property of its own, and does not inherit it.</summary>
        private bool DeclaredHere(Marker marker)
            => marker.OnParameter || SymbolEqualityComparer.Default.Equals(marker.Property?.ContainingType, type);

        /// <summary>
        /// Whether an inherited marker is read with the line of a class the request derives from: one between the
        /// request and the class that declares the marker, that one included, carries the attribute that reads it, and
        /// its line reads it.
        /// </summary>
        private bool ReadWithABase(Marker marker, StatedKind readBy)
        {
            if (DeclaredHere(marker) || marker.Property?.ContainingType is not { } declaring)
            {
                return false;
            }

            for (var current = type.BaseType; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                if (StatedOn(current).Any(each => each.Kind == readBy))
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
        /// DDD00084: a requirement attribute the request carries that another beside it already requires, as the other's
        /// package says with <c>Implies</c>, or that the request it derives from carries already. The caller's own of
        /// <c>[OwnOnly]</c> requires what its package says the own of the member's type requires, <c>[assembly:
        /// CallersOwn(..., Implies = ...)]</c>: a sign-in for a <see cref="Guid"/>, a seat in the tenant for Tenancy's seat
        /// id. A key with <c>[OrOwn]</c> lets either through, so only what both the key and the caller's own require is
        /// counted for it.
        /// </summary>
        private void Redundant()
        {
            if (stated.Count < 2)
            {
                return;
            }

            var implied = new List<(INamedTypeSymbol Implied, string By)>();
            foreach (var each in stated)
            {
                switch (each.Kind)
                {
                    case StatedKind.Call:
                        implied.AddRange(each.Implies.Select(implies => (implies, each.Named)));
                        break;

                    case StatedKind.OwnOnly when markers.FirstOrDefault(static marker => marker.Kind == MarkerKind.Own) is { } own:
                        implied.AddRange(ImpliedByOwn(OwnTypeOf(own), compilation, cancellationToken).Select(implies => (implies, each.Named + " with [Own] '" + own.Name + "'")));
                        break;

                    case StatedKind.Key:
                        var byKey = new List<(INamedTypeSymbol Implied, string By)>();
                        if (KeyMarkers.FirstOrDefault(static marker => marker.Place) is { } place)
                        {
                            byKey.AddRange(place.Implies.Select(implies => (implies, each.Named + " with " + Bracketed(place.Class))));
                        }
                        else if (KeyWithoutMarkerIn(compilation) is { Count: 1 } defaults)
                        {
                            byKey.AddRange(defaults[0].Implies.Select(implies => (implies, each.Named)));
                        }

                        if (markers.FirstOrDefault(static marker => marker.Kind == MarkerKind.OrOwn) is { } orOwn)
                        {
                            // Either way through lets the caller in, so only what both require is required.
                            var byOwn = ImpliedByOwn(OwnTypeOf(orOwn), compilation, cancellationToken);
                            byKey = byKey
                                .Where(pair => byOwn.Any(implies => SymbolEqualityComparer.Default.Equals(implies.OriginalDefinition, pair.Implied.OriginalDefinition)))
                                .Select(pair => (pair.Implied, pair.By + " and [OrOwn] '" + orOwn.Name + "'"))
                                .ToList();
                        }

                        implied.AddRange(byKey);
                        break;
                }
            }

            foreach (var each in stated.Where(static each => each.InheritedFrom is null && each.Kind != StatedKind.Key))
            {
                var by = implied.FirstOrDefault(pair => SymbolEqualityComparer.Default.Equals(pair.Implied.OriginalDefinition, each.Class.OriginalDefinition)).By;
                if (by is null && stated.FirstOrDefault(other => other.InheritedFrom is not null && SymbolEqualityComparer.Default.Equals(other.Class.OriginalDefinition, each.Class.OriginalDefinition)) is { InheritedFrom: { } from })
                {
                    by = "'" + from.Name + "' it derives from";
                }

                if (by is not null)
                {
                    Report(DiagnosticDescriptors.RedundantRequirement, WhereWritten(each.Attribute, cancellationToken) ?? _location, _name, Bracketed(each.Class), by);
                }
            }
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

        /// <summary>
        /// The line: the pattern of the request's type, and the call that makes its requirement. Several attributes all
        /// hold, so their calls go into <c>AccessRequirement.All</c>, in the order the attributes are written.
        /// </summary>
        private string Arm(out string requirement)
        {
            var pattern = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var reads = false;
            var calls = new List<string>(stated.Count);
            foreach (var each in stated)
            {
                calls.Add(CallOf(each, ref reads));
            }

            requirement = calls.Count == 1 ? calls[0] : KnownTypes.AccessRequirementUsage + ".All(" + string.Join(", ", calls) + ")";
            return pattern + (reads ? " " + Designation : string.Empty) + " => " + requirement;
        }

        /// <summary>The call one requirement attribute stands for; <paramref name="reads"/> is set where it reads the request.</summary>
        private string CallOf(Stated each, ref bool reads)
        {
            switch (each.Kind)
            {
                case StatedKind.OwnOnly:
                    reads = true;
                    return Own(markers.First(static marker => marker.Kind == MarkerKind.Own));

                case StatedKind.Key:
                    var key = KeyOf(each);
                    var place = KeyMarkers.FirstOrDefault(static marker => marker.Place);
                    string held;
                    string? there = null;
                    if (place is null)
                    {
                        held = KeyWithoutAMarker(each, compilation);
                    }
                    else
                    {
                        reads = true;
                        var arguments = new List<string> { key, Designation + "." + Identifier(place.Property!.Name) };
                        arguments.AddRange(KeyMarkers.Where(static marker => marker.Kind == MarkerKind.Argument).Select(static marker => Designation + "." + Identifier(marker.Property!.Name)));
                        var factory = place.Factory!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        held = factory + "." + Identifier(place.Method) + "(" + string.Join(", ", arguments) + ")";

                        // What the caller's own way through still meets where the marker says, as its package names it:
                        // the resource seen, read and kept, on Membership's.
                        there = place.OwnAlternative is { } alternative ? factory + "." + Identifier(alternative) + "(" + string.Join(", ", arguments) + ")" : null;
                    }

                    // The caller's own first, since it reads nothing; the key only where it is not.
                    if (markers.FirstOrDefault(static marker => marker.Kind == MarkerKind.OrOwn) is { } orOwn)
                    {
                        reads = true;
                        var own = there is null ? Own(orOwn) : KnownTypes.AccessRequirementUsage + ".All(" + Own(orOwn) + ", " + there + ")";
                        return KnownTypes.AccessRequirementUsage + ".Any(" + own + ", " + held + ")";
                    }

                    return held;

                default:
                    return RequestRequirement.CallOf(each);
            }
        }

        /// <summary>
        /// The core's case of the caller's own, of the member <paramref name="marker"/> marks: the value itself, or, for the id
        /// of an aggregate whose owner it marks, the owner of that aggregate.
        /// </summary>
        private string Own(Marker marker)
            => KnownTypes.AccessRequirementUsage + (_ownerOf.ContainsKey(marker.Name) ? ".OwnerOf(" : ".Own(") + Designation + "." + Identifier(marker.Property!.Name) + ")";

        private void Report(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] arguments)
        {
            if (descriptor.DefaultSeverity == DiagnosticSeverity.Error)
            {
                _errors++;
            }

            _diagnostics.Add(DiagnosticInfo.Create(descriptor, location, arguments));
        }

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
