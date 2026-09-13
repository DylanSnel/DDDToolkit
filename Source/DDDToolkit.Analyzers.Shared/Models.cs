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
    EquatableArray<string> ContainingTypeHeaders,
    LocationInfo? Location)
{
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

    /// <summary>Header for the generated partial part, e.g. "readonly partial record struct CatId".</summary>
    public string PartialHeader => (IsReadOnly && IsStruct ? "readonly " : string.Empty) + "partial " + Keyword + " " + Name;

    public string HintName(string suffix = "")
        => (Namespace.Length == 0 ? Name : Namespace + "." + Name) + suffix + ".g.cs";
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
    EquatableArray<CollectionPropertyInfo> Collections,
    bool EfBackingFieldAttributeAvailable,
    bool ReadOnlySetAvailable,
    bool CanGenerate,
    EquatableArray<DiagnosticInfo> Diagnostics);
