﻿using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Analyzers.Common;
using DDDToolkit.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGeneratorsToolkit;
using SourceGeneratorsToolkit.Providers;
using SourceGeneratorsToolkit.Providers.Contexts;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace DDDToolkit.HotChocolate.Analyzers.Generators;

[Generator]
public class SingleValueObjectConverterGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {

        //#if DEBUG
        //        if (!System.Diagnostics.Debugger.IsAttached)
        //        {
        //            System.Diagnostics.Debugger.Launch();
        //        }
        //#endif

        var dddPropertiesProvider = context.DDDOptionsProvider();


        var singleValueObjectSyntax = context.FindAttributesProvider<SingleValueObjectAttribute, RecordDeclarationSyntax>();
        var entityIdSyntax = context.FindAttributesProvider<EntityIdAttribute, RecordDeclarationSyntax>();
        var typeDeclarations = singleValueObjectSyntax.Collect().Combine(entityIdSyntax.Collect()).SelectMany((x, y) => x.Left.Concat(x.Right));

        var singleValueObjects = typeDeclarations.Select((ctx, _) => GenericObjectDefinition.FromTypeDeclarationSyntax(ctx));


        context.RegisterSourceOutput(singleValueObjects, GenerateValueConverter);

        var classesWithProperties = dddPropertiesProvider.Combine(typeDeclarations.Collect());
        var classesWithPropertiesAndCompilation = classesWithProperties.Combine(context.CompilationProvider);

        context.RegisterSourceOutput(classesWithPropertiesAndCompilation, GenerateConfigurationExtensions);
    }
    private static void GenerateValueConverter(SourceProductionContext context, GenericObjectDefinition data)
    {
        var converterName = $"{data.Name}Converter";

        var sourceCode = $$$"""
                            // <auto-generated/>
                            using HotChocolate.Utilities;
                            using System.Diagnostics.CodeAnalysis;

                            namespace {{{data.Namespace}}};
    
                            partial record {{{data.Name}}} 
                            {
                                public class ChangeTypeProvider : IChangeTypeProvider
                                {
                                    public bool TryCreateConverter(
                                        Type source,
                                        Type target,
                                        global::HotChocolate.Utilities.ChangeTypeProvider root,
                                        [NotNullWhen(true)] out ChangeType? converter)
                                    {
                                        if (source == typeof({{{data.Name}}}) && target == typeof({{{data.Type}}}))
                                        {
                                            converter = value => (({{{data.Name}}})value!).Value;
                                            return true;
                                        }

                                        if (source == typeof(Valid{{{data.Name}}}) && target == typeof({{{data.Type}}}))
                                        {
                                            converter = value => ((Valid{{{data.Name}}})value!).Value;
                                            return true;
                                        }

                                        // Handle conversion from {{{data.Type}}} to {{{data.Name}}}/Valid{{{data.Name}}} if needed
                                        if (source == typeof({{{data.Type}}}) && target == typeof({{{data.Name}}}))
                                        {
                                            converter = value => new {{{data.Name}}}(({{{data.Type}}})value!);
                                            return true;
                                        }

                                        if (source == typeof({{{data.Type}}}) && target == typeof(Valid{{{data.Name}}}))
                                        {
                                            converter = value => new Valid{{{data.Name}}}(({{{data.Type}}})value!);
                                            return true;
                                        }

                                        converter = null;
                                        return false;
                                    }
                                }
                            }

                            """;

        context.AddSource($"{data.Namespace}.{converterName}.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
    }



    private static void GenerateConfigurationExtensions(SourceProductionContext sourceContext, ((DDDOptions Properties, ImmutableArray<ResultTypeAttributeSyntaxContext> records) data, Compilation compilation) context)
    {
        var records = context.data.records;
        var properties = context.data.Properties;
        var compilation = context.compilation;
        if (records.Length == 0)
        {
            return;
        }

        var assemblyName = compilation.AssemblyName;
        var moduleName = properties.ModuleName;

        if (string.IsNullOrEmpty(moduleName))
        {
            moduleName = CreateModuleName(assemblyName);
        }

        StringBuilder runtimeBindings = new();

        foreach (var record in records)
        {
            var objectDefinition = GenericObjectDefinition.FromTypeDeclarationSyntax(record);
            var (attribute, match) = record.Attributes.FirstOrDefault(x => x.attribute.AttributeData.AttributeClass?.Name == "GraphQLTypeAttribute");

            if (attribute != null)
            {
                var graphQLType = attribute.AttributeData.AttributeClass?.TypeArguments.FirstOrDefault();
                if (graphQLType != null)
                {
                    var graphQLTypeName = graphQLType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    runtimeBindings.AppendLine($"builder.BindRuntimeType<{objectDefinition.Namespace}.{objectDefinition.Name}, {graphQLTypeName}>();");
                }
            }
            else
            {
                if (_typeMappings.TryGetValue(objectDefinition.Type, out var graphQLType))
                {
                    runtimeBindings.AppendLine($"builder.BindRuntimeType<{objectDefinition.Namespace}.{objectDefinition.Name}, {graphQLType}>();");
                }
                runtimeBindings.AppendLine($"builder.AddTypeConverter<{objectDefinition.Namespace}.{objectDefinition.Name}.ChangeTypeProvider>();");
            }
        }

        var sourceCode = $$"""
        // <auto-generated/>
        using HotChocolate.Execution.Configuration;
        using HotChocolate.Types;
        using Microsoft.Extensions.DependencyInjection;
        using DDDToolkit.HotChocolate;

        namespace {{assemblyName}}.GraphQl;
        
        public static class HotChocolateExtensions
        {
            public static IRequestExecutorBuilder Add{{moduleName}}GraphQlRuntimeBindings(this IRequestExecutorBuilder builder)
            {
                {{runtimeBindings}}
                builder.RegisterDDDTypes();
                return builder;
            }
        }
        """;
        sourceCode = CodeFormatter.FormatSourceCode(sourceCode);
        sourceContext.AddSource("BindingExtensions.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
    }



    private static string CreateModuleName(string? assemblyName)
        => assemblyName is null
            ? "AssemblyTypes"
            : assemblyName.Replace(".", string.Empty);

    private static readonly Dictionary<string, string> _typeMappings = new()
    {
        { "String", "StringType" },
        { "string", "StringType" },
        { "int", "IntType" },
        { "long", "LongType" },
        { "float", "FloatType" },
        { "double", "FloatType" },
        { "decimal", "DecimalType" },
        { "bool", "BooleanType" },
        { "Boolean", "BooleanType" },
        { "TimeOnly", "DateTimeType" },
        { "DateOnly", "DateTimeType" },
        { "DateTime", "DateTimeType" },
        { "DateTimeOffset", "DateTimeType" },
        { "Guid", "UuidType" },
    };

}


