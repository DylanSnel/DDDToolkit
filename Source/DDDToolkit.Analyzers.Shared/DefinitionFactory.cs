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
/// Turns the symbols Roslyn hands us into plain, equatable definition records. Everything the
/// emitters need is computed here so the output step never touches the semantic model.
/// </summary>
internal static class DefinitionFactory
{
    private static readonly SymbolDisplayFormat FullyQualifiedWithNullability =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly HashSet<SyntaxKind> AccessibilityModifiers = new()
    {
        SyntaxKind.PublicKeyword,
        SyntaxKind.InternalKeyword,
        SyntaxKind.ProtectedKeyword,
        SyntaxKind.PrivateKeyword,
        SyntaxKind.FileKeyword,
    };

    private static readonly Dictionary<string, CollectionBacking> CollectionInterfaces = new(StringComparer.Ordinal)
    {
        ["System.Collections.Generic.IReadOnlyList<T>"] = CollectionBacking.List,
        ["System.Collections.Generic.IReadOnlyCollection<T>"] = CollectionBacking.List,
        ["System.Collections.Generic.IEnumerable<T>"] = CollectionBacking.List,
        ["System.Collections.Generic.IReadOnlySet<T>"] = CollectionBacking.HashSet,
    };

    /// <summary>
    /// Whether a partial collection property of this type is one the entity generator keeps in a
    /// <c>List&lt;T&gt;</c>: for a package's own generator that writes code over that list.
    /// </summary>
    public static bool IsBackedByAList(ITypeSymbol collectionType)
        => collectionType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
           && CollectionInterfaces.TryGetValue(named.OriginalDefinition.ToDisplayString(), out var backing)
           && backing == CollectionBacking.List;

    // ------------------------------------------------------------------ entity ids

    public static EntityIdDefinition CreateEntityId(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;
        var attribute = context.Attributes[0];
        var compilation = context.SemanticModel.Compilation;

        var type = CreateTypeInfo(symbol, syntax);
        var diagnostics = new List<DiagnosticInfo>();
        var canGenerate = true;

        if (!type.IsRecord)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.EntityIdShouldBeRecord, type.Location, type.Name));
            canGenerate = false;
        }

        if (!type.IsPartial)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TypeShouldBePartial, type.Location, type.Name, "EntityId"));
            canGenerate = false;
        }

        if (type.Kind == DeclarationKind.RecordClass && type.IsSealed)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.ValueObjectsCantBeSealed, type.Location, type.Name));
            canGenerate = false;
        }

        if (type.IsGeneric)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TypeCannotBeGeneric, type.Location, type.Name, "EntityId"));
            canGenerate = false;
        }

        if (type.Kind == DeclarationKind.RecordStruct && !type.IsReadOnly)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.EntityIdStructShouldBeReadonly, type.Location, type.Name));
        }

        var valueType = GetTypeArgument(attribute, compilation);

        return new EntityIdDefinition(
            Type: type,
            Value: CreateValueTypeInfo(valueType),
            Prefix: GetArgument(attribute, "Prefix", string.Empty),
            ColumnLength: GetArgument(attribute, "ColumnLength", -1),
            GraphQLSchemaType: GetGraphQLSchemaType(symbol),
            SystemTextJsonAvailable: HasType(compilation, KnownTypes.StjJsonConverterAttribute),
            IParsableAvailable: HasType(compilation, KnownTypes.IParsable),
            CanGenerate: canGenerate,
            Diagnostics: diagnostics.ToEquatableArray())
        {
            DeclaresValidate = DeclaresValidate(symbol),
            SingleValueAvailable = HasType(compilation, KnownTypes.SingleValueInterface),
        };
    }

    // ------------------------------------------------------------------ single value objects

    public static SingleValueObjectDefinition CreateSingleValueObject(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;
        var attribute = context.Attributes[0];
        var compilation = context.SemanticModel.Compilation;

        var type = CreateTypeInfo(symbol, syntax);
        var diagnostics = new List<DiagnosticInfo>();
        var canGenerate = ValidateValueObjectShape(type, "SingleValueObject", diagnostics);

        var valueType = GetTypeArgument(attribute, compilation);

        return new SingleValueObjectDefinition(
            Type: type,
            Value: CreateValueTypeInfo(valueType),
            ColumnLength: GetArgument(attribute, "ColumnLength", -1),
            GraphQLSchemaType: GetGraphQLSchemaType(symbol),
            SystemTextJsonAvailable: HasType(compilation, KnownTypes.StjJsonConverterAttribute),
            CanGenerate: canGenerate,
            Diagnostics: diagnostics.ToEquatableArray())
        {
            DeclaresValidate = DeclaresValidate(symbol),
            SingleValueAvailable = HasType(compilation, KnownTypes.SingleValueInterface),
        };
    }

    // ------------------------------------------------------------------ value objects

    public static ValueObjectDefinition CreateValueObject(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;
        var compilation = context.SemanticModel.Compilation;

        var type = CreateTypeInfo(symbol, syntax);
        var diagnostics = new List<DiagnosticInfo>();
        var canGenerate = ValidateValueObjectShape(type, "ValueObject", diagnostics);

        var properties = new List<PropertyInfo>();
        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.IsIndexer || property.IsImplicitlyDeclared || property.ExplicitInterfaceImplementations.Length > 0)
            {
                continue;
            }

            if (property.Name == "EqualityContract")
            {
                continue;
            }

            // A property synthesized from a positional parameter is declared by that parameter. It is
            // always 'public init' and nothing the author writes can change that, so instead of reporting
            // DDD00010 the generator declares it itself, as 'protected init'.
            var isPositional = property.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken) is ParameterSyntax);

            var info = new PropertyInfo(
                Name: property.Name,
                TypeName: property.Type.ToDisplayString(FullyQualifiedWithNullability),
                HasSetter: property.SetMethod is not null,
                IsInitOnly: property.SetMethod?.IsInitOnly ?? false,
                HasProtectedSetter: property.SetMethod?.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal or Accessibility.ProtectedAndInternal,
                IsInternal: HasAttribute(property, KnownTypes.InternalAttribute),
                IsDontCompare: HasAttribute(property, KnownTypes.DontCompareAttribute),
                Location: LocationInfo.From(property))
            {
                IsPositional = isPositional,
                Attributes = isPositional ? PositionalPropertyAttributes(symbol, property) : EquatableArray<string>.Empty,
            };

            properties.Add(info);

            if (info.IsPositional || info.IsInternal || !info.HasSetter)
            {
                continue;
            }

            if (!info.IsInitOnly)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.UseInitSetters, info.Location, info.Name));
            }

            if (!info.HasProtectedSetter)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.UseProtectedSetters, info.Location, info.Name));
            }
        }

        var primaryConstructor = symbol.InstanceConstructors.FirstOrDefault(constructor =>
            constructor.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken) is RecordDeclarationSyntax));

        return new ValueObjectDefinition(
            Type: type,
            Properties: properties.ToEquatableArray(),
            SystemTextJsonAvailable: HasType(compilation, KnownTypes.StjJsonConverterAttribute),
            CanGenerate: canGenerate,
            Diagnostics: diagnostics.ToEquatableArray())
        {
            PrimaryConstructorParameterTypes = primaryConstructor?.Parameters
                // Without the '?' of a nullable reference type: the generator writes these as default(T).
                .Select(parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .ToEquatableArray(),
            GenerateWith = symbol.GetMembers("With").IsEmpty,
            DeclaresValidate = DeclaresValidate(symbol),
            GraphQLIgnoreAvailable = HasType(compilation, KnownTypes.GraphQLIgnoreAttribute),
        };
    }

    /// <summary>
    /// Whether the author declared a method with the signature of either <c>Validate</c> overload on the
    /// value object base, in any part of the type. Only the signature matters: any such method, override or
    /// not, would clash with the one DDDToolkit.FluentValidation generates. A <c>Validate</c> with other
    /// parameters, such as a static check on the raw value, clashes with nothing and does not count.
    /// </summary>
    private static bool DeclaresValidate(INamedTypeSymbol symbol)
        => symbol.GetMembers("Validate").OfType<IMethodSymbol>().Any(method => method.Parameters.Length switch
        {
            0 => true,
            1 => method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                 == KnownTypes.ValidationNamespace + ".ValidationErrorBuilder",
            _ => false,
        });

    /// <summary>
    /// The <c>property:</c> and <c>field:</c> attributes on a positional parameter, as source for the
    /// property the generator declares in its place. Roslyn reports them on the synthesized property and
    /// its backing field, which is exactly where they have to go again.
    /// </summary>
    private static EquatableArray<string> PositionalPropertyAttributes(INamedTypeSymbol type, IPropertySymbol property)
    {
        var field = type.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(member => SymbolEqualityComparer.Default.Equals(member.AssociatedSymbol, property));

        return property.GetAttributes().Select(attribute => RenderAttribute(attribute, target: null))
            .Concat(field?.GetAttributes().Select(attribute => RenderAttribute(attribute, target: "field")) ?? Enumerable.Empty<string?>())
            .OfType<string>()
            .ToEquatableArray();
    }

    private static string? RenderAttribute(AttributeData attribute, string? target)
    {
        if (attribute.AttributeClass is not { } attributeClass || attribute.AttributeClass.TypeKind == TypeKind.Error)
        {
            return null;
        }

        var arguments = attribute.ConstructorArguments.Select(argument => argument.ToCSharpString())
            .Concat(attribute.NamedArguments.Select(argument => argument.Key + " = " + argument.Value.ToCSharpString()))
            .ToList();

        return "[" + (target is null ? string.Empty : target + ": ")
            + attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            + (arguments.Count == 0 ? string.Empty : "(" + string.Join(", ", arguments) + ")")
            + "]";
    }

    private static bool ValidateValueObjectShape(TypeDeclarationInfo type, string attributeName, List<DiagnosticInfo> diagnostics)
    {
        var canGenerate = true;

        if (type.Kind != DeclarationKind.RecordClass)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.ValueObjectShouldBeRecord, type.Location, type.Name, attributeName));
            canGenerate = false;
        }

        if (!type.IsPartial)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TypeShouldBePartial, type.Location, type.Name, attributeName));
            canGenerate = false;
        }

        if (type.IsSealed)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.ValueObjectsCantBeSealed, type.Location, type.Name));
            canGenerate = false;
        }

        if (type.IsGeneric)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TypeCannotBeGeneric, type.Location, type.Name, attributeName));
            canGenerate = false;
        }

        return canGenerate;
    }

    // ------------------------------------------------------------------ entities and aggregate roots

    public static EntityDefinition CreateEntity(GeneratorAttributeSyntaxContext context, bool isAggregateRoot, CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;
        var attribute = context.Attributes[0];
        var compilation = context.SemanticModel.Compilation;
        var attributeName = isAggregateRoot ? "AggregateRoot" : "Entity";

        var type = CreateTypeInfo(symbol, syntax);
        var diagnostics = new List<DiagnosticInfo>();
        var canGenerate = CheckEntityShape(type, attributeName, diagnostics);

        if (type.IsGeneric)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TypeCannotBeGeneric, type.Location, type.Name, attributeName));
            canGenerate = false;
        }

        // Both attributes on one class means two providers produce a definition for it, and both output
        // steps then add a source with the same hint name, which throws inside the generator and leaves
        // the author with nothing but a CS8785 about a crashed generator. Refuse from both paths so
        // nothing is generated, but report from the aggregate-root path alone so the author sees the
        // complaint exactly once. A class that also carries a parent's or a template's attribute is
        // refused the same way, and reported by the path that reads that attribute.
        var bothAttributes = HasAttribute(symbol, KnownTypes.EntityAttribute) && HasAttribute(symbol, KnownTypes.AggregateRootAttribute);
        var conflictingAttributes = bothAttributes || OtherDeclarations(symbol).Count > 0;
        if (bothAttributes && isAggregateRoot)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.ConflictingEntityAttributes, type.Location, type.Name));
        }

        canGenerate &= !conflictingAttributes;

        var id = ResolveId(symbol, type, attribute, attributeName, compilation, diagnostics, cancellationToken);
        canGenerate &= id.Ok;

        return CreateEntityDefinition(symbol, type, isAggregateRoot, id.IdType, compilation, conflictingAttributes, diagnostics, canGenerate, cancellationToken) with
        {
            ImplicitId = canGenerate ? id.ImplicitId : null,
        };
    }

    /// <summary>
    /// The definition of an abstract parent a package ships with <c>[AggregateRootBase]</c> or
    /// <c>[EntityBase]</c>: the same as for any entity, closed over the parent's first type parameter,
    /// with its type parameters repeated in the generated part.
    /// </summary>
    public static EntityDefinition CreateEntityBase(GeneratorAttributeSyntaxContext context, bool isAggregateRoot, CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;
        var compilation = context.SemanticModel.Compilation;
        var attributeName = isAggregateRoot ? "AggregateRootBase" : "EntityBase";

        var type = CreateTypeInfo(symbol, syntax) with
        {
            TypeParameters = symbol.TypeParameters.Select(static parameter => parameter.Name).ToEquatableArray(),
        };
        var diagnostics = new List<DiagnosticInfo>();
        var canGenerate = CheckEntityShape(type, attributeName, diagnostics);

        var shape = BaseShapeProblem(symbol);
        if (type.Kind == DeclarationKind.Class && shape is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.EntityBaseShape, type.Location, type.Name, attributeName, shape));
            canGenerate = false;
        }

        // Reported once: by the template path when the class also carries a template attribute, which
        // always reports, and otherwise by the aggregate-root parent's path, the way DDD00009 is. The
        // toolkit's own attributes refuse the class silently when they meet one of these.
        var others = OtherDeclarations(symbol, except: isAggregateRoot ? KnownTypes.AggregateRootBaseAttribute : KnownTypes.EntityBaseAttribute);
        var conflictingAttributes = others.Count > 0;
        if (conflictingAttributes)
        {
            var hasTemplate = symbol.GetAttributes().Any(static candidate =>
                candidate.AttributeClass is { } attributeClass && EntityDeclarations.TemplateOf(attributeClass) is not null);
            if (!hasTemplate && (isAggregateRoot || !HasAttribute(symbol, KnownTypes.AggregateRootBaseAttribute)))
            {
                others.Insert(0, "[" + attributeName + "]");
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.ConflictingEntityDeclarations, type.Location, type.Name, Listed(others)));
            }

            canGenerate = false;
        }

        var idType = symbol.TypeParameters.Length > 0 ? symbol.TypeParameters[0].Name : "global::System.Object";

        return CreateEntityDefinition(symbol, type, isAggregateRoot, idType, compilation, conflictingAttributes, diagnostics, canGenerate, cancellationToken) with
        {
            IsBase = true,
        };
    }

    /// <summary>
    /// What is wrong with a parent's shape beyond what every entity is checked for, phrased to finish
    /// "needs ...", or null when nothing is.
    /// </summary>
    private static string? BaseShapeProblem(INamedTypeSymbol symbol)
    {
        if (!symbol.IsAbstract)
        {
            return "to be abstract: only the classes that derive from it are ever created";
        }

        for (var outer = symbol.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            if (outer.TypeParameters.Length > 0)
            {
                return "to be declared outside '" + outer.Name + "', whose type parameters the classes that derive from it cannot name";
            }
        }

        if (symbol.TypeParameters.Length == 0)
        {
            return "type parameters, the id first, so the application chooses its own ids";
        }

        var id = symbol.TypeParameters[0];
        var entityId = id.ConstraintTypes.Any(static constraint => constraint.ToDisplayString() == KnownTypes.EntityIdInterface);
        var equatable = id.ConstraintTypes.Any(constraint =>
            constraint is INamedTypeSymbol { Name: "IEquatable", TypeArguments.Length: 1 } equatableOf
            && equatableOf.ContainingNamespace.ToDisplayString() == "System"
            && SymbolEqualityComparer.Default.Equals(equatableOf.TypeArguments[0], id));

        return entityId && equatable
            ? null
            : "its id parameter constrained with 'where " + id.Name + " : IEntityId, IEquatable<" + id.Name + ">', which the toolkit's base classes require";
    }

    /// <summary>
    /// The definition of a class declared with a package's template attribute, such as
    /// <c>[TenantAggregate&lt;TenantId&gt;]</c>, or null when <paramref name="context"/> is a declaration
    /// that carries none. Its parent is not known yet: the parent may take type arguments from other
    /// classes of the project, and <see cref="ResolveTemplates"/> fills them in once all are known.
    /// <para>
    /// A class has several declarations when it is partial, and each carries only its own attributes.
    /// Only the declaration that carries the template attribute produces a definition, which is what
    /// <c>ForAttributeWithMetadataName</c> does for the toolkit's own attributes: two definitions of one
    /// class would add two sources with one hint name.
    /// </para>
    /// </summary>
    public static EntityDefinition? CreateTemplateEntity(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var syntax = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(syntax, cancellationToken) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        AttributeData? attribute = null;
        TemplateMarker marker = default;
        foreach (var candidate in symbol.GetAttributes())
        {
            if (candidate.AttributeClass is { } candidateClass && EntityDeclarations.TemplateOf(candidateClass) is { } found)
            {
                attribute = candidate;
                marker = found;
                break;
            }
        }

        if (attribute?.AttributeClass is not { } attributeClass
            || attribute.ApplicationSyntaxReference is not { } application
            || application.SyntaxTree != syntax.SyntaxTree
            || !syntax.Span.Contains(application.Span))
        {
            return null;
        }

        var compilation = context.SemanticModel.Compilation;
        var attributeName = AttributeNameOf(attributeClass);

        var type = CreateTypeInfo(symbol, syntax);
        var diagnostics = new List<DiagnosticInfo>();
        var canGenerate = CheckEntityShape(type, attributeName, diagnostics);

        if (type.IsGeneric)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TypeCannotBeGeneric, type.Location, type.Name, attributeName));
            canGenerate = false;
        }

        var others = OtherDeclarations(symbol, except: attributeClass.OriginalDefinition);
        var conflictingAttributes = others.Count > 0;
        if (conflictingAttributes)
        {
            others.Insert(0, "[" + attributeName + "]");
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.ConflictingEntityDeclarations, type.Location, type.Name, Listed(others)));
            canGenerate = false;
        }

        var template = ReadTemplate(marker, attributeClass, attributeName, type, diagnostics, ref canGenerate);

        // A parent in this same project that its own path refuses gets no generated part, and a class derived
        // from it would only turn that one diagnostic into a page of errors in generated code.
        if (template is not null && ParentIsRefused(marker.Parent!, marker.IsAggregateRoot, cancellationToken))
        {
            canGenerate = false;
        }

        var idType = "global::System.Object";
        var idIsEntityId = false;
        if (attributeClass.TypeArguments.Length > 0 && attributeClass.TypeArguments[0] is { TypeKind: not TypeKind.Error } id)
        {
            idType = id.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            idIsEntityId = IsEntityId(id);
            if (!idIsEntityId)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.TemplateIdIsNotAnEntityId, type.Location, type.Name, attributeName, id.ToDisplayString()));
                canGenerate = false;
            }
        }
        else
        {
            // Nothing the compiler could bind, which it reports, or an attribute without a type argument,
            // which the template check has just reported: either way it has been said.
            canGenerate = false;
        }

        return CreateEntityDefinition(
            symbol, type, marker.IsAggregateRoot, idType, compilation, conflictingAttributes, diagnostics, canGenerate, cancellationToken,
            template is null ? null : IsTheParent(marker, attributeClass)) with
        {
            Template = template,
            TemplateKey = attributeClass.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            TemplateIdIsEntityId = idIsEntityId,
            MetadataName = EntityDeclarations.MetadataNameOf(symbol),
            ParentHasKeyParts = marker.Parent is { } keyed && HasKeyParts(keyed),
        };
    }

    /// <summary>Whether a type declares <c>[KeyPart]</c> properties. The attribute survives metadata, so this answers for a referenced parent too.</summary>
    internal static bool HasKeyParts(INamedTypeSymbol type)
        => type.OriginalDefinition.GetMembers().OfType<IPropertySymbol>()
            .Any(static property => !property.IsStatic && !property.IsIndexer && HasAttribute(property, KnownTypes.KeyPartAttribute));

    /// <summary>
    /// Whether a parent declared in this project is one its own path refuses, and so gets no generated part:
    /// not a partial class, not the shape <see cref="BaseShapeProblem"/> asks for, declared another way as
    /// well, or with its key parts spread over files. Its own path reports why. A parent from a referenced assembly was generated when that assembly
    /// was built, or the assembly would not have built.
    /// </summary>
    private static bool ParentIsRefused(INamedTypeSymbol parent, bool isAggregateRoot, CancellationToken cancellationToken)
    {
        if (parent.DeclaringSyntaxReferences.IsEmpty)
        {
            return false;
        }

        return parent.TypeKind != TypeKind.Class
               || parent.IsRecord
               || parent.DeclaringSyntaxReferences.Any(reference =>
                   reference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration
                   || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
               || BaseShapeProblem(parent) is not null
               || OtherDeclarations(parent, except: isAggregateRoot ? KnownTypes.AggregateRootBaseAttribute : KnownTypes.EntityBaseAttribute).Count > 0
               || KeyPartsSpreadOverFiles(parent);
    }

    /// <summary>Whether the <c>[KeyPart]</c>s of a type are declared in more than one file, which DDD00030 refuses.</summary>
    private static bool KeyPartsSpreadOverFiles(INamedTypeSymbol type)
        => type.GetMembers().OfType<IPropertySymbol>()
            .Where(static property => !property.IsStatic && !property.IsIndexer && HasAttribute(property, KnownTypes.KeyPartAttribute))
            .Select(static property => property.Locations.FirstOrDefault(static location => location.IsInSource)?.SourceTree?.FilePath)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any();

    /// <summary>
    /// Whether a rule nested in a template class is about the parent the class will derive from, or about
    /// something the parent is: one of its interfaces or base classes. The generator cannot see that base
    /// class yet, because it writes it, so it takes the template's word for it.
    /// <para>
    /// The parent is closed over the type arguments the attribute supplies, as many of them as it takes: a
    /// type argument beyond those is the template's own and no part of the parent. The ones a
    /// <c>[TemplateArgument]</c> fills are not known until every class of the project is, so any argument
    /// passes there, and a rule about the wrong one is then a compile error rather than a rule quietly left out.
    /// </para>
    /// </summary>
    private static Func<ITypeSymbol, bool>? IsTheParent(TemplateMarker marker, INamedTypeSymbol attributeClass)
    {
        if (marker.Parent is not { } parent || parent.TypeParameters.Length == 0)
        {
            return null;
        }

        var arguments = attributeClass.TypeArguments;
        var known = parent.TypeParameters.Select((parameter, position) => position < arguments.Length ? arguments[position] : parameter).ToImmutableArray();
        var closed = parent.Construct(known, known.Select(static _ => NullableAnnotation.None).ToImmutableArray());

        var above = new List<INamedTypeSymbol> { closed };
        above.AddRange(closed.AllInterfaces);
        for (var baseType = closed.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            above.Add(baseType);
        }

        return candidate => candidate is INamedTypeSymbol named && above.Any(expected => Fits(expected, named, parent));
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is <paramref name="expected"/>, where a type argument that is still
    /// one of the parent's own type parameters matches anything.
    /// </summary>
    private static bool Fits(INamedTypeSymbol expected, INamedTypeSymbol candidate, INamedTypeSymbol parent)
    {
        if (!SymbolEqualityComparer.Default.Equals(expected.OriginalDefinition, candidate.OriginalDefinition))
        {
            return false;
        }

        for (var position = 0; position < expected.TypeArguments.Length && position < candidate.TypeArguments.Length; position++)
        {
            var argument = expected.TypeArguments[position];
            if (argument is ITypeParameterSymbol open && SymbolEqualityComparer.Default.Equals(open.ContainingSymbol, parent))
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(argument, candidate.TypeArguments[position]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// What the template attribute says about the parent, checked against the parent: the marker names a
    /// parent of the right kind, and the attribute's type arguments plus its <c>[TemplateArgument]</c>s fill
    /// each of the parent's type parameters exactly once. Anything else is the package's mistake, DDD00046.
    /// Type arguments the attribute has beyond what the parent takes are no mistake: they are the template's
    /// own, for a registration to take by position.
    /// </summary>
    private static TemplateDeclaration? ReadTemplate(
        TemplateMarker marker,
        INamedTypeSymbol attributeClass,
        string attributeName,
        TypeDeclarationInfo type,
        List<DiagnosticInfo> diagnostics,
        ref bool canGenerate)
    {
        var problem = TemplateProblem(marker, attributeClass, out var bindings);
        if (problem is not null)
        {
            // An attribute declared in this project is reported where it is declared, by CheckTemplateAttribute;
            // one from a package is reported here, on the class that meets it.
            if (attributeClass.OriginalDefinition.DeclaringSyntaxReferences.IsEmpty)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TemplateDoesNotFitItsParent, type.Location, attributeName, "'" + type.Name + "'", problem));
            }

            canGenerate = false;
            return null;
        }

        var parent = marker.Parent!;
        return new TemplateDeclaration(
            AttributeKey: attributeClass.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            AttributeName: attributeName,
            Parent: parent.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None)),
            ParentMetadataName: EntityDeclarations.MetadataNameOf(parent),
            ParentParameters: parent.TypeParameters.Select(static parameter => parameter.Name).ToEquatableArray(),
            Arguments: attributeClass.TypeArguments.Select(static argument => argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToEquatableArray(),
            Bindings: bindings.OrderBy(static binding => binding.Position).ToEquatableArray());
    }

    /// <summary>
    /// DDD00046 on a template attribute this project declares, where the package's author sees it when the
    /// package is built, whether or not anything in the project uses the attribute yet. Null when it fits.
    /// </summary>
    public static DiagnosticInfo? CheckTemplateAttribute(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol attributeClass || EntityDeclarations.TemplateOf(attributeClass) is not { } marker)
        {
            return null;
        }

        return TemplateProblem(marker, attributeClass, out _) is { } problem
            ? DiagnosticInfo.Create(
                DiagnosticDescriptors.TemplateDoesNotFitItsParent,
                context.TargetNode is TypeDeclarationSyntax declaration ? LocationInfo.From(declaration.Identifier) : LocationInfo.From(attributeClass),
                AttributeNameOf(attributeClass),
                "any class",
                problem)
            : null;
    }

    /// <summary>What is wrong with a template, phrased to follow "cannot declare 'X': ", or null when nothing is.</summary>
    private static string? TemplateProblem(TemplateMarker marker, INamedTypeSymbol attributeClass, out List<TemplateBinding> bindings)
    {
        bindings = [];

        var expected = marker.IsAggregateRoot ? "AggregateRootBase" : "EntityBase";
        if (marker.Parent is not { } parent)
        {
            return "its marker names no parent the compiler can find";
        }

        if (!HasAttribute(parent, marker.IsAggregateRoot ? KnownTypes.AggregateRootBaseAttribute : KnownTypes.EntityBaseAttribute))
        {
            return "its parent '" + parent.Name + "' is not marked [" + expected + "]";
        }

        var arity = parent.TypeParameters.Length;
        var own = attributeClass.TypeArguments.Length;
        if (own == 0)
        {
            return "it has " + own + " type arguments, and its parent '" + parent.Name + "' takes " + arity + ", the id first";
        }

        // The attribute's type arguments fill the parent's type parameters from the first on. Those it has
        // beyond the parent's are its own: what a registration takes by position, and nothing of the parent's.
        var filled = new bool[arity];
        for (var position = 0; position < own && position < arity; position++)
        {
            filled[position] = true;
        }

        foreach (var argument in marker.Attribute.GetAttributes())
        {
            if (argument.AttributeClass is not { } argumentClass || !EntityDeclarations.Is(argumentClass, KnownTypes.TemplateArgumentAttribute))
            {
                continue;
            }

            if (argument.ConstructorArguments.Length != 2
                || argument.ConstructorArguments[0].Value is not int position
                || argument.ConstructorArguments[1] is not { Kind: TypedConstantKind.Type, Value: INamedTypeSymbol source }
                || source.TypeKind == TypeKind.Error)
            {
                continue;
            }

            if (position < 0 || position >= arity)
            {
                return "a [TemplateArgument] fills type parameter " + position + ", and its parent '" + parent.Name + "' has " + arity;
            }

            if (filled[position])
            {
                return "type parameter '" + parent.TypeParameters[position].Name + "' of '" + parent.Name + "' is filled twice";
            }

            if (EntityDeclarations.TemplateOf(source) is null)
            {
                return "a [TemplateArgument] takes '" + parent.TypeParameters[position].Name + "' from '" + source.Name + "', which is not a template attribute";
            }

            // TemplateArgumentKind.Type is 1; an enum argument arrives as its underlying value.
            var takeType = argument.NamedArguments.Any(static named => named.Key == "Take" && named.Value.Value is 1);
            if (takeType && parent.TypeParameters[position].HasConstructorConstraint)
            {
                return "type parameter '" + parent.TypeParameters[position].Name + "' of '" + parent.Name
                       + "' takes the application's class and is constrained new(), which that class never meets because its generated parameterless constructor is not public; create it through a static abstract factory instead";
            }

            filled[position] = true;
            bindings.Add(new TemplateBinding(
                position,
                source.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                AttributeNameOf(source),
                EntityDeclarations.MetadataNameOf(source),
                takeType));
        }

        var missing = Enumerable.Range(0, arity).Where(position => !filled[position]).Select(position => "'" + parent.TypeParameters[position].Name + "'").ToList();
        return missing.Count > 0
            ? "nothing fills " + Listed(missing) + " of its parent '" + parent.Name + "'"
            : null;
    }

    /// <summary>What DDD00045 advises where exactly one class is meant: a parent, and a registration of a template declared once.</summary>
    internal const string KeepOne = "keep one";

    /// <summary>A class a <c>[TemplateArgument]</c> can take from: one of this project's template classes, or a referenced project's.</summary>
    internal sealed class TemplateSource
    {
        public TemplateSource(string name, string fullyQualifiedName, string idType, bool idIsEntityId, bool canGenerate, string? metadataName, INamedTypeSymbol? symbol)
        {
            Name = name;
            FullyQualifiedName = fullyQualifiedName;
            IdType = idType;
            IdIsEntityId = idIsEntityId;
            CanGenerate = canGenerate;
            MetadataName = metadataName;
            Symbol = symbol;
        }

        public string Name { get; }

        public string FullyQualifiedName { get; }

        public string IdType { get; }

        public bool IdIsEntityId { get; }

        /// <summary>False when the class gets no generated base class, so it cannot be a parent's type argument.</summary>
        public bool CanGenerate { get; }

        public string? MetadataName { get; }

        public INamedTypeSymbol? Symbol { get; private set; }

        public INamedTypeSymbol? SymbolIn(Compilation compilation)
            => Symbol ??= MetadataName is null ? null : compilation.GetTypeByMetadataName(MetadataName);
    }

    /// <summary>
    /// Closes the parent of every class declared with a template attribute, now that all of them are
    /// known: a <c>[TemplateArgument]</c> takes the id of, or the class itself, from the one class declared
    /// with the template it names, in this project or, when this project declares none, in the projects it
    /// references. None is DDD00044, several is DDD00045, a type argument the template supplies that does
    /// not meet the parent's constraints is DDD00053, and a class it takes that does not is DDD00048; each
    /// leaves the class without a parent, so nothing is generated for it.
    /// <para>
    /// A class that cannot be generated still counts as declared: saying the class is missing when the
    /// author can see it would be wrong. What it can still provide is its id, when that is an entity id.
    /// A class that is itself refused cannot be a parent's type argument, and a class taking it is refused
    /// silently, because the source's own diagnostic already says why.
    /// </para>
    /// <para>
    /// That silence is about the class it takes, not about the ids. What its own parent asks of the ids is
    /// judged whatever became of the classes, so DDD00053 is reported on every class whose parent refuses an
    /// id. Two classes that take each other are each refused once the other is: were they silenced for it, an
    /// id neither parent accepts would leave both without a base class and without a diagnostic.
    /// </para>
    /// </summary>
    public static ImmutableArray<EntityDefinition> ResolveTemplates(
        ImmutableArray<EntityDefinition> declared,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        // A class refused while resolving, for a source of its own that is missing, cannot be taken either,
        // and whatever took it has to be resolved again. The set only grows, so this ends.
        var refusedWhileResolving = new HashSet<int>();
        while (true)
        {
            var resolved = ResolveOnce(declared, refusedWhileResolving, compilation, cancellationToken);
            var changed = false;
            for (var index = 0; index < declared.Length; index++)
            {
                if (declared[index].CanGenerate && !resolved[index].CanGenerate && refusedWhileResolving.Add(index))
                {
                    changed = true;
                }
            }

            if (!changed)
            {
                return resolved;
            }
        }
    }

    private static ImmutableArray<EntityDefinition> ResolveOnce(
        ImmutableArray<EntityDefinition> declared,
        HashSet<int> refusedWhileResolving,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var byTemplate = new Dictionary<string, List<TemplateSource>>(StringComparer.Ordinal);
        for (var index = 0; index < declared.Length; index++)
        {
            var definition = declared[index];
            if (definition.TemplateKey is not { } key)
            {
                continue;
            }

            if (!byTemplate.TryGetValue(key, out var classes))
            {
                byTemplate.Add(key, classes = []);
            }

            classes.Add(new TemplateSource(
                definition.Type.Name,
                definition.Type.FullyQualifiedName,
                definition.IdType,
                definition.TemplateIdIsEntityId,
                definition.CanGenerate && !refusedWhileResolving.Contains(index),
                definition.MetadataName,
                symbol: null));
        }

        var resolved = ImmutableArray.CreateBuilder<EntityDefinition>(declared.Length);
        foreach (var definition in declared)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (definition.Template is not { } template || !definition.CanGenerate)
            {
                resolved.Add(definition);
                continue;
            }

            var arguments = new string[template.ParentParameters.Count];
            var taken = new TemplateSource?[template.ParentParameters.Count];

            // As many of the attribute's type arguments as the parent takes: one beyond those is the template's own.
            for (var position = 0; position < template.Arguments.Count && position < arguments.Length; position++)
            {
                arguments[position] = template.Arguments[position];
            }

            List<DiagnosticInfo>? diagnostics = null;
            var refused = false;
            foreach (var binding in template.Bindings)
            {
                var parameter = "'" + template.ParentParameters[binding.Position] + "'";
                if (!byTemplate.TryGetValue(binding.SourceKey, out var sources))
                {
                    sources = ReferencedSources(compilation, binding.SourceMetadataName, cancellationToken);
                    byTemplate.Add(binding.SourceKey, sources);
                }

                if (sources.Count == 0)
                {
                    diagnostics ??= new List<DiagnosticInfo>(definition.Diagnostics);
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.TemplateArgumentSourceMissing,
                        definition.Type.Location,
                        definition.Type.Name,
                        template.AttributeName,
                        parameter,
                        binding.SourceName) with
                    {
                        Properties = MissingTemplateClass.Properties(compilation, binding.SourceMetadataName, definition.Type.Name, template.AttributeName),
                    });
                    continue;
                }

                if (sources.Count > 1)
                {
                    diagnostics ??= new List<DiagnosticInfo>(definition.Diagnostics);
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.TemplateArgumentSourceAmbiguous,
                        definition.Type.Location,
                        definition.Type.Name,
                        template.AttributeName,
                        parameter,
                        binding.SourceName,
                        Listed(sources.Select(static source => "'" + source.Name + "'")),
                        KeepOne));
                    continue;
                }

                var source = sources[0];
                if (binding.TakeType ? !source.CanGenerate : !source.IdIsEntityId)
                {
                    refused = true;
                    continue;
                }

                arguments[binding.Position] = binding.TakeType ? source.FullyQualifiedName : source.IdType;
                taken[binding.Position] = source;
            }

            if (diagnostics is null
                && UnmetConstraint(definition, template, taken, judgeTheClasses: !refused, compilation, cancellationToken) is { } unmet)
            {
                diagnostics = new List<DiagnosticInfo>(definition.Diagnostics)
                {
                    DiagnosticInfo.Create(
                        unmet.Descriptor,
                        definition.Type.Location,
                        definition.Type.Name,
                        template.AttributeName,
                        unmet.Argument,
                        unmet.Parameter,
                        unmet.Requirement),
                };
            }

            resolved.Add(diagnostics is not null
                ? definition with { Diagnostics = diagnostics.ToEquatableArray(), CanGenerate = false }
                : refused
                    ? definition with { CanGenerate = false }
                    : definition with { BaseType = template.Parent + "<" + string.Join(", ", arguments) + ">" });
        }

        return resolved.MoveToImmutable();
    }

    /// <summary>
    /// The classes declared with a template in the projects this one references, for a
    /// <c>[TemplateArgument]</c> or a registration's <c>[TemplateType]</c> this project has no class for. Only
    /// assemblies that reference the one declaring the template attribute can use it, so only those are
    /// searched, and only for a binding this project could not fill itself. A class that is not accessible
    /// from here cannot be a type argument.
    /// </summary>
    /// <param name="compilation">The project looking.</param>
    /// <param name="attributeMetadataName">The template attribute the class is declared with.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <param name="module">
    /// When given, only the assemblies that declare this <c>[Module]</c> are looked in: a project of a module that
    /// takes a registration's classes from the module's other projects must never be handed another module's class,
    /// nor told that two modules declare one.
    /// </param>
    internal static List<TemplateSource> ReferencedSources(Compilation compilation, string attributeMetadataName, CancellationToken cancellationToken, string? module = null)
    {
        var found = new List<TemplateSource>();
        if (compilation.GetTypeByMetadataName(attributeMetadataName) is not { } attribute)
        {
            return found;
        }

        var package = attribute.ContainingAssembly;
        var key = attribute.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (module is not null && !string.Equals(ModuleBoundary.ModuleOf(assembly), module, StringComparison.Ordinal))
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(assembly, package)
                && !assembly.Modules.Any(module => module.ReferencedAssemblySymbols.Any(reference => reference.Identity.Name == package.Identity.Name)))
            {
                continue;
            }

            if (!TemplateClassesIn(assembly, cancellationToken).TryGetValue(key, out var classes))
            {
                continue;
            }

            foreach (var type in classes)
            {
                if (!compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
                {
                    continue;
                }

                var id = EntityDeclarations.IdArgumentOf(type);
                found.Add(new TemplateSource(
                    type.Name,
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    id?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object",
                    id is not null && IsEntityId(id),
                    canGenerate: true,
                    metadataName: null,
                    symbol: type));
            }
        }

        return found;
    }

    /// <summary>
    /// The classes of a referenced assembly declared with a template, by the template's open definition.
    /// Walking every type of an assembly is the expensive part, and a referenced assembly does not change
    /// while the project that references it is being edited: the compiler keeps the same symbol for it
    /// across compilations, so the walk is done once per assembly and kept for as long as the symbol lives.
    /// </summary>
    private static readonly ConditionalWeakTable<IAssemblySymbol, Dictionary<string, List<INamedTypeSymbol>>> TemplateClassesByAssembly = new();

    /// <summary>
    /// Whether a referenced assembly declares a class with any template: whether it is a project that brings
    /// classes of its own, rather than one that only holds what is made of another project's.
    /// </summary>
    internal static bool DeclaresTemplateClasses(IAssemblySymbol assembly, CancellationToken cancellationToken)
        => TemplateClassesIn(assembly, cancellationToken).Count > 0;

    /// <summary>
    /// Whether an assembly declares a class with the template <paramref name="templateKey"/>: its open definition,
    /// fully qualified, which is how a registration's <c>[TemplateType]</c> names it. Asked of a referenced assembly,
    /// whose walk is kept, and of the project's own where few projects ask (DDD00064 of two projects that declare no
    /// module): its symbol is new with every compilation, so its walk is kept only as long as that compilation.
    /// </summary>
    internal static bool DeclaresClassesWith(IAssemblySymbol assembly, string templateKey, CancellationToken cancellationToken)
        => TemplateClassesIn(assembly, cancellationToken).ContainsKey(templateKey);

    private static Dictionary<string, List<INamedTypeSymbol>> TemplateClassesIn(IAssemblySymbol assembly, CancellationToken cancellationToken)
    {
        if (TemplateClassesByAssembly.TryGetValue(assembly, out var known))
        {
            return known;
        }

        var found = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        foreach (var type in TypesIn(assembly.GlobalNamespace, cancellationToken))
        {
            foreach (var attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass is { } attributeClass && EntityDeclarations.TemplateOf(attributeClass) is not null)
                {
                    var key = attributeClass.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (!found.TryGetValue(key, out var classes))
                    {
                        found.Add(key, classes = []);
                    }

                    classes.Add(type);
                }
            }
        }

        return TemplateClassesByAssembly.GetValue(assembly, _ => found);
    }

    internal static IEnumerable<INamedTypeSymbol> TypesIn(INamespaceOrTypeSymbol scope, CancellationToken cancellationToken)
    {
        foreach (var member in scope.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (member is INamespaceSymbol nested)
            {
                foreach (var type in TypesIn(nested, cancellationToken))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var inner in TypesIn(type, cancellationToken))
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>A parent type parameter that what a template fills it with does not meet, what it would need, and the diagnostic that says so.</summary>
    private readonly record struct Unmet(DiagnosticDescriptor Descriptor, string Argument, string Parameter, string Requirement);

    /// <summary>
    /// The first constraint of the parent that what the template fills it with does not meet, or null when
    /// everything meets what can be told. Without this the parent closed over it would be a compile error in
    /// generated code, with nothing on the class to fix: <c>CS0453</c> or <c>CS0452</c> for a class where a
    /// struct is asked or the other way round, <c>CS0315</c> or <c>CS0311</c> for an interface that is missing.
    /// <para>
    /// The type arguments the attribute supplies itself, the id first, and the ids it takes from other
    /// classes are judged first, as DDD00053: a class it takes is usually constrained by one of those ids, so
    /// an id that does not fit is the mistake to name. They are judged by <see cref="UnmetByAnArgument"/>.
    /// </para>
    /// <para>
    /// The classes it takes with <c>Take = Type</c> come after, as DDD00048. A class declared with a
    /// template is taken to be what its own parent is and will implement, and any class to be what the
    /// toolkit's base classes are, because the generator writes those and this compilation cannot show them yet.
    /// </para>
    /// </summary>
    /// <param name="definition">The class being closed.</param>
    /// <param name="template">What its attribute says about the parent.</param>
    /// <param name="taken">
    /// The class each <c>[TemplateArgument]</c> takes from, by the parent type parameter it fills; null where
    /// the class it would take from cannot be taken.
    /// </param>
    /// <param name="judgeTheClasses">
    /// False when a class it would take is refused already: the class being closed gets no parent whatever the
    /// others are, and the refused one's own diagnostic says why, so only the ids are judged.
    /// </param>
    /// <param name="compilation">The project.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    private static Unmet? UnmetConstraint(
        EntityDefinition definition,
        TemplateDeclaration template,
        TemplateSource?[] taken,
        bool judgeTheClasses,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (compilation.GetTypeByMetadataName(template.ParentMetadataName) is not { } parent
            || definition.MetadataName is null
            || compilation.GetTypeByMetadataName(definition.MetadataName) is not { } dependent)
        {
            return null;
        }

        // Every argument as a symbol, where it can be had: the attribute's own, the classes taken, and the
        // ids taken from classes. What cannot be had leaves the constraints that mention it unchecked.
        var arguments = new ITypeSymbol?[parent.TypeParameters.Length];
        var attributeArguments = dependent.GetAttributes()
            .Select(static attribute => attribute.AttributeClass)
            .FirstOrDefault(attributeClass => attributeClass is not null && EntityDeclarations.TemplateOf(attributeClass) is not null)?.TypeArguments ?? ImmutableArray<ITypeSymbol>.Empty;
        for (var position = 0; position < attributeArguments.Length && position < arguments.Length; position++)
        {
            arguments[position] = attributeArguments[position];
        }

        var takesTheClass = new bool[arguments.Length];
        foreach (var binding in template.Bindings)
        {
            var source = taken[binding.Position]?.SymbolIn(compilation);
            takesTheClass[binding.Position] = binding.TakeType;
            arguments[binding.Position] = binding.TakeType || source is null ? source : EntityDeclarations.IdArgumentOf(source);
        }

        for (var position = 0; position < arguments.Length; position++)
        {
            if (takesTheClass[position] || arguments[position] is not { TypeKind: not TypeKind.Error } argument)
            {
                continue;
            }

            var parameter = parent.TypeParameters[position];
            if (UnmetByAnArgument(parameter, argument, parent.TypeParameters, arguments, compilation, cancellationToken) is { } requirement)
            {
                return new Unmet(
                    DiagnosticDescriptors.TemplateArgumentFailsConstraint,
                    argument.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    parameter.Name,
                    requirement);
            }
        }

        if (!judgeTheClasses)
        {
            return null;
        }

        foreach (var binding in template.Bindings)
        {
            if (!binding.TakeType || arguments[binding.Position] is not INamedTypeSymbol argument)
            {
                continue;
            }

            var parameter = parent.TypeParameters[binding.Position];
            if (parameter.HasValueTypeConstraint && !argument.IsValueType)
            {
                return new Unmet(DiagnosticDescriptors.TemplateArgumentMissesConstraint, argument.Name, parameter.Name, "a struct");
            }

            foreach (var constraint in parameter.ConstraintTypes)
            {
                if (Substitute(constraint, parent.TypeParameters, arguments, compilation) is not { } expected || Satisfies(argument, expected, compilation))
                {
                    continue;
                }

                return new Unmet(
                    DiagnosticDescriptors.TemplateArgumentMissesConstraint,
                    argument.Name,
                    parameter.Name,
                    "'" + expected.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "'");
            }
        }

        return null;
    }

    /// <summary>
    /// What a type argument a template supplies, its own or an id it takes, does not meet of the parent's
    /// constraints on the type parameter it fills, phrased to follow "which requires", or null when it
    /// meets everything that can be told.
    /// <para>
    /// Whether it is a struct or a class is settled by its declaration, and is what goes wrong most: an id
    /// declared as a record class for a parent that holds its ids by value.
    /// </para>
    /// <para>
    /// The rest is judged only of a type nothing will be added to: one from a referenced project, or one
    /// written out in full. A type declared in this project as <c>partial</c> may still be completed by a
    /// generator, and this compilation does not show what that adds: the toolkit's own writes
    /// <c>IEntityId</c> on an <c>[EntityId&lt;T&gt;]</c>, which is how most ids get it. Of such a type an
    /// interface it already shows counts, and one it does not show is left to the compiler.
    /// </para>
    /// <para>
    /// A registration judges a later type argument it takes from a template the same way, against the
    /// constraints of its method (DDD00050): it is the same type, handed to another generic.
    /// </para>
    /// </summary>
    internal static string? UnmetByAnArgument(
        ITypeParameterSymbol parameter,
        ITypeSymbol argument,
        ImmutableArray<ITypeParameterSymbol> parameters,
        ITypeSymbol?[] arguments,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        // `unmanaged` implies `struct`, and a nullable value type is a value type that meets neither.
        if (parameter.HasValueTypeConstraint
            && (!argument.IsValueType || argument.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
        {
            return "a struct";
        }

        if (parameter.HasReferenceTypeConstraint && !argument.IsReferenceType)
        {
            return "a class";
        }

        var complete = !MayStillBeCompleted(argument, cancellationToken);
        if (complete)
        {
            if (parameter.HasUnmanagedTypeConstraint && !argument.IsUnmanagedType)
            {
                return "an unmanaged type";
            }

            if (parameter.HasConstructorConstraint && !HasPublicParameterlessConstructor(argument))
            {
                return "a public parameterless constructor";
            }
        }

        foreach (var constraint in parameter.ConstraintTypes)
        {
            if (Substitute(constraint, parameters, arguments, compilation) is not { TypeKind: not TypeKind.Error } expected
                || ConvertsTo(argument, expected, compilation)
                || !complete)
            {
                continue;
            }

            return "'" + expected.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "'";
        }

        return null;
    }

    /// <summary>
    /// Whether a generator may still add to a type: it is declared in this project, and as <c>partial</c>.
    /// What it will be is then more than this compilation shows.
    /// <para>
    /// One such type is known to stay as it is: an <c>[EntityId&lt;T&gt;]</c> whose own shape the toolkit
    /// refuses. Nothing is written for it, so the interfaces of an id never come, and a parent closed over
    /// it would be the compile error in generated code that its own diagnostic was meant to replace.
    /// </para>
    /// </summary>
    private static bool MayStillBeCompleted(ITypeSymbol type, CancellationToken cancellationToken)
        => type.OriginalDefinition is INamedTypeSymbol declared
           && declared.DeclaringSyntaxReferences.Any(reference =>
               reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration
               && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
           && !(HasAttribute(declared, KnownTypes.EntityIdAttribute) && EntityIdIsRefused(declared));

    /// <summary>
    /// Whether <see cref="CreateEntityId"/> refuses a partial type that carries <c>[EntityId&lt;T&gt;]</c>, by
    /// the same rules: not a record, a sealed record class, or generic, itself or through a type it is nested in.
    /// </summary>
    private static bool EntityIdIsRefused(INamedTypeSymbol id)
    {
        if (!id.IsRecord || (id.IsSealed && !id.IsValueType))
        {
            return true;
        }

        for (var type = id; type is not null; type = type.ContainingType)
        {
            if (type.TypeParameters.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <c>new()</c> is met: by any struct, and by a class that is not abstract and has a public constructor without parameters.</summary>
    private static bool HasPublicParameterlessConstructor(ITypeSymbol type)
        => type.IsValueType
           || (type is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } named
               && named.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public));

    /// <summary>A constraint with the parent's type parameters replaced by the arguments, or null when one of them is not known.</summary>
    internal static ITypeSymbol? Substitute(ITypeSymbol type, ImmutableArray<ITypeParameterSymbol> parameters, ITypeSymbol?[] arguments, Compilation compilation)
    {
        switch (type)
        {
            case ITypeParameterSymbol parameter:
                var position = parameters.IndexOf(parameter, SymbolEqualityComparer.Default);
                return position < 0 ? type : arguments[position];
            case IArrayTypeSymbol array:
                return Substitute(array.ElementType, parameters, arguments, compilation) is { } element ? compilation.CreateArrayTypeSymbol(element, array.Rank) : null;
            case INamedTypeSymbol { IsGenericType: true } generic:
                var substituted = new ITypeSymbol[generic.TypeArguments.Length];
                for (var index = 0; index < substituted.Length; index++)
                {
                    if (Substitute(generic.TypeArguments[index], parameters, arguments, compilation) is not { } argument)
                    {
                        return null;
                    }

                    substituted[index] = argument;
                }

                return generic.OriginalDefinition.Construct(substituted);
            default:
                return type;
        }
    }

    /// <summary>
    /// Whether <paramref name="argument"/> meets a constraint, given what this compilation cannot show for a
    /// class it declares: the toolkit base class the generator writes, closed over the class's own id, the
    /// key-part list it implements, and the parent a template class will derive from, closed over the type
    /// arguments of its attribute. A class from a referenced assembly shows all of that already.
    /// </summary>
    internal static bool Satisfies(INamedTypeSymbol argument, ITypeSymbol expected, Compilation compilation)
    {
        if (ConvertsTo(argument, expected, compilation))
        {
            return true;
        }

        if (expected is not INamedTypeSymbol named || argument.DeclaringSyntaxReferences.IsEmpty)
        {
            return false;
        }

        var toolkitBase = EntityDeclarations.IsAggregateRoot(argument) ? "DDDToolkit.BaseTypes.AggregateRoot`1" : "DDDToolkit.BaseTypes.Entity`1";
        if (EntityDeclarations.IdArgumentOf(argument) is { TypeKind: not TypeKind.Error } id
            && compilation.GetTypeByMetadataName(toolkitBase) is { } baseDefinition
            && ConvertsTo(baseDefinition.Construct(id), expected, compilation))
        {
            return true;
        }

        if (named.ToDisplayString() == "DDDToolkit.Interfaces.IHasKeyParts"
            && (HasKeyParts(argument) || EntityDeclarations.TemplateParentOf(argument) is { } keyed && HasKeyParts(keyed)))
        {
            return true;
        }

        foreach (var attribute in argument.GetAttributes())
        {
            if (attribute.AttributeClass is { } attributeClass
                && EntityDeclarations.TemplateOf(attributeClass) is { } marker
                && IsTheParent(marker, attributeClass) is { } isTheParent)
            {
                return isTheParent(named);
            }
        }

        return false;
    }

    private static bool ConvertsTo(ITypeSymbol from, ITypeSymbol to, Compilation compilation)
        => compilation is CSharpCompilation csharp
           && csharp.ClassifyConversion(from, to) is { IsImplicit: true } conversion
           && (conversion.IsIdentity || conversion.IsReference || conversion.IsBoxing);

    /// <summary>The attribute as the author writes it: <c>TenantAggregateAttribute&lt;T&gt;</c> is <c>TenantAggregate</c>.</summary>
    internal static string AttributeNameOf(INamedTypeSymbol attributeClass)
        => attributeClass.Name.EndsWith("Attribute", StringComparison.Ordinal) && attributeClass.Name.Length > "Attribute".Length
            ? attributeClass.Name.Substring(0, attributeClass.Name.Length - "Attribute".Length)
            : attributeClass.Name;

    /// <summary>
    /// The parents' and templates' declarations on <paramref name="symbol"/>, which the toolkit's own
    /// <c>[AggregateRoot]</c> and <c>[Entity]</c> cannot sit next to.
    /// </summary>
    private static List<string> OtherDeclarations(INamedTypeSymbol symbol)
        => OtherDeclarations(symbol, static attributeClass =>
            EntityDeclarations.Is(attributeClass, KnownTypes.AggregateRootAttribute) || EntityDeclarations.Is(attributeClass, KnownTypes.EntityAttribute));

    /// <summary>The declarations on <paramref name="symbol"/> other than the parent's attribute being read.</summary>
    private static List<string> OtherDeclarations(INamedTypeSymbol symbol, string except)
        => OtherDeclarations(symbol, attributeClass => EntityDeclarations.Is(attributeClass, except));

    /// <summary>The declarations on <paramref name="symbol"/> other than the template attribute being read.</summary>
    private static List<string> OtherDeclarations(INamedTypeSymbol symbol, INamedTypeSymbol except)
        => OtherDeclarations(symbol, attributeClass => SymbolEqualityComparer.Default.Equals(attributeClass.OriginalDefinition, except));

    /// <summary>
    /// The declarations on <paramref name="symbol"/> other than the one being read, as the author writes
    /// them: <c>[AggregateRoot]</c>, <c>[EntityBase]</c>, <c>[TenantAggregate]</c>. Each gives the class a base
    /// class, so any one of them next to another is DDD00047, or DDD00009 for the toolkit's own pair.
    /// </summary>
    private static List<string> OtherDeclarations(INamedTypeSymbol symbol, Func<INamedTypeSymbol, bool> isTheOneBeingRead)
    {
        var others = new List<string>();
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass || isTheOneBeingRead(attributeClass))
            {
                continue;
            }

            if (EntityDeclarations.Is(attributeClass, KnownTypes.AggregateRootAttribute)
                || EntityDeclarations.Is(attributeClass, KnownTypes.EntityAttribute)
                || EntityDeclarations.Is(attributeClass, KnownTypes.AggregateRootBaseAttribute)
                || EntityDeclarations.Is(attributeClass, KnownTypes.EntityBaseAttribute)
                || EntityDeclarations.TemplateOf(attributeClass) is not null)
            {
                others.Add("[" + AttributeNameOf(attributeClass) + "]");
            }
        }

        return others;
    }

    /// <summary>"a", "a and b", "a, b and c".</summary>
    internal static string Listed(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1
            ? string.Concat(list)
            : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[list.Count - 1];
    }

    /// <summary>What every entity is checked for, whichever way it is declared: a partial class.</summary>
    private static bool CheckEntityShape(TypeDeclarationInfo type, string attributeName, List<DiagnosticInfo> diagnostics)
    {
        var canGenerate = true;

        if (type.Kind != DeclarationKind.Class)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.EntityShouldBeClass, type.Location, type.Name, attributeName));
            canGenerate = false;
        }

        if (!type.IsPartial)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TypeShouldBePartial, type.Location, type.Name, attributeName));
            canGenerate = false;
        }

        return canGenerate;
    }

    /// <summary>
    /// Everything about an entity that does not depend on how it was declared: the aggregate boundary,
    /// its rules, its collections and its key parts.
    /// </summary>
    /// <param name="conflictingAttributes">
    /// True when the class is declared more than one way. Nothing is generated for it and it has been
    /// reported once already; the checks below are skipped because every path that reads it would report
    /// the same members again.
    /// </param>
    private static EntityDefinition CreateEntityDefinition(
        INamedTypeSymbol symbol,
        TypeDeclarationInfo type,
        bool isAggregateRoot,
        string idType,
        Compilation compilation,
        bool conflictingAttributes,
        List<DiagnosticInfo> diagnostics,
        bool canGenerate,
        CancellationToken cancellationToken,
        Func<ITypeSymbol, bool>? parent = null)
    {
        var invariants = EquatableArray<string>.Empty;
        if (!conflictingAttributes)
        {
            AggregateBoundary.Check(symbol, isAggregateRoot, diagnostics, cancellationToken);

            // A rule the generator cannot create is reported and left out, the way an unusable collection
            // property is: the entity itself is still generated, because its base class is what makes the
            // rest of the author's file compile at all.
            invariants = Invariants.Collect(symbol, compilation, diagnostics, cancellationToken, parent);
        }

        var collections = new List<CollectionPropertyInfo>();
        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (property.IsStatic || property.IsIndexer || !property.IsPartialDefinition || property.PartialImplementationPart is not null)
            {
                continue;
            }

            if (property.Type is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } collectionType)
            {
                continue;
            }

            if (!CollectionInterfaces.TryGetValue(collectionType.OriginalDefinition.ToDisplayString(), out var backing))
            {
                continue;
            }

            var fieldName = Identifiers.CollectionFieldNameFor(property.Name);
            var modifiers = SyntaxFacts.GetText(property.DeclaredAccessibility)
                + (property.IsVirtual ? " virtual" : string.Empty)
                + (property.IsOverride ? " override" : string.Empty)
                + (property.IsSealed ? " sealed" : string.Empty)
                + " partial";

            var info = new CollectionPropertyInfo(
                Name: property.Name,
                FieldName: fieldName,
                ElementType: collectionType.TypeArguments[0].ToDisplayString(FullyQualifiedWithNullability),
                ElementIsEntity: IsChildEntity(collectionType.TypeArguments[0]),
                InterfaceType: collectionType.ToDisplayString(FullyQualifiedWithNullability),
                Backing: backing,
                Modifiers: modifiers,
                HasSetter: property.SetMethod is not null,
                Location: LocationInfo.From(property));

            if (info.HasSetter)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.CollectionPropertyMustBeGetOnly, info.Location, info.Name, info.FieldName));
                continue;
            }

            collections.Add(info);
        }

        // A class declared more than one way generates nothing and is reported once already; checking
        // its key parts from every path would report every key-part warning more than once.
        var keyParts = conflictingAttributes ? [] : CollectKeyParts(symbol, type, diagnostics, ref canGenerate, cancellationToken);

        return new EntityDefinition(
            Type: type,
            IsAggregateRoot: isAggregateRoot,
            IdType: idType,
            ImplicitId: null,
            Collections: collections.ToEquatableArray(),
            Invariants: invariants,
            KeyParts: keyParts.ToEquatableArray(),
            EfBackingFieldAttributeAvailable: HasType(compilation, KnownTypes.EfBackingFieldAttribute),
            ReadOnlySetAvailable: HasType(compilation, KnownTypes.ReadOnlySet),
            CanGenerate: canGenerate,
            Diagnostics: diagnostics.ToEquatableArray());
    }

    /// <summary>
    /// Whether the elements of a collection are child entities, which is what decides whether an
    /// aggregate walks that collection when it answers for what it holds. The attribute is the only
    /// signal available: the base class that would prove it comes from this same generator, and a
    /// generator cannot see another's output. It survives the trip through metadata, so a child entity
    /// from a referenced assembly is recognised too.
    /// <para>
    /// A class is required because that is the only shape the entity generator produces a base class
    /// for, and a non-generic one because a generic entity is refused outright. The exception is a parent
    /// a package ships, which is generic by design. Anything else would have no
    /// <c>GetInvariantViolations</c> to call, and a walk emitted over it would turn one diagnostic on the
    /// author's own declaration into a compile error in generated code.
    /// </para>
    /// <para>
    /// Inside a parent the element is often a type parameter: the application's own class, which the
    /// parent holds without knowing it, as in <c>IReadOnlyList&lt;TUnit&gt;</c> where
    /// <c>TUnit : OrganizationUnitEntity&lt;TUnitId&gt;</c>. It is a child entity when its constraint is one,
    /// because the constraint is what gives the walk its <c>GetInvariantViolations</c>.
    /// </para>
    /// </summary>
    private static bool IsChildEntity(ITypeSymbol element) => element switch
    {
        ITypeParameterSymbol parameter => parameter.ConstraintTypes.Any(static constraint =>
            constraint is INamedTypeSymbol { TypeKind: TypeKind.Class } named && EntityDeclarations.IsEntityOrAggregateRoot(named)),
        INamedTypeSymbol { TypeKind: TypeKind.Class, IsGenericType: false } named => EntityDeclarations.IsEntityOrAggregateRoot(named),
        INamedTypeSymbol { TypeKind: TypeKind.Class, IsGenericType: true } named => EntityDeclarations.IsBase(named),
        _ => false,
    };

    // ------------------------------------------------------------------ key parts

    /// <summary>
    /// The <c>[KeyPart]</c> properties declared on this type, in declaration order. That order becomes
    /// the column order of the primary key, so it is taken from the source rather than from
    /// <see cref="INamespaceOrTypeSymbol.GetMembers()"/>, and it is only defined within one file:
    /// key parts spread over several parts of a partial class report DDD00030 and stop generation.
    /// A key part with a public setter reports DDD00029 and is kept.
    /// </summary>
    private static List<string> CollectKeyParts(
        INamedTypeSymbol symbol,
        TypeDeclarationInfo type,
        List<DiagnosticInfo> diagnostics,
        ref bool canGenerate,
        CancellationToken cancellationToken)
    {
        var declared = new List<(IPropertySymbol Property, Location Location)>();
        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (property.IsStatic || property.IsIndexer || !HasAttribute(property, KnownTypes.KeyPartAttribute))
            {
                continue;
            }

            var location = property.Locations.FirstOrDefault(static l => l.IsInSource);
            if (location is null)
            {
                continue;
            }

            declared.Add((property, location));

            if (property.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false })
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.KeyPartHasPublicSetter, LocationInfo.From(location), type.Name, property.Name));
            }
        }

        if (declared.Count == 0)
        {
            return [];
        }

        var files = declared.Select(static d => d.Location.SourceTree!.FilePath).Distinct(StringComparer.Ordinal).ToList();
        if (files.Count > 1)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.KeyPartsSpreadOverFiles,
                type.Location,
                type.Name,
                string.Join(", ", files.Select(static f => System.IO.Path.GetFileName(f)))));
            canGenerate = false;
            return [];
        }

        return declared
            .OrderBy(static d => d.Location.SourceSpan.Start)
            .Select(static d => d.Property.Name)
            .ToList();
    }

    /// <summary>
    /// DDD00028 for a <c>[KeyPart]</c> property whose type is neither an entity nor an aggregate root;
    /// null when it is on one, where <see cref="CreateEntity"/> reads it instead.
    /// </summary>
    public static DiagnosticInfo? CheckKeyPartPlacement(GeneratorAttributeSyntaxContext context)
    {
        var property = context.TargetSymbol;
        var containingType = property.ContainingType;
        if (containingType is null || EntityDeclarations.IsEntityOrAggregateRoot(containingType))
        {
            return null;
        }

        return DiagnosticInfo.Create(DiagnosticDescriptors.KeyPartOutsideAnEntity, LocationInfo.From(property), containingType.Name, property.Name);
    }

    // ------------------------------------------------------------------ the id of an entity

    /// <summary>
    /// What <c>[Entity&lt;T&gt;]</c> and <c>[AggregateRoot&lt;T&gt;]</c> name with their type argument: either an
    /// id that already exists, or the raw value an id should wrap, in which case the id is derived here
    /// and generated alongside the entity.
    /// </summary>
    /// <param name="IdType">Fully qualified name of the id, generated or not. What the base class is closed over.</param>
    /// <param name="ImplicitId">The id to generate, or null when the type argument already was one.</param>
    /// <param name="Ok">False when the type argument cannot be used at all; a diagnostic was added.</param>
    private readonly record struct IdResolution(string IdType, EntityIdDefinition? ImplicitId, bool Ok);

    private static IdResolution ResolveId(
        INamedTypeSymbol entity,
        TypeDeclarationInfo type,
        AttributeData attribute,
        string attributeName,
        Compilation compilation,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        var argument = GetTypeArgumentOrNull(attribute);
        if (argument is null)
        {
            // The attribute names nothing the compiler could bind, which it reports itself. Saying so
            // twice helps nobody, so stop here without a diagnostic of our own.
            return new IdResolution("global::System.Object", null, false);
        }

        var fullyQualified = argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (IsEntityId(argument))
        {
            return new IdResolution(fullyQualified, null, true);
        }

        if (!CanBeWrappedInAnId(argument))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.UnsupportedIdTypeArgument, type.Location, type.Name, attributeName, argument.ToDisplayString()));
            return new IdResolution(fullyQualified, null, false);
        }

        var idName = Identifiers.IdNameFor(type.Name);
        if (!IsNameAvailable(entity, idName, type.Accessibility, cancellationToken, out var authorsPart))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.GeneratedIdNameTaken, type.Location, type.Name, attributeName, idName));
            return new IdResolution(fullyQualified, null, false);
        }

        var implicitId = CreateImplicitEntityId(type, idName, attribute, argument, compilation, authorsPart);
        return new IdResolution(implicitId.Type.FullyQualifiedName, implicitId, true);
    }

    /// <summary>
    /// The id derived from an entity declaration: a readonly record struct named after the entity, with
    /// the same shape an explicitly declared struct id has. The emitter is the same one, so the two
    /// forms cannot drift apart.
    /// </summary>
    private static EntityIdDefinition CreateImplicitEntityId(
        TypeDeclarationInfo entity,
        string idName,
        AttributeData attribute,
        ITypeSymbol valueType,
        Compilation compilation,
        INamedTypeSymbol? authorsPart)
    {
        // "global::Shop.Orders.Order" minus "Order" is the scope the id is declared in, nesting included.
        var scope = entity.FullyQualifiedName.Substring(0, entity.FullyQualifiedName.Length - entity.Name.Length);

        var type = new TypeDeclarationInfo(
            Name: idName,
            Namespace: entity.Namespace,
            FullyQualifiedName: scope + idName,
            Accessibility: entity.Accessibility,
            Kind: DeclarationKind.RecordStruct,
            IsPartial: true,
            IsSealed: false,
            IsReadOnly: true,
            IsAbstract: false,
            IsGeneric: false,
            ContainingTypeHeaders: entity.ContainingTypeHeaders,
            Location: entity.Location)
        {
            IsImplicit = true,
        };

        return new EntityIdDefinition(
            Type: type,
            Value: CreateValueTypeInfo(valueType),
            Prefix: GetArgument(attribute, "Prefix", Identifiers.DefaultIdPrefix),
            ColumnLength: GetArgument(attribute, "ColumnLength", -1),

            // [GraphQLType<T>] has only one place to go: a part of the id the author wrote themselves.
            GraphQLSchemaType: authorsPart is null ? null : GetGraphQLSchemaType(authorsPart),
            SystemTextJsonAvailable: HasType(compilation, KnownTypes.StjJsonConverterAttribute),
            IParsableAvailable: HasType(compilation, KnownTypes.IParsable),
            CanGenerate: true,
            Diagnostics: EquatableArray<DiagnosticInfo>.Empty)
        {
            SingleValueAvailable = HasType(compilation, KnownTypes.SingleValueInterface),
        };
    }

    /// <summary>What <see cref="IdOf"/> found out about a type's id.</summary>
    internal enum DeclaredIdKind
    {
        /// <summary>The type is no entity and no aggregate root, so it has no id.</summary>
        NotAnEntity,

        /// <summary>The type is declared one, and its id cannot be had: its own diagnostic says why.</summary>
        Unavailable,

        /// <summary>The id is known.</summary>
        Found,
    }

    /// <summary>
    /// The id of an entity or aggregate root somebody names, as the entity generator decides it. A class from
    /// a referenced project shows it: the toolkit's base class it was generated with is closed over it. A
    /// class of this project does not have that base class yet, so its declaration says it: the id it names,
    /// or, where it names a raw value, the id generated beside it and named after it.
    /// <para>
    /// A class of this project that <c>[AggregateRoot&lt;T&gt;]</c> or <c>[Entity&lt;T&gt;]</c> refuses gets
    /// nothing generated, neither its base class nor an id over a raw value, so nothing can be closed over it
    /// either. It is judged by the checks <see cref="CreateEntity"/> makes, and its own diagnostic says what
    /// is wrong with it.
    /// </para>
    /// </summary>
    /// <param name="type">The class named.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <param name="text">The id, fully qualified.</param>
    /// <param name="name">The id's own name, <c>OrderId</c>.</param>
    /// <param name="symbol">The id's symbol, or null for an id that is still to be generated: always a struct.</param>
    internal static DeclaredIdKind IdOf(INamedTypeSymbol type, CancellationToken cancellationToken, out string text, out string name, out ITypeSymbol? symbol)
    {
        text = name = string.Empty;
        symbol = null;

        for (var parent = type.BaseType; parent is not null; parent = parent.BaseType)
        {
            if (parent.OriginalDefinition.MetadataName == "Entity`1"
                && parent.ContainingNamespace.ToDisplayString() == "DDDToolkit.BaseTypes"
                && parent.TypeArguments[0] is { TypeKind: not TypeKind.Error } shown)
            {
                return Found(shown, out text, out name, out symbol);
            }
        }

        if (!EntityDeclarations.IsEntityOrAggregateRoot(type))
        {
            return DeclaredIdKind.NotAnEntity;
        }

        if (EntityDeclarations.IdArgumentOf(type) is not { } argument)
        {
            return DeclaredIdKind.Unavailable;
        }

        var own = type.OriginalDefinition.GetAttributes().FirstOrDefault(static attribute =>
            attribute.AttributeClass is { } attributeClass
            && (EntityDeclarations.Is(attributeClass, KnownTypes.AggregateRootAttribute) || EntityDeclarations.Is(attributeClass, KnownTypes.EntityAttribute)));
        if (own is null)
        {
            // A parent or a template: its first type argument is the id, and a template refuses a raw value.
            return IsEntityId(argument) ? Found(argument, out text, out name, out symbol) : DeclaredIdKind.Unavailable;
        }

        // The toolkit's own attributes, on a class that is still to be generated: whether it will be.
        if (own.ApplicationSyntaxReference?.GetSyntax(cancellationToken)?.Parent?.Parent is not TypeDeclarationSyntax syntax)
        {
            return DeclaredIdKind.Unavailable;
        }

        var declaration = CreateTypeInfo(type, syntax);
        if (!CheckEntityShape(declaration, string.Empty, [])
            || declaration.IsGeneric
            || (HasAttribute(type, KnownTypes.EntityAttribute) && HasAttribute(type, KnownTypes.AggregateRootAttribute))
            || OtherDeclarations(type).Count > 0)
        {
            return DeclaredIdKind.Unavailable;
        }

        if (IsEntityId(argument))
        {
            return Found(argument, out text, out name, out symbol);
        }

        // A raw value: the id is generated beside the class and named after it.
        name = Identifiers.IdNameFor(type.Name);
        if (!CanBeWrappedInAnId(argument) || !IsNameAvailable(type, name, declaration.Accessibility, cancellationToken, out _))
        {
            return DeclaredIdKind.Unavailable;
        }

        text = declaration.FullyQualifiedName.Substring(0, declaration.FullyQualifiedName.Length - declaration.Name.Length) + name;
        return DeclaredIdKind.Found;

        static DeclaredIdKind Found(ITypeSymbol id, out string text, out string name, out ITypeSymbol? symbol)
        {
            text = id.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            name = id.Name;
            symbol = id;
            return DeclaredIdKind.Found;
        }
    }

    /// <summary>
    /// Whether the type already is a strongly typed id. The interface is only there for ids that come
    /// from another assembly: an id in this compilation gets it from a generator, and a generator
    /// cannot see another generator's output, so the attribute is what identifies those.
    /// </summary>
    internal static bool IsEntityId(ITypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is { } attributeClass
                && attributeClass.Name == KnownTypes.EntityIdAttributeName
                && attributeClass.ContainingNamespace.ToDisplayString() == KnownTypes.AttributesNamespace)
            {
                return true;
            }
        }

        return type.AllInterfaces.Any(@interface => @interface.ToDisplayString() == KnownTypes.EntityIdInterface);
    }

    /// <summary>
    /// Whether an id can be generated over this value. A string or a value type is copied by value and
    /// cannot be null, which is what the generated <c>Value</c>, <c>IsEmpty</c> and equality assume. A
    /// nullable value type is excluded on purpose: an optional id is <c>OrderId?</c>, not an id over
    /// <c>Guid?</c>.
    /// </summary>
    private static bool CanBeWrappedInAnId(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        return type.IsValueType
            && type is not ITypeParameterSymbol
            && type.TypeKind != TypeKind.Pointer
            && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
    }

    /// <summary>
    /// Whether the generated id can be declared next to the entity. Anything else of that name in the
    /// same namespace or containing type is a clash, except a partial record struct the author declared
    /// to add members to the id: the generated declaration is another part of that one, and is handed
    /// back through <paramref name="authorsPart"/> so what the author put on it is not lost.
    /// </summary>
    private static bool IsNameAvailable(
        INamedTypeSymbol entity,
        string idName,
        string accessibility,
        CancellationToken cancellationToken,
        out INamedTypeSymbol? authorsPart)
    {
        authorsPart = null;
        var scope = (INamespaceOrTypeSymbol?)entity.ContainingType ?? entity.ContainingNamespace;

        foreach (var member in scope.GetMembers(idName))
        {
            if (member is not INamedTypeSymbol existing || !IsPartOfTheGeneratedId(existing, accessibility, cancellationToken))
            {
                authorsPart = null;
                return false;
            }

            authorsPart = existing;
        }

        return true;
    }

    private static bool IsPartOfTheGeneratedId(INamedTypeSymbol existing, string accessibility, CancellationToken cancellationToken)
    {
        // An id of its own carries [EntityId<T>], and two definitions of one id would collide.
        if (!existing.IsRecord || !existing.IsValueType || existing.TypeParameters.Length > 0 || IsEntityId(existing))
        {
            return false;
        }

        if (existing.DeclaringSyntaxReferences.Length == 0)
        {
            return false;
        }

        foreach (var reference in existing.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax syntax
                || !syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return false;
            }

            // A part without an accessibility modifier takes it from the generated one; a part that
            // states a different accessibility does not compile (CS0262).
            var stated = syntax.Modifiers.Any(modifier => AccessibilityModifiers.Contains(modifier.Kind()));
            if (stated && SyntaxFacts.GetText(existing.DeclaredAccessibility) != accessibility)
            {
                return false;
            }
        }

        return true;
    }

    // ------------------------------------------------------------------ shared helpers

    public static TypeDeclarationInfo CreateTypeInfo(INamedTypeSymbol symbol, TypeDeclarationSyntax syntax)
    {
        var kind = symbol switch
        {
            { IsRecord: true, IsValueType: true } => DeclarationKind.RecordStruct,
            { IsRecord: true } => DeclarationKind.RecordClass,
            { TypeKind: TypeKind.Struct } => DeclarationKind.Struct,
            { TypeKind: TypeKind.Class } => DeclarationKind.Class,
            { TypeKind: TypeKind.Interface } => DeclarationKind.Interface,
            _ => DeclarationKind.Other,
        };

        var containing = new List<string>();
        var isGeneric = symbol.TypeParameters.Length > 0;
        for (var outer = symbol.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            isGeneric |= outer.TypeParameters.Length > 0;

            var keyword = outer switch
            {
                { IsRecord: true, IsValueType: true } => "record struct",
                { IsRecord: true } => "record",
                { TypeKind: TypeKind.Struct } => "struct",
                { TypeKind: TypeKind.Interface } => "interface",
                _ => "class",
            };
            var typeParameters = outer.TypeParameters.Length == 0
                ? string.Empty
                : "<" + string.Join(", ", outer.TypeParameters.Select(p => p.Name)) + ">";
            containing.Insert(0, "partial " + keyword + " " + outer.Name + typeParameters);
        }

        return new TypeDeclarationInfo(
            Name: symbol.Name,
            Namespace: symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString(),
            FullyQualifiedName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Accessibility: SyntaxFacts.GetText(symbol.DeclaredAccessibility),
            Kind: kind,
            IsPartial: syntax.Modifiers.Any(SyntaxKind.PartialKeyword),
            IsSealed: symbol.IsSealed && kind is not (DeclarationKind.RecordStruct or DeclarationKind.Struct),
            IsReadOnly: symbol.IsReadOnly,
            IsAbstract: symbol.IsAbstract,
            IsGeneric: isGeneric,
            ContainingTypeHeaders: containing.ToEquatableArray(),
            Location: LocationInfo.From(syntax.Identifier));
    }

    public static ValueTypeInfo CreateValueTypeInfo(ITypeSymbol type)
    {
        var fullyQualified = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var hasFormatProviderTryParse = type.GetMembers("TryParse").OfType<IMethodSymbol>().Any(method =>
            method.IsStatic
            && method.Parameters.Length == 3
            && method.Parameters[0].Type.SpecialType == SpecialType.System_String
            && method.Parameters[1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.IFormatProvider"
            && method.Parameters[2].RefKind == RefKind.Out
            && SymbolEqualityComparer.Default.Equals(method.Parameters[2].Type, type));

        return new ValueTypeInfo(
            FullyQualifiedName: fullyQualified,
            Name: type.Name,
            IsString: type.SpecialType == SpecialType.System_String,
            IsGuid: fullyQualified == "global::System.Guid",
            IsValueType: type.IsValueType,
            HasFormatProviderTryParse: hasFormatProviderTryParse);
    }

    /// <summary>The single type argument of a generic attribute such as [EntityId&lt;Guid&gt;]; object when the attribute is malformed.</summary>
    private static ITypeSymbol GetTypeArgument(AttributeData attribute, Compilation compilation)
        => GetTypeArgumentOrNull(attribute) ?? compilation.GetSpecialType(SpecialType.System_Object);

    /// <summary>The single type argument of a generic attribute, or null when the compiler could not bind one.</summary>
    private static ITypeSymbol? GetTypeArgumentOrNull(AttributeData attribute)
        => attribute.AttributeClass is { TypeArguments.Length: > 0 } attributeClass && attributeClass.TypeArguments[0] is not IErrorTypeSymbol
            ? attributeClass.TypeArguments[0]
            : null;

    /// <summary>Reads a constructor argument by parameter name (positional or named syntax), falling back to a named property.</summary>
    internal static T GetArgument<T>(AttributeData attribute, string parameterName, T defaultValue)
    {
        var parameters = attribute.AttributeConstructor?.Parameters;
        if (parameters is not null)
        {
            for (var i = 0; i < parameters.Value.Length && i < attribute.ConstructorArguments.Length; i++)
            {
                if (parameters.Value[i].Name == parameterName)
                {
                    var argument = attribute.ConstructorArguments[i];
                    return argument is { IsNull: false, Kind: TypedConstantKind.Primitive, Value: T value } ? value : defaultValue;
                }
            }
        }

        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key == parameterName && named.Value is { IsNull: false, Kind: TypedConstantKind.Primitive, Value: T value })
            {
                return value;
            }
        }

        return defaultValue;
    }

    /// <summary>The fully qualified schema type from [GraphQLType&lt;TSchemaType&gt;], if the type carries one.</summary>
    private static string? GetGraphQLSchemaType(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass?.OriginalDefinition.ToDisplayString() == "DDDToolkit.HotChocolate.Attributes.GraphQLTypeAttribute<TSchemaType>"
                && attributeClass.TypeArguments.Length == 1)
            {
                return attributeClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the symbol carries the attribute with this metadata name. The arity suffix of a generic
    /// attribute is stripped, because <see cref="ISymbol.Name"/> reports <c>EntityAttribute</c> where the
    /// metadata name is <c>EntityAttribute`1</c>; comparing the two directly never matches.
    /// </summary>
    internal static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        var expectedName = metadataName.Substring(metadataName.LastIndexOf('.') + 1);
        var arity = expectedName.IndexOf('`');
        if (arity >= 0)
        {
            expectedName = expectedName.Substring(0, arity);
        }

        var expectedNamespace = metadataName.Substring(0, metadataName.LastIndexOf('.'));

        return symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass is { } attributeClass
            && attributeClass.Name == expectedName
            && attributeClass.ContainingNamespace.ToDisplayString() == expectedNamespace);
    }

    private static bool HasType(Compilation compilation, string metadataName) => compilation.GetTypeByMetadataName(metadataName) is not null;
}
