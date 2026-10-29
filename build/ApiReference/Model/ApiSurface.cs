using System.ComponentModel;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Build.ApiReference.Model;

/// <summary>
/// What of an assembly the reference shows: what a consumer can name. Left out are what the compiler writes on its own
/// (a record's equality and printing, a class's default constructor, backing fields) and what the packages hide from
/// the editor, <c>[EditorBrowsable(Never)]</c> and <c>[Obsolete]</c>, which <see cref="Skipped"/> lists so the page
/// can say so.
/// </summary>
internal sealed class ApiSurface
{
    // A record's members the compiler writes, or a generator writes for it, and that say nothing of the type.
    private static readonly HashSet<string> RecordPlumbing =
        ["Equals", "GetHashCode", "<Clone>$", "Deconstruct", "PrintMembers", "EqualityContract", "op_Equality", "op_Inequality"];

    /// <summary>What was hidden from the editor, with why, in the order it was met.</summary>
    public List<(ISymbol Symbol, string Reason)> Skipped { get; } = [];

    /// <summary>How many members the compiler wrote that were left out.</summary>
    public int CompilerWritten { get; private set; }

    /// <summary>
    /// The types of <paramref name="assembly"/> a consumer can name, nested types included; with <paramref name="take"/>,
    /// only those it takes, and a type it does not take is not looked into.
    /// </summary>
    public IEnumerable<INamedTypeSymbol> TypesOf(IAssemblySymbol assembly, Func<INamedTypeSymbol, bool>? take = null)
        => TypesIn(assembly.GlobalNamespace, take ?? (_ => true));

    /// <summary>The members of <paramref name="type"/> a consumer can name, nested types excluded, in declaration order.</summary>
    public IReadOnlyList<ISymbol> MembersOf(INamedTypeSymbol type)
    {
        var members = new List<ISymbol>();
        foreach (var member in type.GetMembers())
        {
            if (member is INamedTypeSymbol || !IsReachable(member, type))
            {
                continue;
            }

            if (member is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise or MethodKind.StaticConstructor or MethodKind.Destructor })
            {
                continue;
            }

            if (IsCompilerWritten(member, type))
            {
                CompilerWritten++;
                continue;
            }

            if (HiddenBecause(member) is { } reason)
            {
                Skipped.Add((member, reason));
                continue;
            }

            members.Add(member);
        }

        return members;
    }

    private IEnumerable<INamedTypeSymbol> TypesIn(INamespaceOrTypeSymbol container, Func<INamedTypeSymbol, bool> take)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                foreach (var type in TypesIn(ns, take))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol type && take(type) && IsVisible(type))
            {
                yield return type;
                foreach (var nested in TypesIn(type, take))
                {
                    yield return nested;
                }
            }
        }
    }

    private bool IsVisible(INamedTypeSymbol type)
    {
        if (type.Name.StartsWith('<') || HasAttribute(type, "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
        {
            return false;
        }

        if (type.ContainingType is { } containing ? !IsReachable(type, containing) : type.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (HiddenBecause(type) is { } reason)
        {
            Skipped.Add((type, reason));
            return false;
        }

        return true;
    }

    /// <summary>Public, or protected in a type a consumer can derive from.</summary>
    private static bool IsReachable(ISymbol member, INamedTypeSymbol containing) => member.DeclaredAccessibility switch
    {
        Accessibility.Public => true,
        Accessibility.Protected or Accessibility.ProtectedOrInternal => !containing.IsSealed && !containing.IsStatic && containing.TypeKind == TypeKind.Class,
        _ => false,
    };

    private static bool IsCompilerWritten(ISymbol member, INamedTypeSymbol type)
    {
        if (member.IsImplicitlyDeclared || member.Name.StartsWith('<') || HasAttribute(member, "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
        {
            return true;
        }

        if (IsRecord(type) && (RecordPlumbing.Contains(member.Name) || IsCopyConstructor(member, type)))
        {
            return true;
        }

        // The default constructor C# writes for a class that declares none: it has no comment to carry, and CS1591 never
        // asks for one. A parameterless constructor someone wrote has a comment or an attribute.
        return member is IMethodSymbol { MethodKind: MethodKind.Constructor, Parameters.Length: 0, IsStatic: false }
            && member.GetAttributes().Length == 0
            && string.IsNullOrWhiteSpace(member.GetDocumentationCommentXml());
    }

    /// <summary>
    /// Whether <paramref name="type"/> was declared a record. Metadata marks a record class by its clone method; a record
    /// struct has none, and is known by the <c>ToString</c> the compiler wrote for it.
    /// </summary>
    public static bool IsRecord(INamedTypeSymbol type)
        => type.IsRecord
           || (type.TypeKind == TypeKind.Struct
               && type.GetMembers("ToString").Any(member => HasAttribute(member, "System.Runtime.CompilerServices.CompilerGeneratedAttribute")));

    private static bool IsCopyConstructor(ISymbol member, INamedTypeSymbol type)
        => member is IMethodSymbol { MethodKind: MethodKind.Constructor, Parameters: [var only] }
           && SymbolEqualityComparer.Default.Equals(only.Type.OriginalDefinition, type.OriginalDefinition);

    private static string? HiddenBecause(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case "System.ComponentModel.EditorBrowsableAttribute"
                    when attribute.ConstructorArguments is [{ Value: int state }] && state == (int)EditorBrowsableState.Never:
                    return "[EditorBrowsable(Never)]";
                case "System.ObsoleteAttribute":
                    return "[Obsolete]";
            }
        }

        return null;
    }

    private static bool HasAttribute(ISymbol symbol, string name)
        => symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == name);
}
