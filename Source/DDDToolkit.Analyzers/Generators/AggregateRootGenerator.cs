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
public class AggregateRootGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        //#if DEBUG
        //        if (!System.Diagnostics.Debugger.IsAttached)
        //        {

        //            System.Diagnostics.Debugger.Launch();
        //        }
        //#endif

        var entityIds = context.FindAttributesProvider<AggregateRootAttribute, ClassDeclarationSyntax>();
        context.RegisterSourceOutput(entityIds, Execute);
    }

    private static void Execute(SourceProductionContext context, TypeAttributeSyntaxContext data)
    {

        var classDeclaration = data.TargetNode as ClassDeclarationSyntax;
        if (classDeclaration is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.EntityShouldBeClass, data.TargetNode.GetLocation(), data.TargetSymbol.Name));
            return;
        }
        var name = classDeclaration.GetName();
        var @namespace = classDeclaration.GetNamespace();
        var accessModifier = classDeclaration.GetAccessModifier();
        var match = data.Attributes.First(x => x.match);
        var type = match.attribute.GenericTypes.First();
        var typeName = type.Name;

        var idNamespace = type.ContainingNamespace.ToString();

        var sourceCode = $$$"""
                            // <auto-generated/>
                            using DDDToolkit.BaseTypes;
                            using {{{idNamespace}}};

                            namespace {{{@namespace}}};
    
                            partial class {{{name}}} : AggregateRoot<{{{typeName}}}>
                            {
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
