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

    /// <summary>
    /// The type parameters of the type itself, in order, which its generated part has to repeat. Empty for
    /// everything but the abstract parents a package declares with <c>[AggregateRootBase]</c> or
    /// <c>[EntityBase]</c>: every other generic type is refused before anything is generated for it.
    /// </summary>
    public EquatableArray<string> TypeParameters { get; init; } = EquatableArray<string>.Empty;

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
           + "partial " + Keyword + " " + Name
           + (TypeParameters.Count == 0 ? string.Empty : "<" + string.Join(", ", TypeParameters) + ">");

    /// <summary>
    /// File name of the generated part: the type's own name, the suffix, and eight hex digits of a
    /// hash of the fully qualified name, as in <c>Order.EntityFramework.1f3a9c2e.g.cs</c>.
    /// <para>
    /// The name has to be unique per generator: two AddSource calls with one hint name throw inside
    /// the generator, which then contributes nothing at all, for either type. The fully qualified name
    /// is unique, but it is also long, and Visual Studio puts every generated document at
    /// <c>{project}\Generated\{generator assembly}\{generator type}\{hint name}</c>, a path it has to
    /// be able to expand. With the namespace spelled out a second time in the file name, a module a few
    /// folders deep went past the 260 character limit and the project failed to load. The hash keeps
    /// the name unique (a nested type, the same name in two namespaces) at a fixed, small cost.
    /// </para>
    /// <para>
    /// The hash is FNV-1a over the UTF-16 code units of the name, not <see cref="string.GetHashCode()"/>,
    /// which differs between processes: a hint name has to be the same on every build and machine.
    /// Characters a file name cannot hold become underscores.
    /// </para>
    /// </summary>
    public string HintName(string suffix = "") => HintNameFor(Name, FullyQualifiedName, suffix);

    /// <summary>
    /// <see cref="HintName"/> for a type the generator did not start from a declaration of, such as a type
    /// in a referenced assembly whose generated counterpart is written into this one.
    /// </summary>
    public static string HintNameFor(string name, string fullyQualifiedName, string suffix)
    {
        var qualified = fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualifiedName.Substring("global::".Length)
            : fullyQualifiedName;

        var hash = 2166136261u;
        foreach (var character in qualified)
        {
            hash = unchecked((hash ^ character) * 16777619u);
        }

        var builder = new StringBuilder(name.Length + suffix.Length + 20);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        return builder.Append(suffix).Append('.').Append(hash.ToString("x8")).Append(".g.cs").ToString();
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
    LocationInfo? Location)
{
    /// <summary>
    /// True for a property the compiler synthesized from a positional record parameter. Such a property
    /// is always <c>public init</c>, so the value object generator declares it again as
    /// <c>protected init</c>, which stops the compiler from synthesizing it.
    /// </summary>
    public bool IsPositional { get; init; }

    /// <summary>
    /// The attributes a positional parameter aimed at its property or backing field, rendered as source
    /// (<c>[global::Ns.DontCompare]</c>, <c>[field: global::Ns.X(1)]</c>). Once the generator declares the
    /// property itself the compiler drops them from the parameter, so the declaration carries them.
    /// </summary>
    public EquatableArray<string> Attributes { get; init; } = EquatableArray<string>.Empty;
}

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
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    /// <summary>True when the author wrote a <c>Validate</c> of their own; see <see cref="ValueObjectDefinition.DeclaresValidate"/>.</summary>
    public bool DeclaresValidate { get; init; }

    /// <summary>
    /// True when the project can see <c>DDDToolkit.Interfaces.ISingleValue&lt;TSelf, TValue&gt;</c>, so the id (and a
    /// record id's twin) implements it. Read off the compilation, like <see cref="IParsableAvailable"/>.
    /// </summary>
    public bool SingleValueAvailable { get; init; }

    /// <summary>
    /// True when the project can see <c>DDDToolkit.Abstractions.Interfaces.ICreatableEntityId&lt;TSelf&gt;</c>, which
    /// only the .NET 10 build of the abstractions has. Read off the compilation, like <see cref="SingleValueAvailable"/>.
    /// </summary>
    public bool CreatableAvailable { get; init; }

    /// <summary>What the author's own parts of the id declare called <c>Create</c> without parameters.</summary>
    public DeclaredCreate OwnCreate { get; init; }

    /// <summary>
    /// Whether the generator writes <c>Create()</c>, making a time-ordered id as <c>CreateSequential()</c> does: for an
    /// id over a <c>Guid</c> whose parts declare nothing of that name. A <c>Create()</c> of the author's own wins.
    /// </summary>
    public bool WritesCreate => Value.IsGuid && OwnCreate == DeclaredCreate.None;

    /// <summary>
    /// Whether the id implements <c>ICreatableEntityId&lt;TSelf&gt;</c>: where the project sees it, with the
    /// generator's <c>Create()</c> or with the author's own.
    /// </summary>
    public bool ImplementsCreatable => CreatableAvailable && (WritesCreate || OwnCreate == DeclaredCreate.Fits);
}

/// <summary>What an id's own parts declare called <c>Create</c>, without parameters: it decides whether the generator writes one.</summary>
internal enum DeclaredCreate
{
    /// <summary>Nothing: an id over a <c>Guid</c> gets the generator's, and an id over anything else has none.</summary>
    None,

    /// <summary>A public static <c>Create()</c> that answers the id: the generator implements the interface with it.</summary>
    Fits,

    /// <summary>
    /// Something else of that name, an instance method, one that is not public or one that answers another type:
    /// the generator writes no <c>Create()</c> beside it, and implements no interface with it.
    /// </summary>
    Other,
}

internal sealed record SingleValueObjectDefinition(
    TypeDeclarationInfo Type,
    ValueTypeInfo Value,
    int ColumnLength,
    string? GraphQLSchemaType,
    bool SystemTextJsonAvailable,
    bool CanGenerate,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    /// <summary>True when the author wrote a <c>Validate</c> of their own; see <see cref="ValueObjectDefinition.DeclaresValidate"/>.</summary>
    public bool DeclaresValidate { get; init; }

    /// <summary>
    /// True when the project can see <c>DDDToolkit.Interfaces.ISingleValue&lt;TSelf, TValue&gt;</c>, so the value object
    /// and its twin implement it. See <see cref="EntityIdDefinition.SingleValueAvailable"/>.
    /// </summary>
    public bool SingleValueAvailable { get; init; }
}

internal sealed record ValueObjectDefinition(
    TypeDeclarationInfo Type,
    EquatableArray<PropertyInfo> Properties,
    bool SystemTextJsonAvailable,
    bool CanGenerate,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    /// <summary>
    /// The parameter types of the primary constructor, in order, when the record is positional; null
    /// otherwise. The generated parameterless constructor has to chain to it.
    /// </summary>
    public EquatableArray<string>? PrimaryConstructorParameterTypes { get; init; }

    /// <summary>False when the author declared a member named <c>With</c>, which the generated one would clash with.</summary>
    public bool GenerateWith { get; init; } = true;

    /// <summary>
    /// True when the author declared <c>Validate()</c> or <c>Validate(ValidationErrorBuilder)</c> in any part
    /// of the type. DDDToolkit.FluentValidation generates both, so it leaves such a type alone: the author's
    /// validation is the validation, and there is no nested <c>Validator</c> or <c>Errors</c>.
    /// </summary>
    public bool DeclaresValidate { get; init; }

    /// <summary>
    /// True when HotChocolate is referenced. Without the toolkit's conventions HotChocolate publishes every
    /// public method as a field, and <c>With</c> takes arguments it cannot turn into input types, which
    /// fails the whole schema; <c>[GraphQLIgnore]</c> keeps it out whether the conventions are there or not.
    /// </summary>
    public bool GraphQLIgnoreAvailable { get; init; }
}

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
    /// <summary>The names of the <c>[KeyPart]</c> properties, in declaration order. Empty for most types.</summary>
    EquatableArray<string> KeyParts,
    bool EfBackingFieldAttributeAvailable,
    bool ReadOnlySetAvailable,
    bool CanGenerate,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    /// <summary>
    /// True for an abstract generic parent a package declares with <c>[AggregateRootBase]</c> or
    /// <c>[EntityBase]</c>. It gets the base class and the collections every entity gets, and instead of
    /// the public invariant methods it offers the two its descendant chains to, so the parent's rules run
    /// wherever the descendant's do.
    /// </summary>
    public bool IsBase { get; init; }

    /// <summary>
    /// How a class declared with a package's template attribute was declared, until the parent it derives
    /// from is known. Null for every other declaration.
    /// </summary>
    public TemplateDeclaration? Template { get; init; }

    /// <summary>
    /// The closed parent a class declared with a template attribute derives from, such as
    /// <c>global::Tenancy.TenantAggregate&lt;global::Shop.Contracts.TenantId&gt;</c>, or null for a class that
    /// derives from the toolkit's own <c>AggregateRoot&lt;TId&gt;</c> or <c>Entity&lt;TId&gt;</c>.
    /// </summary>
    public string? BaseType { get; init; }

    /// <summary>Whether this class runs its parent's rules and walks its parent's children before its own.</summary>
    public bool ChainsToParent => BaseType is not null;

    /// <summary>
    /// The open definition of the template attribute the class is declared with, fully qualified, whether or
    /// not the template itself is usable. What another template's <c>[TemplateArgument]</c> finds the class
    /// by: a class the author can see is never reported missing because its package made a mistake.
    /// </summary>
    public string? TemplateKey { get; init; }

    /// <summary>Whether the id a template class is declared with is an entity id, which is what lets another class take it.</summary>
    public bool TemplateIdIsEntityId { get; init; }

    /// <summary>The metadata name of a template class, <c>Shop.Tenancy.ShopTenant</c>, for finding its symbol again when the parent is closed.</summary>
    public string? MetadataName { get; init; }

    /// <summary>
    /// True when the parent of a template class has <c>[KeyPart]</c> properties. A class with key parts of its
    /// own implements the key-part list again, and has to put the parent's in front of its own.
    /// </summary>
    public bool ParentHasKeyParts { get; init; }

    /// <summary>
    /// The name a template class's id is written with, <c>SeatId</c>, when no type of that name exists yet and that
    /// alone keeps the class from being generated: an id the project's <c>[TemplateDefaults]</c> switch writes, which
    /// the compilation a generator sees does not show. Null for every other class. The switch's plan binds it, or
    /// leaves the class to the compiler's own error about the name.
    /// </summary>
    public string? UnboundIdName { get; init; }

    /// <summary>
    /// The positions of the template's later type arguments that name an id the project's <c>[TemplateDefaults]</c>
    /// switch writes, <c>SeatId</c> in <c>[Member&lt;CrewMemberId, SeatId, RoleId, Project&gt;]</c>: the compiler a
    /// generator sees could not bind them, so the switch's plan has put the id in full in
    /// <see cref="TemplateDeclaration.Arguments"/>, and a registration closed over the class takes it from there.
    /// Empty for every other class.
    /// </summary>
    public EquatableArray<int> WrittenArguments { get; init; }
}

/// <summary>
/// What a template attribute on a class says, read before the other classes of the project are known.
/// Resolving it fills the parent's type parameters in order: the attribute's own type arguments first,
/// then each <see cref="Bindings"/> entry from the one class in the project declared with the attribute
/// that entry names.
/// </summary>
/// <param name="AttributeKey">The template attribute's open definition, fully qualified; what a binding names to find this class.</param>
/// <param name="AttributeName">The attribute as the author writes it, <c>TenantAggregate</c>, for diagnostics.</param>
/// <param name="Parent">The parent's fully qualified name without type arguments.</param>
/// <param name="ParentMetadataName">The parent's metadata name, for checking its constraints once every argument is known.</param>
/// <param name="ParentParameters">The parent's type parameter names, for diagnostics; their count is its arity.</param>
/// <param name="Arguments">
/// The attribute's own type arguments, fully qualified. The first is the id. There may be more of them than the
/// parent has type parameters: those beyond are not the parent's.
/// </param>
internal sealed record TemplateDeclaration(
    string AttributeKey,
    string AttributeName,
    string Parent,
    string ParentMetadataName,
    EquatableArray<string> ParentParameters,
    EquatableArray<string> Arguments,
    EquatableArray<TemplateBinding> Bindings);

/// <summary>One parent type parameter a template fills from another class of the project.</summary>
/// <param name="Position">The parent type parameter it fills.</param>
/// <param name="SourceKey">The open template attribute whose class provides it, fully qualified.</param>
/// <param name="SourceName">That attribute as the author writes it, for diagnostics.</param>
/// <param name="SourceMetadataName">That attribute's metadata name, for finding a class declared with it in a referenced project.</param>
/// <param name="TakeType">True to take the class itself, false to take its id.</param>
internal sealed record TemplateBinding(int Position, string SourceKey, string SourceName, string SourceMetadataName, bool TakeType);
