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

        var idType = GetTypeArgument(attribute, compilation).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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
            IdType: idType,
            Collections: collections.ToEquatableArray(),
            EfBackingFieldAttributeAvailable: HasType(compilation, KnownTypes.EfBackingFieldAttribute),
            ReadOnlySetAvailable: HasType(compilation, KnownTypes.ReadOnlySet),
            CanGenerate: canGenerate,
            Diagnostics: diagnostics.ToEquatableArray());
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
    {
        if (attribute.AttributeClass is { TypeArguments.Length: > 0 } attributeClass && attributeClass.TypeArguments[0] is not IErrorTypeSymbol)
        {
            return attributeClass.TypeArguments[0];
        }

        return compilation.GetSpecialType(SpecialType.System_Object);
    }

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

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        var expectedName = metadataName.Substring(metadataName.LastIndexOf('.') + 1);
        var expectedNamespace = metadataName.Substring(0, metadataName.LastIndexOf('.'));

        return symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass is { } attributeClass
            && attributeClass.Name == expectedName
            && attributeClass.ContainingNamespace.ToDisplayString() == expectedNamespace);
    }

    private static bool HasType(Compilation compilation, string metadataName) => compilation.GetTypeByMetadataName(metadataName) is not null;
}
