﻿using DDDToolkit.Abstractions.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGeneratorsToolkit.Providers;
using SourceGeneratorsToolkit.Providers.Contexts;
using SourceGeneratorsToolkit.SyntaxExtensions;
using System.Linq;
using System.Text;

namespace DDDToolkit.Analyzers.Generators;

[Generator]
public class ValueObjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        //#if DEBUG
        //        if (!System.Diagnostics.Debugger.IsAttached)
        //        {

        //            System.Diagnostics.Debugger.Launch();
        //        }
        //#endif

        var singleValueObjects = context.FindAttributesProvider<ValueObjectAttribute, RecordDeclarationSyntax>();
        context.RegisterSourceOutput(singleValueObjects, Execute);
    }

    private static void Execute(SourceProductionContext context, TypeAttributeSyntaxContext data)
    {
        var recordDeclaration = data.TargetNode as RecordDeclarationSyntax;
        if (recordDeclaration is null)
        {
            return;
        }
        var name = recordDeclaration.GetName();
        var @namespace = recordDeclaration.GetNamespace();
        var accessModifier = recordDeclaration.GetAccessModifier();
        var properties = recordDeclaration.GetProperties();
        var attributesWithoutIgnore = properties.Where(x => !x.Attributes().Any(a => a.Matches<DontCompareAttribute>()));

        var equalityComponents = string.Join("\n        ", attributesWithoutIgnore.Select(x => $"yield return {x.GetName()};"));

        var virtualEquals = recordDeclaration.IsSealed() ? "" : "virtual ";


        var sourceCode = $$$"""
                            // <auto-generated/>
                            using DDDToolkit.BaseTypes;

                            #nullable enable
                            namespace {{{@namespace}}};
    
                            {{{accessModifier}}} {{{recordDeclaration.SealedModifier()}}}partial record {{{name}}} : ValueObject
                            {
                                public override IEnumerable<object?> GetEqualityComponents()
                                {
                                    {{{equalityComponents}}}
                                }
                            
                                public {{{virtualEquals}}}bool Equals({{{name}}}? other)
                                {
                                    if (other is null)
                                    {
                                        return false;
                                    }
                                    return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
                                }

                                public override int GetHashCode()
                                    => GetEqualityComponents()
                                        .Select(x => x?.GetHashCode() ?? 0)
                                        .Aggregate((x, y) => x ^ y);

                            #pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
                                protected {{{name}}}()
                            #pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
                                {

                                }
                            }
    
                            """;
        // Add the generated source to the compilation
        context.AddSource($"{@namespace}.{name}.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
    }

}
