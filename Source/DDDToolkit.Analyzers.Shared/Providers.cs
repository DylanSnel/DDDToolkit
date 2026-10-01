using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// Incremental providers for each DDDToolkit attribute. They match by metadata name, so a type
/// carrying the attribute is found regardless of what kind of declaration it is; the factory
/// then reports a diagnostic when the declaration kind is wrong instead of silently skipping it.
/// </summary>
internal static class Providers
{
    /// <summary>
    /// Every entity id in the compilation, whichever of the two ways it was asked for: an explicit
    /// <c>[EntityId&lt;T&gt;]</c> declaration, or an <c>[AggregateRoot&lt;Guid&gt;]</c> or <c>[Entity&lt;Guid&gt;]</c>
    /// declaration that names a raw value and so asks for an id to be generated with the entity.
    /// <para>
    /// Every generator that deals with ids reads them from here. It has to: a generator cannot see
    /// another generator's output, so an implicit id carries no <c>[EntityId]</c> attribute anything
    /// else could find. Consuming one provider is what keeps the id type, its Entity Framework value
    /// converter, its GraphQL binding and its registration entries in step.
    /// </para>
    /// </summary>
    public static IncrementalValuesProvider<EntityIdDefinition> EntityIds(this IncrementalGeneratorInitializationContext context)
    {
        var declared = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.EntityIdAttribute,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (syntaxContext, cancellationToken) => DefinitionFactory.CreateEntityId(syntaxContext, cancellationToken));

        return declared.Collect()
            .Combine(context.Entities().ImplicitIds().Collect())
            .Combine(context.AggregateRoots().ImplicitIds().Collect())
            .SelectMany(static (all, _) => all.Left.Left.AddRange(all.Left.Right).AddRange(all.Right));
    }

    /// <summary>The ids these entity declarations ask the toolkit to generate, skipping those that name an id of their own.</summary>
    private static IncrementalValuesProvider<EntityIdDefinition> ImplicitIds(this IncrementalValuesProvider<EntityDefinition> entities)
        => entities
            .Where(static definition => definition.ImplicitId is not null)
            .Select(static (definition, _) => definition.ImplicitId!);

    public static IncrementalValuesProvider<SingleValueObjectDefinition> SingleValueObjects(this IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.SingleValueObjectAttribute,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (syntaxContext, cancellationToken) => DefinitionFactory.CreateSingleValueObject(syntaxContext, cancellationToken));

    public static IncrementalValuesProvider<ValueObjectDefinition> ValueObjects(this IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.ValueObjectAttribute,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (syntaxContext, cancellationToken) => DefinitionFactory.CreateValueObject(syntaxContext, cancellationToken));

    public static IncrementalValuesProvider<EntityDefinition> Entities(this IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.EntityAttribute,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (syntaxContext, cancellationToken) => DefinitionFactory.CreateEntity(syntaxContext, isAggregateRoot: false, cancellationToken));

    public static IncrementalValuesProvider<EntityDefinition> AggregateRoots(this IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.AggregateRootAttribute,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (syntaxContext, cancellationToken) => DefinitionFactory.CreateEntity(syntaxContext, isAggregateRoot: true, cancellationToken));

    /// <summary>The abstract parents a package ships for aggregate roots declared elsewhere: <c>[AggregateRootBase]</c>.</summary>
    public static IncrementalValuesProvider<EntityDefinition> AggregateRootBases(this IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.AggregateRootBaseAttribute,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (syntaxContext, cancellationToken) => DefinitionFactory.CreateEntityBase(syntaxContext, isAggregateRoot: true, cancellationToken));

    /// <summary>The abstract parents a package ships for child entities declared elsewhere: <c>[EntityBase]</c>.</summary>
    public static IncrementalValuesProvider<EntityDefinition> EntityBases(this IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.EntityBaseAttribute,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (syntaxContext, cancellationToken) => DefinitionFactory.CreateEntityBase(syntaxContext, isAggregateRoot: false, cancellationToken));

    /// <summary>
    /// Every class declared with a package's template attribute, such as <c>[TenantAggregate&lt;TenantId&gt;]</c>,
    /// with the parent it derives from closed over the application's own types.
    /// <para>
    /// The toolkit cannot name these attributes in advance, so this is the one provider that looks at
    /// every type declaration with an attribute on it and asks the attribute's class whether it is a
    /// template. The answer is a plain equatable definition like any other, so what follows the lookup
    /// runs again only when a template class changes. The definitions are collected before they are
    /// resolved because a parent may take a type argument from another template class of the project, and
    /// resolved against the compilation because that class may be in a project this one references, and
    /// because the parent's constraints are checked against the classes it is closed over. What comes out
    /// compares equal when nothing changed, so the output steps stay cached.
    /// </para>
    /// </summary>
    public static IncrementalValuesProvider<EntityDefinition> TemplateEntities(this IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 } declaration && HasGenericAttribute(declaration),
                transform: static (syntaxContext, cancellationToken) => DefinitionFactory.CreateTemplateEntity(syntaxContext, cancellationToken))
            .Where(static definition => definition is not null)
            .Select(static (definition, _) => definition!)
            .Collect()
            .Combine(context.CompilationProvider)
            .SelectMany(static (all, cancellationToken) => DefinitionFactory.ResolveTemplates(all.Left, all.Right, cancellationToken));

    /// <summary>
    /// Whether a declaration carries a generic attribute, which is what a template always is: its first
    /// type argument is the id. A syntactic check, so the semantic model is asked only about declarations
    /// that could be template classes, and a project full of other attributes pays nothing for templates.
    /// </summary>
    private static bool HasGenericAttribute(TypeDeclarationSyntax declaration)
    {
        foreach (var list in declaration.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                if (attribute.Name is GenericNameSyntax
                    or QualifiedNameSyntax { Right: GenericNameSyntax }
                    or AliasQualifiedNameSyntax { Name: GenericNameSyntax })
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>DDD00046 on every template attribute this project declares that does not fit its parent.</summary>
    public static IncrementalValuesProvider<DiagnosticInfo> TemplateAttributeProblems(this IncrementalGeneratorInitializationContext context)
    {
        var roots = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.AggregateRootTemplateAttribute,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (syntaxContext, _) => DefinitionFactory.CheckTemplateAttribute(syntaxContext));
        var entities = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.EntityTemplateAttribute,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (syntaxContext, _) => DefinitionFactory.CheckTemplateAttribute(syntaxContext));

        return roots.Collect().Combine(entities.Collect())
            .SelectMany(static (all, _) => all.Left.AddRange(all.Right))
            .Where(static diagnostic => diagnostic is not null)
            .Select(static (diagnostic, _) => diagnostic!);
    }

    /// <summary>
    /// DDD00028 for every <c>[KeyPart]</c> property on a type that is neither an entity nor an
    /// aggregate root. Key parts on those types are read by <see cref="Entities"/> and
    /// <see cref="AggregateRoots"/> and never appear here.
    /// </summary>
    public static IncrementalValuesProvider<DiagnosticInfo> MisplacedKeyParts(this IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.ForAttributeWithMetadataName(
                KnownTypes.KeyPartAttribute,
                predicate: static (node, _) => node is PropertyDeclarationSyntax,
                transform: static (syntaxContext, _) => DefinitionFactory.CheckKeyPartPlacement(syntaxContext))
            .Where(static diagnostic => diagnostic is not null)
            .Select(static (diagnostic, _) => diagnostic!);

    /// <summary>The compilation's assembly name, as a cacheable value.</summary>
    public static IncrementalValueProvider<string?> AssemblyName(this IncrementalGeneratorInitializationContext context)
        => context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName);
}
