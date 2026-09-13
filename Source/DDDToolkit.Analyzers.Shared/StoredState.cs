using System;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// What a type stores, and what that stored state points at. Shared by the two boundary rules that
/// care: <see cref="AggregateBoundary"/> (DDD00021, one aggregate holding another) and
/// <see cref="ModuleBoundary"/> (DDD00023, one module holding another module's entity).
/// <para>
/// Both look at stored state only, meaning the instance fields and properties of a type, because those
/// are exactly what Entity Framework turns into a navigation and what a load therefore drags along.
/// Method parameters and return types are left alone: passing another entity into a domain method is
/// how two of them are meant to cooperate, and nothing is stored by doing it.
/// </para>
/// </summary>
internal static class StoredState
{
    /// <summary>How far to dig through arrays, nullables and collections before giving up.</summary>
    private const int MaxDepth = 5;

    /// <summary>
    /// The type a member stores, or null when the member stores nothing. Static members are state of the
    /// type rather than of one instance, and a compiler-generated field (the backing field of an auto
    /// property) would report the property a second time under a mangled name.
    /// </summary>
    public static ITypeSymbol? StoredTypeOf(ISymbol member)
    {
        if (member.IsStatic || member.IsImplicitlyDeclared)
        {
            return null;
        }

        return member switch
        {
            IPropertySymbol { IsIndexer: false, ExplicitInterfaceImplementations.IsEmpty: true } property => property.Type,
            IFieldSymbol { IsConst: false, AssociatedSymbol: null } field => field.Type,
            _ => null,
        };
    }

    /// <summary>A type reached from a member, and whether it was reached through a collection.</summary>
    public readonly struct Held
    {
        public Held(INamedTypeSymbol type, bool viaCollection)
        {
            Type = type;
            ViaCollection = viaCollection;
        }

        public INamedTypeSymbol Type { get; }

        /// <summary>True when the member holds many of them. A back-navigation is always single valued.</summary>
        public bool ViaCollection { get; }
    }

    /// <summary>
    /// The first type matching <paramref name="match"/> that a member type holds, looking through arrays,
    /// <c>Nullable&lt;T&gt;</c> and anything enumerable, so <c>Customer</c>, <c>Customer?</c>,
    /// <c>Customer[]</c>, <c>IReadOnlyList&lt;Customer&gt;</c> and
    /// <c>Dictionary&lt;OrderId, Customer&gt;</c> all count.
    /// </summary>
    public static Held? Find(ITypeSymbol type, Func<INamedTypeSymbol, bool> match)
        => Find(type, match, viaCollection: false, depth: 0);

    private static Held? Find(ITypeSymbol type, Func<INamedTypeSymbol, bool> match, bool viaCollection, int depth)
    {
        if (depth > MaxDepth)
        {
            return null;
        }

        if (type is IArrayTypeSymbol array)
        {
            return Find(array.ElementType, match, viaCollection: true, depth + 1);
        }

        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && named.TypeArguments.Length == 1)
        {
            return Find(named.TypeArguments[0], match, viaCollection, depth + 1);
        }

        if (match(named))
        {
            return new Held(named, viaCollection);
        }

        if (named.TypeArguments.Length > 0 && IsEnumerable(named))
        {
            foreach (var argument in named.TypeArguments)
            {
                var found = Find(argument, match, viaCollection: true, depth + 1);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>Whether the type is a collection the traversal should look inside.</summary>
    public static bool IsEnumerable(INamedTypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Collections_IEnumerable
            || type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return true;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (@interface.SpecialType == SpecialType.System_Collections_IEnumerable)
            {
                return true;
            }
        }

        return false;
    }
}
