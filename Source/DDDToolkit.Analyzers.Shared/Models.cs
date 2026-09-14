using System;
using System.Text;

namespace DDDToolkit.Analyzers.Common;

internal enum DeclarationKind
{
    Class,
    RecordClass,
    RecordStruct,
    Struct,
    Interface,
    Other,
}

/// <summary>Everything the emitters need to know about the user's type declaration.</summary>
internal sealed record TypeDeclarationInfo(
    string Name,
    string Namespace,
    string FullyQualifiedName,
    string Accessibility,
    DeclarationKind Kind,
    bool IsPartial,
    bool IsSealed,
    bool IsReadOnly,
    bool IsAbstract,
    /// <summary>
    /// True when the type, or any type it is nested in, has type parameters. Either way the generated
    /// members cannot name it from an attribute argument or from a registration method outside it.
    /// </summary>
    bool IsGeneric,
    EquatableArray<string> ContainingTypeHeaders,
    LocationInfo? Location)
{
    /// <summary>
    /// True when the author wrote no declaration of this type at all and the generator emits the only
    /// one, as it does for the id derived from <c>[AggregateRoot&lt;Guid&gt;]</c>. Such a declaration has to
    /// carry its own accessibility, because there is no other part to take it from.
    /// </summary>
    public bool IsImplicit { get; init; }

    public bool IsRecord => Kind is DeclarationKind.RecordClass or DeclarationKind.RecordStruct;

    public bool IsStruct => Kind is DeclarationKind.RecordStruct or DeclarationKind.Struct;

    /// <summary>Reference-type records get an always-valid twin ('ValidX'); structs cannot inherit and do not.</summary>
    public bool HasValidTwin => Kind == DeclarationKind.RecordClass;

    public string ValidTwinName => "Valid" + Name;

    /// <summary>Fully qualified name of the always-valid twin, e.g. global::Ns.ValidEmailAddress.</summary>
    public string ValidTwinFullyQualifiedName
        => FullyQualifiedName.Substring(0, FullyQualifiedName.Length - Name.Length) + ValidTwinName;

    /// <summary>The keyword(s) to redeclare the type in a partial part: class, record, record struct, struct.</summary>
    public string Keyword => Kind switch
    {
        DeclarationKind.RecordClass => "record",
        DeclarationKind.RecordStruct => "record struct",
        DeclarationKind.Struct => "struct",
        DeclarationKind.Interface => "interface",
        _ => "class",
    };

    /// <summary>
    /// Header for the generated partial part, e.g. "readonly partial record struct CatId". An implicit
    /// type leads with its accessibility ("public readonly partial record struct OrderId"), which the
    /// author may repeat but need not: a part without an accessibility modifier takes it from this one.
    /// </summary>
    public string PartialHeader
        => (IsImplicit ? Accessibility + " " : string.Empty)
           + (IsReadOnly && IsStruct ? "readonly " : string.Empty)
           + "partial " + Keyword + " " + Name;

    /// <summary>
    /// File name of the generated part. Built from the fully qualified name (not just namespace plus
    /// name) so a nested type cannot collide with a top-level type of the same name in the same
    /// namespace: two AddSource calls with one hint name throw inside the generator, which then
    /// contributes nothing at all — for either type. Characters a file name cannot hold (the angle
    /// brackets of a generic type) become underscores.
    /// </summary>
    public string HintName(string suffix = "")
    {
        var qualified = FullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? FullyQualifiedName.Substring("global::".Length)
            : FullyQualifiedName;

        var builder = new StringBuilder(qualified.Length + suffix.Length + 5);
        foreach (var character in qualified)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '.' || character == '_' ? character : '_');
        }

        return builder.Append(suffix).Append(".g.cs").ToString();
    }
}

/// <summary>The wrapped value type of a single value object or entity id.</summary>
internal sealed record ValueTypeInfo(
    string FullyQualifiedName,
    string Name,
    bool IsString,
    bool IsGuid,
    bool IsValueType,
    /// <summary>True when the type has a static TryParse(string, IFormatProvider, out T) (all BCL primitives on .NET 7+).</summary>
    bool HasFormatProviderTryParse)
{
    /// <summary>Whether Parse/TryParse can be generated for ids wrapping this type.</summary>
    public bool CanParse => IsString || HasFormatProviderTryParse;
}

internal sealed record PropertyInfo(
    string Name,
    string TypeName,
    bool HasSetter,
    bool IsInitOnly,
    bool HasProtectedSetter,
    bool IsInternal,
    bool IsDontCompare,
    LocationInfo? Location);

internal enum CollectionBacking
{
    List,
    HashSet,
}

/// <summary>A get-only partial collection property the entity generator implements.</summary>
internal sealed record CollectionPropertyInfo(
    string Name,
    string FieldName,
    string ElementType,
    /// <summary>
    /// True when the element carries <c>[Entity]</c> or <c>[AggregateRoot]</c>, which is what makes this
    /// collection one of the entity's children rather than a bag of values. Only these are walked when an
    /// aggregate answers for what it holds.
    /// </summary>
    bool ElementIsEntity,
    string InterfaceType,
    CollectionBacking Backing,
    string Modifiers,
    bool HasSetter,
    LocationInfo? Location)
{
    public string BackingType => Backing switch
    {
        CollectionBacking.HashSet => "global::System.Collections.Generic.HashSet<" + ElementType + ">",
        _ => "global::System.Collections.Generic.List<" + ElementType + ">",
    };
}

internal sealed record EntityIdDefinition(
    TypeDeclarationInfo Type,
    ValueTypeInfo Value,
    string Prefix,
    int ColumnLength,
    string? GraphQLSchemaType,
    bool SystemTextJsonAvailable,
    bool IParsableAvailable,
    bool CanGenerate,
    EquatableArray<DiagnosticInfo> Diagnostics);

internal sealed record SingleValueObjectDefinition(
    TypeDeclarationInfo Type,
    ValueTypeInfo Value,
    int ColumnLength,
    string? GraphQLSchemaType,
    bool SystemTextJsonAvailable,
    bool CanGenerate,
    EquatableArray<DiagnosticInfo> Diagnostics);

internal sealed record ValueObjectDefinition(
    TypeDeclarationInfo Type,
    EquatableArray<PropertyInfo> Properties,
    bool SystemTextJsonAvailable,
    bool CanGenerate,
    EquatableArray<DiagnosticInfo> Diagnostics);

internal sealed record EntityDefinition(
    TypeDeclarationInfo Type,
    bool IsAggregateRoot,
    string IdType,
    /// <summary>
    /// The id this declaration asks the toolkit to generate, when the attribute names a raw value
    /// (<c>[AggregateRoot&lt;Guid&gt;]</c>) rather than an existing id. Null for the explicit form, where
    /// the type argument already is the id. <see cref="IdType"/> always names whichever it is.
    /// </summary>
    EntityIdDefinition? ImplicitId,
    EquatableArray<CollectionPropertyInfo> Collections,
    /// <summary>
    /// Fully qualified names of the nested <c>IInvariant&lt;T&gt;</c> rules this type states, in the order
    /// they are declared. Empty when it states none, which is the shape that keeps the generated
    /// <c>EnsureInvariants</c> a method the JIT can drop.
    /// </summary>
    EquatableArray<string> Invariants,
    bool EfBackingFieldAttributeAvailable,
    bool ReadOnlySetAvailable,
    bool CanGenerate,
    EquatableArray<DiagnosticInfo> Diagnostics);
