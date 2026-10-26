using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.EntityFramework.Analyzers;

/// <summary>
/// The ids and single value objects of other projects that the project holding a module's context registers a
/// converter for, so that one call of its <c>Add{Module}Converters()</c> covers every such type the module may store.
/// Each implements <c>ISingleValue&lt;TSelf, TValue&gt;</c>.
/// <list type="bullet">
///   <item><description>Every one of an assembly of this project's own module, its domain and contracts projects, that
///   has no nested converter of its own, which is what a type declared in a project without Entity Framework looks
///   like. One that has one is registered by its own project's <c>Add{Module}Converters()</c>, of the same name, which
///   this one calls.</description></item>
///   <item><description>The published ones, <c>[ModuleContract]</c> or public in an assembly that is its module's
///   contracts, <c>[assembly: ModuleContracts]</c>, of an assembly of another module, which this module
///   may store (DDD00022 allows nothing else): with <c>SingleValueConverter</c> where the type has no converter of its
///   own, and with its own nested converter where its project references Entity Framework. The other module's
///   registration has another name, which nothing here calls, so the published types are registered here either way:
///   a module stores another's published ids with its own one call, whatever that module's projects reference.</description></item>
/// </list>
/// An assembly that declares no module is left alone: a package such as the Tenancy package maps what it declares
/// itself, and a shared kernel without a module is not something a module's registration can speak for. A project
/// without a module registers only its own.
/// <para>
/// A module can have more than one project that references Entity Framework, each with a registration of the same
/// name, and the one that references the other calls it. What the called one registered for the module's projects
/// without Entity Framework is then left out here, so one call registers each type once: see <see cref="Covers"/>.
/// </para>
/// </summary>
internal static class ModuleSingleValues
{
    /// <summary>The converter the registration uses for them, which only a project referencing DDDToolkit.EntityFramework can name.</summary>
    public const string ConverterMetadataName = "DDDToolkit.EntityFramework.Storage.SingleValueConverter`2";

    /// <summary>The generated registration class, under the namespace of the assembly it is generated into.</summary>
    public const string RegistrationClass = "Converters.ConverterExtensions";

    /// <summary>What the generated registration method's name ends in.</summary>
    public const string RegistrationSuffix = "Converters";

    private const string Converter = "global::DDDToolkit.EntityFramework.Storage.SingleValueConverter";

    /// <summary>
    /// One type of a referenced assembly as the scan found it, before this compilation decides whether it takes it:
    /// that depends on this project's module and on what it can see.
    /// </summary>
    /// <param name="Symbol">The type.</param>
    /// <param name="Type">The type, fully qualified.</param>
    /// <param name="Value">The value it stores as, fully qualified.</param>
    /// <param name="ColumnLength">The column length its attribute asks for, or -1.</param>
    /// <param name="Published">Whether it is published, as <see cref="ModuleBoundary.IsPublished"/> reads it, itself or as the twin of a published type.</param>
    /// <param name="OwnConverter">Its nested converter, fully qualified, or null when it has none.</param>
    private sealed record Found(INamedTypeSymbol Symbol, string Type, string Value, int ColumnLength, bool Published, string? OwnConverter);

    /// <summary>
    /// The candidates of a referenced assembly. Walking every type of an assembly is the expensive part, and a
    /// referenced assembly does not change while the project that references it is edited: the compiler keeps the
    /// same symbol for it across compilations, so it is walked once for as long as the symbol lives.
    /// </summary>
    private static readonly ConditionalWeakTable<IAssemblySymbol, Found[]> ByAssembly = new();

    /// <summary>
    /// The registrations this compilation writes for other projects' types, sorted, so the generated file does not
    /// change when the references are handed over in another order. Empty when the project declares no module or
    /// cannot name <see cref="ConverterMetadataName"/>.
    /// </summary>
    public static EquatableArray<ReferencedConverter> Of(Compilation compilation, CancellationToken cancellationToken)
    {
        if (ModuleBoundary.ModuleOf(compilation.Assembly) is not { } module
            || compilation.GetTypeByMetadataName(ConverterMetadataName) is not { } converter)
        {
            return EquatableArray<ReferencedConverter>.Empty;
        }

        // The registrations of this module that the generated method calls, and that could name the converter
        // themselves: each registered, by these same rules, what it references and can see.
        var called = DDDOptionsProvider.RegistrationsOfTheSameModule(compilation, RegistrationClass, RegistrationSuffix)
            .Where(registration => registration.References(converter.ContainingAssembly))
            .ToList();

        var taken = new Dictionary<string, ReferencedConverter>(StringComparer.Ordinal);
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
                // A type of this module with a converter of its own is its own project's to register, in the
                // registration of the same name this one calls. A published one of another module is registered
                // here whatever it has, with its own converter where it has one.
                var wanted = sameModule ? found.OwnConverter is null : found.Published;
                if (wanted
                    && compilation.IsSymbolAccessibleWithin(found.Symbol, compilation.Assembly)
                    && !taken.ContainsKey(found.Type)
                    && !called.Any(registration => Covers(compilation, registration, assembly, found.Symbol)))
                {
                    taken.Add(found.Type, new ReferencedConverter(found.Type, found.OwnConverter ?? Converter + "<" + found.Type + ", " + found.Value + ">", found.ColumnLength));
                }
            }
        }

        return taken.Values.OrderBy(static converter => converter.Type, StringComparer.Ordinal).ToEquatableArray();
    }

    /// <summary>
    /// Whether a registration this project calls already has the type: it is generated into an assembly of the same
    /// module, so it takes the same types this one would, and it took this one when it references the type's
    /// assembly and can see the type.
    /// <para>
    /// What an assembly references is read from its metadata, and the reference assembly a build compiles against
    /// lists only the references its public surface uses. An assembly that keeps another project's types out of
    /// its public surface is therefore not known to have registered them, and they are registered here as well:
    /// the same line twice, which changes nothing, where leaving one out would leave a type without a converter.
    /// </para>
    /// </summary>
    private static bool Covers(Compilation compilation, ModuleRegistration registration, IAssemblySymbol declaring, INamedTypeSymbol type)
        => registration.References(declaring) && compilation.IsSymbolAccessibleWithin(type, registration.Assembly);

    private static Found[] Scan(IAssemblySymbol assembly, CancellationToken cancellationToken)
    {
        if (ByAssembly.TryGetValue(assembly, out var known))
        {
            return known;
        }

        var types = DefinitionFactory.TypesIn(assembly.GlobalNamespace, cancellationToken).ToList();

        // An id an [AggregateRoot<Guid>] or [Entity<Guid>] asks for carries no attribute of its own: its column length
        // is on the entity's attribute, and the id is named after the entity, next to it.
        var implicitLengths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (EntityAttributeOf(type) is not { } attribute
                || attribute.AttributeClass is not { TypeArguments.Length: 1 } attributeClass
                || DefinitionFactory.IsEntityId(attributeClass.TypeArguments[0]))
            {
                continue;
            }

            var scope = type.ContainingType is { } outer
                ? outer.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "."
                : type.ContainingNamespace is { IsGlobalNamespace: false } space ? "global::" + space.ToDisplayString() + "." : "global::";
            implicitLengths[scope + Identifiers.IdNameFor(type.Name)] = DefinitionFactory.GetArgument(attribute, "ColumnLength", -1);
        }

        var found = new List<Found>();
        foreach (var type in types)
        {
            if (type.IsGenericType || SingleValueOf(type) is not { } singleValue)
            {
                continue;
            }

            var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // The converter the entity framework generator nests in the type, where its project references Entity
            // Framework: public, as the type is, a value converter, and the one that type's own registration uses.
            // A type with a class of that name that is none of these is left alone, as before: whatever it is, it
            // is not a converter a registration could name.
            var nestedConverter = type.GetTypeMembers(type.Name + "Converter")
                .FirstOrDefault(static nested => nested.DeclaredAccessibility == Accessibility.Public && IsValueConverter(nested));
            if (nestedConverter is null && type.GetTypeMembers(type.Name + "Converter").Length > 0)
            {
                continue;
            }

            // A twin has no attribute either: it is the value object or id it derives from, validated.
            var declared = type.BaseType is { } parent && SingleValueOf(parent) is not null ? parent : type;
            var length = ValueAttributeOf(declared) is { } own
                ? DefinitionFactory.GetArgument(own, "ColumnLength", -1)
                : implicitLengths.TryGetValue(declared.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), out var implicitLength) ? implicitLength : -1;

            found.Add(new Found(
                type,
                name,
                singleValue.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                length,
                ModuleBoundary.IsPublished(type) || ModuleBoundary.IsPublished(declared),
                nestedConverter?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        return ByAssembly.GetValue(assembly, _ => found.ToArray());
    }

    /// <summary>Whether <paramref name="type"/> derives from Entity Framework's <c>ValueConverter</c>, as the converter the generator nests does.</summary>
    private static bool IsValueConverter(INamedTypeSymbol type)
    {
        for (var parent = type.BaseType; parent is not null; parent = parent.BaseType)
        {
            if (parent is { Name: "ValueConverter" }
                && parent.ContainingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore.Storage.ValueConversion")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>ISingleValue&lt;TSelf, TValue&gt;</c> with the type itself as <c>TSelf</c>, or null: a twin also inherits its parent's.</summary>
    private static INamedTypeSymbol? SingleValueOf(INamedTypeSymbol type)
        => type.AllInterfaces.FirstOrDefault(candidate =>
            candidate is { Name: "ISingleValue", Arity: 2 }
            && candidate.ContainingNamespace.ToDisplayString() == "DDDToolkit.Interfaces"
            && SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], type));

    /// <summary>The <c>[EntityId&lt;T&gt;]</c> or <c>[SingleValueObject&lt;T&gt;]</c> a type is declared with, or null.</summary>
    private static AttributeData? ValueAttributeOf(INamedTypeSymbol type)
        => type.GetAttributes().FirstOrDefault(static attribute =>
            attribute.AttributeClass is { Arity: 1 } attributeClass
            && attributeClass.Name is "EntityIdAttribute" or "SingleValueObjectAttribute"
            && attributeClass.ContainingNamespace.ToDisplayString() == KnownTypes.AttributesNamespace);

    /// <summary>The <c>[AggregateRoot&lt;T&gt;]</c> or <c>[Entity&lt;T&gt;]</c> a type is declared with, or null.</summary>
    private static AttributeData? EntityAttributeOf(INamedTypeSymbol type)
        => type.GetAttributes().FirstOrDefault(static attribute =>
            attribute.AttributeClass is { Arity: 1 } attributeClass
            && attributeClass.Name is "AggregateRootAttribute" or "EntityAttribute"
            && attributeClass.ContainingNamespace.ToDisplayString() == KnownTypes.AttributesNamespace);
}

/// <summary>A registration the generated <c>Add{Module}Converters()</c> writes for a type of another project.</summary>
/// <param name="Type">The id or value object, fully qualified.</param>
/// <param name="Converter">The closed <c>SingleValueConverter&lt;T, TValue&gt;</c> that stores it.</param>
/// <param name="ColumnLength">The column length its attribute asks for, or -1.</param>
internal sealed record ReferencedConverter(string Type, string Converter, int ColumnLength);
