using System;
using System.Collections.Generic;
using System.Linq;
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
            Diagnostics: diagnostics.ToEquatableArray());
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
            Diagnostics: diagnostics.ToEquatableArray());
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

            var info = new PropertyInfo(
                Name: property.Name,
                TypeName: property.Type.ToDisplayString(FullyQualifiedWithNullability),
                HasSetter: property.SetMethod is not null,
                IsInitOnly: property.SetMethod?.IsInitOnly ?? false,
                HasProtectedSetter: property.SetMethod?.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal or Accessibility.ProtectedAndInternal,
                IsInternal: HasAttribute(property, KnownTypes.InternalAttribute),
                IsDontCompare: HasAttribute(property, KnownTypes.DontCompareAttribute),
                Location: LocationInfo.From(property));

            properties.Add(info);

            if (info.IsInternal || !info.HasSetter)
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

        return new ValueObjectDefinition(
            Type: type,
            Properties: properties.ToEquatableArray(),
            SystemTextJsonAvailable: HasType(compilation, KnownTypes.StjJsonConverterAttribute),
            CanGenerate: canGenerate,
            Diagnostics: diagnostics.ToEquatableArray());
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

        if (type.IsGeneric)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TypeCannotBeGeneric, type.Location, type.Name, attributeName));
            canGenerate = false;
        }

        // Both attributes on one class means two providers produce a definition for it, and both output
        // steps then add a source with the same hint name, which throws inside the generator and leaves
        // the author with nothing but a CS8785 about a crashed generator. Refuse from both paths so
        // nothing is generated, but report from the aggregate-root path alone so the author sees the
        // complaint exactly once.
        var conflictingAttributes = HasAttribute(symbol, KnownTypes.EntityAttribute) && HasAttribute(symbol, KnownTypes.AggregateRootAttribute);
        var invariants = EquatableArray<string>.Empty;
        if (conflictingAttributes)
        {
            if (isAggregateRoot)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.ConflictingEntityAttributes, type.Location, type.Name));
            }

            canGenerate = false;
        }
        else
        {
            // Both skipped for a class carrying both attributes: it is reported already, nothing is
            // generated for it, and both providers would otherwise report over the same members twice.
            AggregateBoundary.Check(symbol, isAggregateRoot, diagnostics, cancellationToken);

            // A rule the generator cannot create is reported and left out, the way an unusable collection
            // property is: the entity itself is still generated, because its base class is what makes the
            // rest of the author's file compile at all.
            invariants = Invariants.Collect(symbol, compilation, diagnostics, cancellationToken);
        }

        var id = ResolveId(symbol, type, attribute, attributeName, compilation, diagnostics, cancellationToken);
        canGenerate &= id.Ok;

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

            var fieldName = "_" + char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
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

        return new EntityDefinition(
            Type: type,
            IsAggregateRoot: isAggregateRoot,
            IdType: id.IdType,
            ImplicitId: canGenerate ? id.ImplicitId : null,
            Collections: collections.ToEquatableArray(),
            Invariants: invariants,
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
    /// for, and a non-generic one because a generic entity is refused outright. Either way the element
    /// would have no <c>GetInvariantViolations</c> to call, and a walk emitted over it would turn one
    /// diagnostic on the author's own declaration into a compile error in generated code.
    /// </para>
    /// </summary>
    private static bool IsChildEntity(ITypeSymbol element)
        => element is INamedTypeSymbol { TypeKind: TypeKind.Class, IsGenericType: false }
           && (HasAttribute(element, KnownTypes.EntityAttribute) || HasAttribute(element, KnownTypes.AggregateRootAttribute));

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
            Diagnostics: EquatableArray<DiagnosticInfo>.Empty);
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
    private static T GetArgument<T>(AttributeData attribute, string parameterName, T defaultValue)
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
