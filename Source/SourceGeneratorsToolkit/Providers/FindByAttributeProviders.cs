using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceGeneratorsToolkit.Providers.Contexts;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SourceGeneratorsToolkit.Providers;
public static class FindByAttributeProviders
{
    //public static IncrementalValueProvider<ImmutableArray<AttributeSyntaxContext>> GetAttributeProvider<TAttribute>(this IncrementalGeneratorInitializationContext context) where TAttribute : Attribute
    //    => GetAttributeProvider(context, typeof(TAttribute));

    //private static IncrementalValueProvider<ImmutableArray<AttributeSyntaxContext>> GetAttributeProvider(this IncrementalGeneratorInitializationContext context, Type? attributeType = null)
    //{
    //    var provider = context.SyntaxProvider.CreateSyntaxProvider(
    //                       predicate: static (node, _) => node is AttributeSyntax,
    //                       transform: (ctx, cancellationToken) =>
    //                       {

    //                           //var types = ctx.SemanticModel.Compilation.SourceModule.ReferencedAssemblySymbols.SelectMany(a =>
    //                           //{
    //                           //    try
    //                           //    {
    //                           //        var main = a.Identity.Name.Split('.').Aggregate(a.GlobalNamespace, (s, c) => s.GetNamespaceMembers().Single(m => m.Name.Equals(c)));

    //                           //        return GetAllTypes(main);
    //                           //    }
    //                           //    catch
    //                           //    {
    //                           //        return Enumerable.Empty<ITypeSymbol>();
    //                           //    }
    //                           //});

    //                           if (ctx.Node is not AttributeSyntax attributeSyntax)
    //                           {
    //                               return default;
    //                           }

    //                           var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node);
    //                           if (symbol is null)
    //                           {
    //                               return default;
    //                           }
    //                           //if (!symbol.IsDerivedFromType(attributeType?.FullName ?? typeof(Attribute).FullName))
    //                           //{
    //                           //    return default;
    //                           //}
    //                           return new AttributeSyntaxContext(ctx.Node, symbol, ctx.SemanticModel);
    //                       }).Where(x => x is not null)!.Collect();

    //    return provider!;
    //}

    private static IEnumerable<ITypeSymbol> GetAllTypes(INamespaceSymbol root)
    {
        foreach (var namespaceOrTypeSymbol in root.GetMembers())
        {
            if (namespaceOrTypeSymbol is INamespaceSymbol @namespace)
                foreach (var nested in GetAllTypes(@namespace))
                    yield return nested;

            else if (namespaceOrTypeSymbol is ITypeSymbol type)
                yield return type;
        }
    }

    public static IncrementalValuesProvider<TypeAttributeSyntaxContext> FindAttributesProvider<TAttribute, TType>(this IncrementalGeneratorInitializationContext context) where TAttribute : Attribute where TType : MemberDeclarationSyntax
    => context.FindAttributesProvider<TAttribute, TType, TypeAttributeSyntaxContext>((ctx, _) => ctx);

    public static IncrementalValuesProvider<TypeAttributeSyntaxContext> FindAttributesProvider<TAttribute, TType>(this IncrementalGeneratorInitializationContext context, Func<SyntaxNode, CancellationToken, bool> additionalPredicate) where TAttribute : Attribute where TType : MemberDeclarationSyntax
    => context.FindAttributesProvider<TAttribute, TType, TypeAttributeSyntaxContext>(additionalPredicate: additionalPredicate, transform: (ctx, _) => ctx);

    public static IncrementalValuesProvider<TReturn> FindAttributesProvider<TAttribute, TType, TReturn>(this IncrementalGeneratorInitializationContext context,
                                                                   Func<TypeAttributeSyntaxContext, CancellationToken, TReturn> transform) where TAttribute : Attribute where TType : MemberDeclarationSyntax
        => context.FindAttributesProvider<TAttribute, TType, TReturn>(null, transform);

    public static IncrementalValuesProvider<TReturn> FindAttributesProvider<TAttribute, TType, TReturn>(this IncrementalGeneratorInitializationContext context,
                                                                    Func<SyntaxNode, CancellationToken, bool>? additionalPredicate,
                                                                    Func<TypeAttributeSyntaxContext, CancellationToken, TReturn> transform,
                                                                    IncrementalValueProvider<ImmutableArray<AttributeSyntaxContext>>? attributeProvider = null) where TAttribute : Attribute where TType : MemberDeclarationSyntax
    {
        return context.HandleFindAttributesProvide<TType, TReturn>(typeof(TAttribute), additionalPredicate, transform, attributeProvider);


    }
    public static IncrementalValuesProvider<TReturn> FindAttributesProvider<TAttribute, TType, TReturn>(this IncrementalGeneratorInitializationContext context,
                                                                   Func<TypeAttributeSyntaxContext, CancellationToken, TReturn> transform,
                                                                   IncrementalValueProvider<ImmutableArray<AttributeSyntaxContext>>? attributeProvider = null) where TAttribute : Attribute where TType : MemberDeclarationSyntax
    {
        return context.HandleFindAttributesProvide<TType, TReturn>(typeof(TAttribute), null, transform, attributeProvider);


    }


    public static IncrementalValuesProvider<TReturn> FindAttributesProvider<TType, TReturn>(this IncrementalGeneratorInitializationContext context,
                                                                   Type attributeType,
                                                                   Func<SyntaxNode, CancellationToken, bool> additionalPredicate,
                                                                   Func<TypeAttributeSyntaxContext, CancellationToken, TReturn> transform,
                                                                   IncrementalValueProvider<ImmutableArray<AttributeSyntaxContext>>? attributeProvider = null) where TType : MemberDeclarationSyntax
    {
        return context.HandleFindAttributesProvide<TType, TReturn>(attributeType, additionalPredicate, transform, attributeProvider);


    }

    public static IncrementalValuesProvider<TReturn> HandleFindAttributesProvide<TType, TReturn>(this IncrementalGeneratorInitializationContext context,
                                                            Type attributeType,
                                                            Func<SyntaxNode, CancellationToken, bool>? additionalPredicate,
                                                            Func<TypeAttributeSyntaxContext, CancellationToken, TReturn> transform,
                                                            IncrementalValueProvider<ImmutableArray<AttributeSyntaxContext>>? attributeProvider = null)
                                                            where TType : MemberDeclarationSyntax
    {
        return context.SyntaxProvider.CreateSyntaxProvider(
          predicate: (node, _) => node is TType type && type.AttributeLists.Count > 0 && (additionalPredicate?.Invoke(node, _) ?? true),
          transform: (ctx, cancellationToken) =>
            {
                if (ctx.Node is not TType)
                {
                    return default;
                }

                var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) as INamedTypeSymbol;
                if (symbol is null)
                {
                    return default;
                }

                var memberAttributes = symbol.GetAttributes();
                if (memberAttributes.Length == 0)
                {
                    return default;
                }
                List<(AttributeSyntaxContext attribute, bool match)> attributeList = memberAttributes.Select(x =>
                {
                    var attData = new AttributeSyntaxContext(x);
                    return (attData, attData.Matches(attributeType));
                }).ToList();

                if (!attributeList.Exists(x => x.match))
                {
                    var x = default(TReturn);
                    return x;
                }
                return transform(new TypeAttributeSyntaxContext(ctx.Node, symbol, ctx.SemanticModel, attributeList.ToImmutableArray()), cancellationToken);
            }
        ).Where(x => x is not null)!;
    }
}


