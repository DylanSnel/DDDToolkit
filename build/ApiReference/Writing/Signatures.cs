using System.Text;
using DDDToolkit.Build.ApiReference.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DDDToolkit.Build.ApiReference.Writing;

/// <summary>
/// Declarations as C# writes them, without namespaces: every type in a signature is named as a file with the
/// packages' usings names it, nullability included. Generic constraints go on lines of their own, and a long parameter
/// list one parameter per line, so a signature reads without scrolling.
/// </summary>
internal static class Signatures
{
    private const int LongLine = 100;

    private const SymbolDisplayMiscellaneousOptions Miscellaneous =
        SymbolDisplayMiscellaneousOptions.UseSpecialTypes
        | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
        | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        | SymbolDisplayMiscellaneousOptions.AllowDefaultLiteral;

    /// <summary>A type where a signature names it: <c>TenancyActor&lt;TSeatId&gt;?</c>.</summary>
    private static readonly SymbolDisplayFormat TypeReference = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeVariance,
        miscellaneousOptions: Miscellaneous);

    /// <summary>A member's declaration up to its parameter list: <c>public static IServiceCollection AddTenancy&lt;TContext&gt;</c>.</summary>
    private static readonly SymbolDisplayFormat MemberHead = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeVariance,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility
            | SymbolDisplayMemberOptions.IncludeModifiers
            | SymbolDisplayMemberOptions.IncludeType
            | SymbolDisplayMemberOptions.IncludeRef
            | SymbolDisplayMemberOptions.IncludeConstantValue,
        kindOptions: SymbolDisplayKindOptions.IncludeMemberKeyword,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        miscellaneousOptions: Miscellaneous);

    /// <summary>One parameter: <c>TenancyActor&lt;TSeatId&gt;? by = null</c>.</summary>
    private static readonly SymbolDisplayFormat Parameter = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeDefaultValue,
        miscellaneousOptions: Miscellaneous);

    /// <summary>A member by name and parameter types, for a heading or a table: <c>Place(TUnitId, bool)</c>.</summary>
    private static readonly SymbolDisplayFormat Short = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: Miscellaneous);

    /// <summary><paramref name="type"/> as a signature names it.</summary>
    public static string Reference(ITypeSymbol type) => type.ToDisplayString(TypeReference);

    /// <summary>
    /// A type's name as the reference heads it: the types it is nested in by name, itself with its type parameters,
    /// <c>TenancyRequirement.AtUnit&lt;TUnitId&gt;</c>. The nine type parameters of <c>TenancyUseCases</c> would bury
    /// every type nested in it, and the generated class an application names them through has none.
    /// </summary>
    public static string Name(INamedTypeSymbol type)
    {
        var own = type.ToDisplayString(TypeReference);
        return type.ContainingType is { } containing ? Plain(containing) + "." + own : own;
    }

    /// <summary>A type and the types it is nested in, by name alone: <c>TenancyUseCases.SeatCommands</c>.</summary>
    public static string Plain(INamedTypeSymbol type)
        => type.ContainingType is { } containing ? Plain(containing) + "." + type.Name : type.Name;

    /// <summary>A member by name and parameter types: <c>Place(TUnitId, bool, DateTimeOffset)</c>, <c>Status</c>.</summary>
    public static string ShortName(ISymbol member)
    {
        var text = member.ToDisplayString(Short);

        // The constructor of a nested type is shown with its containing types; the type's own heading says them.
        if (member is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            return constructor.ContainingType.Name + text[text.IndexOf('(')..];
        }

        // A generic method's type parameters are in its signature; a heading of five of them and five parameters
        // reads as noise, so the name stands alone before its parameter types.
        if (member is IMethodSymbol { IsGenericMethod: true } && text.StartsWith(member.Name + "<", StringComparison.Ordinal))
        {
            var depth = 0;
            for (var index = member.Name.Length; index < text.Length; index++)
            {
                depth += text[index] switch { '<' => 1, '>' => -1, _ => 0 };
                if (depth == 0)
                {
                    return member.Name + text[(index + 1)..];
                }
            }
        }

        return text;
    }

    /// <summary>The declaration of <paramref name="type"/>, with its base types and constraints.</summary>
    public static string Declaration(INamedTypeSymbol type)
    {
        var builder = new StringBuilder();
        foreach (var attribute in type.GetAttributes())
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name is "System.AttributeUsageAttribute" or "System.FlagsAttribute")
            {
                builder.Append('[').Append(AttributeText(attribute)).Append("]\n");
            }
        }

        builder.Append(Accessibility(type.DeclaredAccessibility)).Append(' ');
        if (type.TypeKind == TypeKind.Delegate && type.DelegateInvokeMethod is { } invoke)
        {
            builder.Append("delegate ").Append(Reference(invoke.ReturnType)).Append(' ').Append(type.ToDisplayString(TypeReference))
                .Append('(').Append(string.Join(", ", invoke.Parameters.Select(parameter => parameter.ToDisplayString(Parameter)))).Append(')');
            return builder.Append(Constraints(type.TypeParameters)).ToString();
        }

        if (type.IsStatic)
        {
            builder.Append("static ");
        }
        else if (type.IsAbstract && type.TypeKind == TypeKind.Class)
        {
            builder.Append("abstract ");
        }
        else if (type.IsSealed && type.TypeKind == TypeKind.Class)
        {
            builder.Append("sealed ");
        }

        if (type is { TypeKind: TypeKind.Struct, IsReadOnly: true })
        {
            builder.Append("readonly ");
        }

        builder.Append(type switch
        {
            { TypeKind: TypeKind.Struct } when ApiSurface.IsRecord(type) => "record struct",
            _ when ApiSurface.IsRecord(type) => "record",
            { TypeKind: TypeKind.Interface } => "interface",
            { TypeKind: TypeKind.Struct } => "struct",
            { TypeKind: TypeKind.Enum } => "enum",
            _ => "class",
        });

        builder.Append(' ').Append(type.ToDisplayString(TypeReference));

        var bases = new List<string>();
        if (type.TypeKind == TypeKind.Enum)
        {
            if (type.EnumUnderlyingType is { SpecialType: not SpecialType.System_Int32 } underlying)
            {
                bases.Add(Reference(underlying));
            }
        }
        else
        {
            if (type.BaseType is { SpecialType: not (SpecialType.System_Object or SpecialType.System_ValueType) } baseType)
            {
                bases.Add(Reference(baseType));
            }

            // A record's IEquatable of itself is the compiler's, and says nothing a reader of a record does not know.
            bases.AddRange(type.Interfaces
                .Where(contract => !(ApiSurface.IsRecord(type) && contract.Name == "IEquatable" && contract.TypeArguments is [var argument] && SymbolEqualityComparer.Default.Equals(argument, type)))
                .Select(Reference));
        }

        if (bases.Count > 0)
        {
            builder.Append(" : ").Append(string.Join(", ", bases));
        }

        return builder.Append(Constraints(type.TypeParameters)).ToString();
    }

    /// <summary>The declaration of a member, as it is written in the class.</summary>
    public static string Declaration(ISymbol member)
    {
        // Roslyn writes an interface's members without modifiers, which suits an instance member, but a static one is
        // implemented by a type, not by an instance, and reads as the other without its own.
        var modifiers = member is { IsStatic: true, ContainingType.TypeKind: TypeKind.Interface }
            ? member.IsAbstract ? "static abstract " : member.IsVirtual ? "static virtual " : "static "
            : "";
        return modifiers + Written(member);
    }

    private static string Written(ISymbol member)
    {
        switch (member)
        {
            case IMethodSymbol method:
            {
                var head = method.MethodKind == MethodKind.Constructor
                    ? Accessibility(method.DeclaredAccessibility) + " " + method.ContainingType.Name
                    : method.ToDisplayString(MemberHead);
                var parameters = method.Parameters.Select(parameter => parameter.ToDisplayString(Parameter)).ToList();
                if (method.IsExtensionMethod)
                {
                    parameters[0] = "this " + parameters[0];
                }

                return WithParameters(head, "(", parameters, ")") + Constraints(method.TypeParameters);
            }

            case IPropertySymbol { IsIndexer: true } indexer:
            {
                var head = indexer.ToDisplayString(MemberHead);
                var at = head.IndexOf("this[", StringComparison.Ordinal);
                var accessors = head[(head.IndexOf(']') + 1)..];
                var parameters = indexer.Parameters.Select(parameter => parameter.ToDisplayString(Parameter)).ToList();
                return WithParameters(head[..(at + 4)], "[", parameters, "]") + accessors;
            }

            default:
                return member.ToDisplayString(MemberHead);
        }
    }

    private static string WithParameters(string head, string open, List<string> parameters, string close)
    {
        var oneLine = head + open + string.Join(", ", parameters) + close;
        return oneLine.Length <= LongLine || parameters.Count < 2
            ? oneLine
            : head + open + "\n    " + string.Join(",\n    ", parameters) + close;
    }

    private static string Constraints(IEnumerable<ITypeParameterSymbol> typeParameters)
    {
        var builder = new StringBuilder();
        foreach (var parameter in typeParameters)
        {
            var constraints = new List<string>();
            if (parameter.HasReferenceTypeConstraint)
            {
                constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            }
            else if (parameter.HasUnmanagedTypeConstraint)
            {
                constraints.Add("unmanaged");
            }
            else if (parameter.HasValueTypeConstraint)
            {
                constraints.Add("struct");
            }
            else if (parameter.HasNotNullConstraint)
            {
                constraints.Add("notnull");
            }

            constraints.AddRange(parameter.ConstraintTypes.Select(Reference));

            if (parameter.HasConstructorConstraint)
            {
                constraints.Add("new()");
            }

            if (parameter.AllowsRefLikeType)
            {
                constraints.Add("allows ref struct");
            }

            if (constraints.Count > 0)
            {
                builder.Append("\n    where ").Append(parameter.Name).Append(" : ").Append(string.Join(", ", constraints));
            }
        }

        return builder.ToString();
    }

    private static string AttributeText(AttributeData attribute)
    {
        var name = attribute.AttributeClass!.Name;
        name = name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;

        var arguments = attribute.ConstructorArguments.Select(Constant)
            .Concat(attribute.NamedArguments.Select(named => named.Key + " = " + Constant(named.Value)))
            .ToList();
        return arguments.Count == 0 ? name : name + "(" + string.Join(", ", arguments) + ")";
    }

    private static string Constant(TypedConstant constant)
    {
        if (constant.Kind == TypedConstantKind.Enum && constant.Type is INamedTypeSymbol enumType && constant.Value is { } raw)
        {
            // AttributeTargets.Class | AttributeTargets.Struct, as the flags were written.
            var value = Convert.ToInt64(raw);
            var named = enumType.GetMembers().OfType<IFieldSymbol>()
                .Where(field => field.HasConstantValue && Convert.ToInt64(field.ConstantValue) is var flag and not 0 && (value & flag) == flag)
                .Select(field => enumType.Name + "." + field.Name)
                .ToList();
            return named.Count > 0 ? string.Join(" | ", named) : value.ToString();
        }

        return constant.ToCSharpString();
    }

    private static string Accessibility(Accessibility accessibility) => accessibility switch
    {
        Microsoft.CodeAnalysis.Accessibility.Public => "public",
        Microsoft.CodeAnalysis.Accessibility.Protected => "protected",
        Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => "protected internal",
        Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "private protected",
        Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
        _ => "private",
    };
}
