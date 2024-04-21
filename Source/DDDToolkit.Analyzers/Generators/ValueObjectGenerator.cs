﻿using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Analyzers.Common;
using DDDToolkit.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGeneratorsToolkit;
using SourceGeneratorsToolkit.Providers;
using SourceGeneratorsToolkit.Providers.Contexts;
using SourceGeneratorsToolkit.SyntaxExtensions;
using System.Collections.Generic;
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

        var dddOptions = context.DDDOptionsProvider();
        var valueObjects = context.FindAttributesProvider<ValueObjectAttribute, RecordDeclarationSyntax>();
        var combined = valueObjects.Combine(dddOptions);
        context.RegisterSourceOutput(combined, CreateValueObject);
    }

    private static void CreateValueObject(SourceProductionContext context, (ResultTypeAttributeSyntaxContext data, DDDOptions options) arguments)
    {
        var (data, options) = arguments;
        var recordDeclaration = data.TargetNode as RecordDeclarationSyntax;
        if (recordDeclaration is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ValueObjectShouldBeRecord, data.TargetNode.GetLocation(), data.TargetSymbol.Name));
            return;
        }

        if (recordDeclaration.IsSealed())
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ValueObjectsCantBeSealed, recordDeclaration.GetLocation(), data.TargetSymbol.Name));
            return;
        }


        var valueObjectInfo = new ValueObjectInfo(recordDeclaration, options);




        var sourceCode = $$$"""
                            // <auto-generated/>
                            using DDDToolkit.BaseTypes;
                            using System.Text.Json.Serialization;
                            using DDDToolkit.Abstractions.Interfaces;
                            using DDDToolkit.Abstractions.Attributes;

                            #nullable enable
                            #pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

                            namespace {{{valueObjectInfo.Namespace}}};
    
                            partial record {{{valueObjectInfo.Name}}} : ValueObject
                            {
                                public override IEnumerable<object?> GetEqualityComponents()
                                {
                                    {{{string.Join("\n", valueObjectInfo.EqualityComponents)}}}
                                }
                            
                                public virtual bool Equals({{{valueObjectInfo.Name}}}? other)
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

                                [JsonConstructor]
                                protected {{{valueObjectInfo.Name}}}()
                                {

                                }

                            public Valid{{{valueObjectInfo.Name}}} ToValid() => new(this);
                                
                            }
                            {{{AddAlwaysValid(valueObjectInfo)}}}
                            #pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    
                            """;

        sourceCode = CodeFormatter.FormatSourceCode(sourceCode);
        // Add the generated source to the compilation
        context.AddSource($"{valueObjectInfo.Namespace}.{valueObjectInfo.Name}.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
    }

    private static string AddAlwaysValid(ValueObjectInfo valueObjectInfo)
    {
        var propertiesInitialization = valueObjectInfo.ConverterConstructorProperties.Select(x =>
            $"this.{x.Identifier.ValueText} = value.{x.Identifier.ValueText};");

        return $$$"""
                   

                    {{{valueObjectInfo.AccessModifier}}} partial record Valid{{{valueObjectInfo.Name}}}  : {{{valueObjectInfo.Name}}}, IAlwaysValid
                    {


                        {{{valueObjectInfo.AccessModifier}}} Valid{{{valueObjectInfo.Name}}}({{{valueObjectInfo.Name}}} value)
                        {
                            value.EnsureValidated();
                            {{{string.Join("\n", propertiesInitialization)}}}
                            _isValid = true;
                        }
                       
                        [Internal]
                        public override IEnumerable<object?> GetEqualityComponents()
                        {
                            {{{string.Join("\n", valueObjectInfo.EqualityComponents)}}}
                        }

                        public virtual bool Equals(Valid{{{valueObjectInfo.Name}}}? other)
                        {
                            if (other is null)
                            {
                                return false;
                            }
                            return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
                        }

                        public override int GetHashCode()
                                => base.GetHashCode();
                    }
            """;
    }


    internal class ValueObjectInfo(RecordDeclarationSyntax recordDeclaration, DDDOptions options)
    {
        public RecordDeclarationSyntax RecordDeclaration { get; } = recordDeclaration;
        public DDDOptions Options { get; } = options;

        public string Name => RecordDeclaration.GetName();
        public string Namespace => RecordDeclaration.GetNamespace();
        public string AccessModifier => RecordDeclaration.GetAccessModifier();
        public IEnumerable<PropertyDeclarationSyntax> InterfaceProperties => RecordDeclaration.GetProperties()
            .Where(prop => !prop.HasAttribute<InternalAttribute>());
        public IEnumerable<PropertyDeclarationSyntax> ComparisonProperties => InterfaceProperties
            .Where(prop => !prop.HasAttribute<DontCompareAttribute>());
        public IEnumerable<PropertyDeclarationSyntax> ConverterConstructorProperties => InterfaceProperties.Where(x => x.HasSetter());
        public IEnumerable<string> EqualityComponents => ComparisonProperties
            .Select(prop => $"yield return {prop.Identifier.ValueText};");
    }

}
