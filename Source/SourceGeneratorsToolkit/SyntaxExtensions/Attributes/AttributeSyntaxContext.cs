using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace SourceGeneratorsToolkit.SyntaxExtensions.Attributes;
public class AttributeSyntaxContext(AttributeData attributeData)
{
    public AttributeData AttributeData { get; } = attributeData;
    public bool IsGeneric => AttributeData.AttributeClass?.IsGenericType ?? false;
    public IEnumerable<ITypeSymbol> GenericTypes => IsGeneric
                                       ? AttributeData.AttributeClass!.TypeArguments
                                       : Enumerable.Empty<ITypeSymbol>();

    public Dictionary<string, TypedConstant> Arguments { get; } = attributeData.ConstructorArguments
                                                .Select((arg, index) => new
                                                {
                                                    Key = attributeData.AttributeConstructor?.Parameters[index].Name,
                                                    Value = arg
                                                })
                                                .ToDictionary(arg => arg.Key!, arg => arg.Value);

    public string FriendlyName => $"{AttributeData.AttributeClass?.ContainingNamespace}.{AttributeData.AttributeClass?.Name}";

    public bool Matches<T>() where T : AttributeData
    {
        return typeof(T).FullName == FriendlyName;
    }


}


