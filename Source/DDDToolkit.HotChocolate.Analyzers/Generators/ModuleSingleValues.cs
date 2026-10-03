using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.HotChocolate.Analyzers;

/// <summary>
/// The ids and single value objects of other projects that the project building a module's schema has to bind,
/// because nobody else will: those that implement <c>ISingleValue&lt;TSelf, TValue&gt;</c> and have no nested
/// <c>ChangeTypeProvider</c>, which is what a type declared in a project without DDDToolkit.HotChocolate looks like.
/// <list type="bullet">
///   <item><description>Every one of an assembly of this project's own module: its domain and contracts projects.</description></item>
///   <item><description>The published ones, <c>[ModuleContract]</c>, of an assembly of another module, which are the
///   only ones this module may name (DDD00022 allows nothing else).</description></item>
/// </list>
/// An assembly that declares no module is left alone: a package or a shared kernel without a module is not something
/// a module's bindings can speak for. A project without a module binds only its own. A type that has a nested
/// provider keeps it: its project referenced DDDToolkit.HotChocolate and binds it in its own
/// <c>Add{Module}GraphQlRuntimeBindings()</c>. A type this project cannot name is left out as well: one that is
/// not accessible from it, or whose value is declared in an assembly it does not reference.
/// <para>
/// A module can have more than one project that references DDDToolkit.HotChocolate, each with bindings of the same
/// name, and the one that references the other calls it. What the called one bound for the module's projects
/// without DDDToolkit.HotChocolate is then left out here, so one call binds each type once: see <see cref="Covers"/>.
/// </para>
/// <para>
/// The Entity Framework generator has a file of this name with the same rules, for value converters. They are two
/// scans and not one shared: what marks a type as taken care of, and what is read off it, differ per integration.
/// </para>
/// </summary>
internal static class ModuleSingleValues
{
    /// <summary>The provider the bindings use for them, which only a project referencing DDDToolkit.HotChocolate can name.</summary>
    public const string ProviderMetadataName = "DDDToolkit.HotChocolate.Types.SingleValueChangeTypeProvider`2";

    /// <summary>The node id serializer the bindings use for the identifiers among them.</summary>
    public const string NodeIdSerializerMetadataName = "DDDToolkit.HotChocolate.Types.SingleValueNodeIdSerializer`2";

    /// <summary>The serializer that makes a struct id a key HotChocolate's paging can order a list by.</summary>
    public const string CursorKeySerializerMetadataName = "DDDToolkit.HotChocolate.Paging.SingleValueCursorKeySerializer`2";

    /// <summary>That serializer as generated code names it, before its type arguments.</summary>
    public const string CursorKeySerializer = "global::DDDToolkit.HotChocolate.Paging.SingleValueCursorKeySerializer";

    /// <summary>The generated bindings class, under the namespace of the assembly it is generated into.</summary>
    public const string RegistrationClass = "GraphQl.HotChocolateExtensions";

    /// <summary>What the generated bindings method's name ends in.</summary>
    public const string RegistrationSuffix = "GraphQlRuntimeBindings";

    /// <summary>The nested class the generator writes into a type whose own project binds it.</summary>
    private const string NestedProvider = "ChangeTypeProvider";

    private const string Provider = "global::DDDToolkit.HotChocolate.Types.SingleValueChangeTypeProvider";

    private const string NodeIdSerializer = "global::DDDToolkit.HotChocolate.Types.SingleValueNodeIdSerializer";

    /// <summary>
    /// One type of a referenced assembly as the scan found it, before this compilation decides whether it takes it:
    /// that depends on this project's module and on what it can see.
    /// </summary>
    /// <param name="Symbol">The id, single value object or twin.</param>
    /// <param name="Type">It, fully qualified.</param>
    /// <param name="ValueSymbol">The value it holds.</param>
    /// <param name="Value">The value it holds, fully qualified.</param>
    /// <param name="ValueName">The value's type name, which is what a default scalar and a node id value are known by.</param>
    /// <param name="SchemaType">The schema type its <c>[GraphQLType&lt;T&gt;]</c> names, or null when it has none.</param>
    /// <param name="IsIdentifier">Whether it is an identifier itself, rather than a single value object or a twin.</param>
    /// <param name="IsComparableStruct">Whether it is a struct that compares to itself, as a generated struct id does: what paging needs of a key.</param>
    /// <param name="Published">Whether another module may name it.</param>
    private sealed record Found(INamedTypeSymbol Symbol, string Type, ITypeSymbol ValueSymbol, string Value, string ValueName, ITypeSymbol? SchemaType, bool IsIdentifier, bool IsComparableStruct, bool Published);

    /// <summary>
    /// The candidates of a referenced assembly. Walking every type of an assembly is the expensive part, and a
    /// referenced assembly does not change while the project that references it is edited: the compiler keeps the
    /// same symbol for it across compilations, so it is walked once for as long as the symbol lives.
    /// </summary>
    private static readonly ConditionalWeakTable<IAssemblySymbol, Found[]> ByAssembly = new();

    /// <summary>
    /// The bindings this compilation writes for other projects' types, sorted, so the generated file does not
    /// change when the references are handed over in another order. Empty when the project declares no module or
    /// cannot name <see cref="ProviderMetadataName"/>.
    /// </summary>
    /// <param name="compilation">The project the bindings are written into.</param>
    /// <param name="defaultScalars">The schema type a value is bound to when nothing names another, by the value's type name.</param>
    /// <param name="nodeIdValues">The values a Relay node id can carry, by type name.</param>
    /// <param name="cursorKeyValues">The values HotChocolate's paging can write into a cursor, by type name.</param>
    /// <param name="cancellationToken">Stops the walk of a referenced assembly.</param>
    public static EquatableArray<ReferencedBinding> Of(
        Compilation compilation,
        IReadOnlyDictionary<string, string> defaultScalars,
        ICollection<string> nodeIdValues,
        ICollection<string> cursorKeyValues,
        CancellationToken cancellationToken)
    {
        if (ModuleBoundary.ModuleOf(compilation.Assembly) is not { } module
            || compilation.GetTypeByMetadataName(ProviderMetadataName) is not { } provider)
        {
            return EquatableArray<ReferencedBinding>.Empty;
        }

        // The bindings of this module that the generated method calls, and that could name the provider themselves:
        // each bound, by these same rules, what it references and can name.
        var called = DDDOptionsProvider.RegistrationsOfTheSameModule(compilation, RegistrationClass, RegistrationSuffix)
            .Where(registration => registration.References(provider.ContainingAssembly))
            .ToList();

        var writesNodeIds = compilation.GetTypeByMetadataName(NodeIdSerializerMetadataName) is not null;
        var registersCursorKeys = compilation.GetTypeByMetadataName(CursorKeySerializerMetadataName) is not null;

        var taken = new Dictionary<string, ReferencedBinding>(StringComparer.Ordinal);
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ModuleBoundary.ModuleOf(assembly) is not { } owner)
            {
                continue;
            }

            var sameModule = string.Equals(owner, module, StringComparison.Ordinal);
            foreach (var found in Scan(assembly, cancellationToken))
            {
                // The provider is closed over the type and its value, so both have to be names this project can
                // write. A value of an assembly it does not reference is not, and neither is the type then.
                if ((!sameModule && !found.Published)
                    || !compilation.IsSymbolAccessibleWithin(found.Symbol, compilation.Assembly)
                    || !CanName(compilation, found.ValueSymbol)
                    || taken.ContainsKey(found.Type)
                    || called.Any(registration => Covers(compilation, registration, assembly, found)))
                {
                    continue;
                }

                // A schema type the attribute names and this project cannot see is not bound, as a value without a
                // default scalar is not: the converter is still registered, so the application can bind it.
                string? scalar;
                if (found.SchemaType is null)
                {
                    defaultScalars.TryGetValue(found.ValueName, out scalar);
                }
                else
                {
                    scalar = CanName(compilation, found.SchemaType)
                        ? found.SchemaType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        : null;
                }

                var arguments = "<" + found.Type + ", " + found.Value + ">";
                taken.Add(found.Type, new ReferencedBinding(
                    found.Type,
                    scalar,
                    Provider + arguments,
                    writesNodeIds && found.IsIdentifier && nodeIdValues.Contains(found.ValueName) ? NodeIdSerializer + arguments : null,
                    registersCursorKeys && found.IsIdentifier && found.IsComparableStruct && cursorKeyValues.Contains(found.ValueName) ? CursorKeySerializer + arguments : null));
            }
        }

        return taken.Values.OrderBy(static binding => binding.Type, StringComparer.Ordinal).ToEquatableArray();
    }

    /// <summary>
    /// Whether bindings this project calls already have the type: they are generated into an assembly of the same
    /// module, so they take the same types this one would, and they took this one when that assembly references
    /// the type's assembly and can name the type and its value. A schema type the type asks for with
    /// <c>[GraphQLType&lt;T&gt;]</c> has to be one that assembly can name as well: where it cannot, its bindings
    /// have the converter without the scalar, which is not the whole of it, and this project writes both.
    /// <para>
    /// What an assembly references is read from its metadata, and the reference assembly a build compiles against
    /// lists only the references its public surface uses. An assembly that keeps another project's types out of
    /// its public surface is therefore not known to have bound them, and they are bound here as well: the same
    /// binding twice, which changes nothing, where leaving one out would leave a type the schema cannot convert.
    /// </para>
    /// </summary>
    private static bool Covers(Compilation compilation, ModuleRegistration registration, IAssemblySymbol declaring, Found found)
        => registration.References(declaring)
           && compilation.IsSymbolAccessibleWithin(found.Symbol, registration.Assembly)
           && CanName(compilation, found.ValueSymbol, registration)
           && (found.SchemaType is null || CanName(compilation, found.SchemaType, registration));

    /// <summary>
    /// Whether generated code in this project can write the type's name: it and everything it is closed over are
    /// declared in an assembly the project references, and are accessible from it. A type of an assembly the
    /// project does not reference reaches this compilation as a name only, through the metadata of the project
    /// that used it; asking the compiler whether such a type is accessible throws, so it is answered here first.
    /// <para>
    /// With <paramref name="from"/>, the same question for the assembly a called registration was generated into,
    /// which this compilation sees through metadata: there a reference is one that assembly lists.
    /// </para>
    /// </summary>
    private static bool CanName(Compilation compilation, ITypeSymbol type, ModuleRegistration? from = null)
    {
        switch (type)
        {
            case IErrorTypeSymbol:
                return false;

            case IArrayTypeSymbol array:
                return CanName(compilation, array.ElementType, from);

            case INamedTypeSymbol named:
                for (var current = named; current is not null; current = current.ContainingType)
                {
                    foreach (var argument in current.TypeArguments)
                    {
                        if (!CanName(compilation, argument, from))
                        {
                            return false;
                        }
                    }
                }

                if (from is null)
                {
                    return compilation.IsSymbolAccessibleWithin(named, compilation.Assembly);
                }

                return (SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, from.Assembly) || from.References(named.ContainingAssembly))
                       && compilation.IsSymbolAccessibleWithin(named, from.Assembly);

            default:
                return false;
        }
    }

    private static Found[] Scan(IAssemblySymbol assembly, CancellationToken cancellationToken)
    {
        if (ByAssembly.TryGetValue(assembly, out var known))
        {
            return known;
        }

        var found = new List<Found>();
        foreach (var type in DefinitionFactory.TypesIn(assembly.GlobalNamespace, cancellationToken))
        {
            if (type.IsGenericType || SingleValueOf(type) is not { } singleValue)
            {
                continue;
            }

            // A twin carries nothing of its own: it is the value object or id it derives from, validated. Its
            // parent's nested provider converts it too, and its parent's attributes are the ones that count.
            var declared = type.BaseType is { } parent && SingleValueOf(parent) is not null ? parent : type;
            if (declared.GetTypeMembers(NestedProvider).Length > 0)
            {
                continue;
            }

            var value = singleValue.TypeArguments[1];
            found.Add(new Found(
                type,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                value,
                value.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                value.Name,
                SchemaTypeOf(declared),
                SymbolEqualityComparer.Default.Equals(declared, type) && DefinitionFactory.IsEntityId(type),
                type.IsValueType && ComparesToItself(type),
                ModuleBoundary.IsPublished(type) || ModuleBoundary.IsPublished(declared)));
        }

        return ByAssembly.GetValue(assembly, _ => found.ToArray());
    }

    /// <summary><c>ISingleValue&lt;TSelf, TValue&gt;</c> with the type itself as <c>TSelf</c>, or null: a twin also inherits its parent's.</summary>
    private static INamedTypeSymbol? SingleValueOf(INamedTypeSymbol type)
        => type.AllInterfaces.FirstOrDefault(candidate =>
            candidate is { Name: "ISingleValue", Arity: 2 }
            && candidate.ContainingNamespace.ToDisplayString() == "DDDToolkit.Interfaces"
            && SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], type));

    /// <summary>Whether the type implements <c>IComparable&lt;T&gt;</c> of itself: the comparison paging has Entity Framework translate.</summary>
    private static bool ComparesToItself(INamedTypeSymbol type)
        => type.AllInterfaces.Any(candidate =>
            candidate is { Name: "IComparable", Arity: 1 }
            && candidate.ContainingNamespace.ToDisplayString() == "System"
            && SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], type));

    /// <summary>
    /// The schema type a <c>[GraphQLType&lt;T&gt;]</c> on the type names, or null. A project that references
    /// DDDToolkit.HotChocolate for the attribute and does not run its generator declares such a type.
    /// </summary>
    private static ITypeSymbol? SchemaTypeOf(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "GraphQLTypeAttribute", TypeArguments.Length: 1 } attributeClass
                && attributeClass.ContainingNamespace.ToDisplayString() == "DDDToolkit.HotChocolate.Attributes")
            {
                return attributeClass.TypeArguments[0];
            }
        }

        return null;
    }
}

/// <summary>What the generated <c>Add{Module}GraphQlRuntimeBindings()</c> writes for a type of another project.</summary>
/// <param name="Type">The id or value object, fully qualified.</param>
/// <param name="Scalar">The schema type it is bound to, or null when its value has no default scalar.</param>
/// <param name="Provider">The closed <c>SingleValueChangeTypeProvider&lt;T, TValue&gt;</c> that converts it.</param>
/// <param name="NodeIdSerializer">
/// The closed <c>SingleValueNodeIdSerializer&lt;T, TValue&gt;</c> that writes it into a Relay node id, or null when
/// it is not an identifier over a value a node id can carry.
/// </param>
/// <param name="CursorKeySerializer">
/// The closed <c>SingleValueCursorKeySerializer&lt;T, TValue&gt;</c> that makes it a key paging can order by, or null
/// when it is not a struct identifier over a value a cursor can carry.
/// </param>
internal sealed record ReferencedBinding(string Type, string? Scalar, string Provider, string? NodeIdSerializer, string? CursorKeySerializer);
