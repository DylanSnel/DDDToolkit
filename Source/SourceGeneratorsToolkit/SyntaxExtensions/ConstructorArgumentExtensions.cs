using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SourceGeneratorsToolkit.SyntaxExtensions;
public static class ConstructorArgumentExtensions
{
    public static T? GetArgument<T>(this Dictionary<string, TypedConstant> typedConstants, string key, T? defaultValue = default)
    {
        var argument = typedConstants.FirstOrDefault(_ => _.Key == key).Value;
        if (argument.Kind == TypedConstantKind.Array)
        {
            return !argument.Values.Any() ? defaultValue : (T)Convert.ChangeType(argument.Value, typeof(T));
        }
        else
        {
            return argument.Value is null ? defaultValue : (T)Convert.ChangeType(argument.Value, typeof(T));
        }
    }
}
