using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// The ways a class says it is an entity or an aggregate root, answered in one place so every generator
/// and analyzer agrees. There are three:
/// <list type="bullet">
/// <item><c>[AggregateRoot&lt;TId&gt;]</c> and <c>[Entity&lt;TId&gt;]</c>, the toolkit's own;</item>
/// <item><c>[AggregateRootBase]</c> and <c>[EntityBase]</c> on an abstract generic parent a package ships;</item>
/// <item>an attribute a package declares and marks <c>[AggregateRootTemplate]</c> or <c>[EntityTemplate]</c>,
/// such as <c>[TenantAggregate&lt;TenantId&gt;]</c>, which declares a class deriving from that package's parent.</item>
/// </list>
/// <para>
/// The attributes are the only signal there is. The base class that would prove what a type is comes from
/// the entity generator, and a generator cannot see another generator's output, nor its own. They survive
/// the trip through metadata, so a type from a referenced assembly is recognised too.
/// </para>
/// </summary>
internal static class EntityDeclarations
{
    /// <summary>Whether the type is declared an aggregate root, in any of the three ways.</summary>
    public static bool IsAggregateRoot(INamedTypeSymbol type) => DeclaredAs(type) == Declared.AggregateRoot;

    /// <summary>Whether the type is declared a child entity, in any of the three ways.</summary>
    public static bool IsEntity(INamedTypeSymbol type) => DeclaredAs(type) == Declared.Entity;

    /// <summary>Whether the type is declared an entity or an aggregate root, in any of the three ways.</summary>
    public static bool IsEntityOrAggregateRoot(INamedTypeSymbol type) => DeclaredAs(type) != Declared.None;

    /// <summary>Whether the type is an abstract parent a package ships, <c>[AggregateRootBase]</c> or <c>[EntityBase]</c>.</summary>
    public static bool IsBase(INamedTypeSymbol type)
        => DefinitionFactory.HasAttribute(type.OriginalDefinition, KnownTypes.AggregateRootBaseAttribute)
           || DefinitionFactory.HasAttribute(type.OriginalDefinition, KnownTypes.EntityBaseAttribute);

    /// <summary>
    /// The type argument that names the id: of <c>[AggregateRoot&lt;TId&gt;]</c>, <c>[Entity&lt;TId&gt;]</c> or a
    /// template attribute, or the first type argument of a parent. Null for a type declared in none of the
    /// ways, or whose attribute the compiler could not bind.
    /// </summary>
    public static ITypeSymbol? IdArgumentOf(INamedTypeSymbol type)
    {
        foreach (var attribute in type.OriginalDefinition.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass)
            {
                continue;
            }

            if (Is(attributeClass, KnownTypes.AggregateRootAttribute)
                || Is(attributeClass, KnownTypes.EntityAttribute)
                || TemplateOf(attributeClass) is not null)
            {
                return attributeClass.TypeArguments.Length > 0 && attributeClass.TypeArguments[0] is { TypeKind: not TypeKind.Error } argument ? argument : null;
            }

            if (Is(attributeClass, KnownTypes.AggregateRootBaseAttribute) || Is(attributeClass, KnownTypes.EntityBaseAttribute))
            {
                return type.TypeArguments.Length > 0 ? type.TypeArguments[0] : null;
            }
        }

        return null;
    }

    /// <summary>
    /// The open parent a class declared with a template derives from, or null for any other class. The
    /// compilation a generator sees does not show that base class yet, because the generator writes it,
    /// so anything that asks about a template class's inherited members asks the parent instead.
    /// </summary>
    public static INamedTypeSymbol? TemplateParentOf(INamedTypeSymbol type)
    {
        foreach (var attribute in type.OriginalDefinition.GetAttributes())
        {
            if (attribute.AttributeClass is { } attributeClass && TemplateOf(attributeClass) is { Parent: { } parent })
            {
                return parent;
            }
        }

        return null;
    }

    /// <summary>
    /// The name <c>Compilation.GetTypeByMetadataName</c> finds a type by: its namespace, the names of the
    /// types it is nested in joined with <c>+</c>, and the arity suffix of every generic one.
    /// </summary>
    public static string MetadataNameOf(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        if (definition.ContainingType is { } outer)
        {
            return MetadataNameOf(outer) + "+" + definition.MetadataName;
        }

        return definition.ContainingNamespace is { IsGlobalNamespace: false } scope
            ? scope.ToDisplayString() + "." + definition.MetadataName
            : definition.MetadataName;
    }

    /// <summary>
    /// What a template attribute declares, when <paramref name="attributeClass"/> is one: the parent a class
    /// declared with it derives from, and whether that class is an aggregate root or a child entity. Null
    /// for any other attribute. The parent is null when the marker names nothing the compiler could bind.
    /// </summary>
    public static TemplateMarker? TemplateOf(INamedTypeSymbol attributeClass)
    {
        var definition = attributeClass.OriginalDefinition;
        foreach (var marker in definition.GetAttributes())
        {
            if (marker.AttributeClass is not { } markerClass)
            {
                continue;
            }

            var isAggregateRoot = Is(markerClass, KnownTypes.AggregateRootTemplateAttribute);
            if (!isAggregateRoot && !Is(markerClass, KnownTypes.EntityTemplateAttribute))
            {
                continue;
            }

            var parent = marker.ConstructorArguments.Length == 1
                         && marker.ConstructorArguments[0] is { Kind: TypedConstantKind.Type, Value: INamedTypeSymbol named }
                         && named.TypeKind != TypeKind.Error
                ? named.OriginalDefinition
                : null;

            return new TemplateMarker(definition, parent, isAggregateRoot);
        }

        return null;
    }

    /// <summary>Whether the attribute class is the one with this metadata name, matched by name and namespace.</summary>
    public static bool Is(INamedTypeSymbol attributeClass, string metadataName)
    {
        var dot = metadataName.LastIndexOf('.');
        var name = metadataName.Substring(dot + 1);
        var arity = name.IndexOf('`');
        if (arity >= 0)
        {
            name = name.Substring(0, arity);
        }

        return attributeClass.Name == name && attributeClass.ContainingNamespace.ToDisplayString() == metadataName.Substring(0, dot);
    }

    private enum Declared
    {
        None,
        Entity,
        AggregateRoot,
    }

    private static Declared DeclaredAs(INamedTypeSymbol type)
    {
        foreach (var attribute in type.OriginalDefinition.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass)
            {
                continue;
            }

            if (Is(attributeClass, KnownTypes.AggregateRootAttribute) || Is(attributeClass, KnownTypes.AggregateRootBaseAttribute))
            {
                return Declared.AggregateRoot;
            }

            if (Is(attributeClass, KnownTypes.EntityAttribute) || Is(attributeClass, KnownTypes.EntityBaseAttribute))
            {
                return Declared.Entity;
            }

            if (TemplateOf(attributeClass) is { } template)
            {
                return template.IsAggregateRoot ? Declared.AggregateRoot : Declared.Entity;
            }
        }

        return Declared.None;
    }
}

/// <summary>What a template attribute's marker says.</summary>
/// <param name="Attribute">The template attribute's open definition.</param>
/// <param name="Parent">The open parent a class declared with it derives from, or null when the marker names nothing usable.</param>
/// <param name="IsAggregateRoot">True for <c>[AggregateRootTemplate]</c>, false for <c>[EntityTemplate]</c>.</param>
internal readonly record struct TemplateMarker(INamedTypeSymbol Attribute, INamedTypeSymbol? Parent, bool IsAggregateRoot);
