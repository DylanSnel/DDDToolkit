using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// Whether an id has a <c>Create()</c>, which is how a package that is generic over an application's ids makes a new
/// one, in code and before the save: <c>where TId : ICreatableEntityId&lt;TId&gt;</c> on the package's side,
/// <c>[AggregateRootTemplate(..., CreatesIds = true)]</c> on its template.
/// <para>
/// A generator does not see what another writes, so an id of the project being generated is judged by what the
/// toolkit's id generator will make of it: an <c>[EntityId&lt;Guid&gt;]</c> gets a <c>Create()</c>, and an id that
/// declares a <c>public static Create()</c> of its own keeps it, the interface implemented with it either way. An id
/// of a referenced project shows what it has, and so does one written by hand, which no generator completes: a type
/// that shows <c>IEntityId</c> without the attribute. Of an id nothing can be told about, one the compiler cannot bind
/// yet or the author's part of the id the generator writes for an <c>[AggregateRoot&lt;Guid&gt;]</c>, which shows no
/// <c>IEntityId</c> until the generator adds it, nothing is said: the compiler has the last word.
/// </para>
/// </summary>
internal static class IdCreation
{
    /// <summary>Whether a type parameter asks for an id that makes a new one of itself: <c>where TId : ICreatableEntityId&lt;TId&gt;</c>.</summary>
    public static bool IsRequiredBy(ITypeParameterSymbol parameter)
        => parameter.ConstraintTypes.Any(static constraint => constraint is INamedTypeSymbol { IsGenericType: true } named
                                                              && EntityDeclarations.MetadataNameOf(named.OriginalDefinition) == KnownTypes.CreatableEntityIdInterface);

    /// <summary>
    /// Whether <paramref name="id"/> is known to have no <c>Create()</c> once the generator has written it. False when it
    /// has one, when the project cannot see the interface, so nothing can ask for one, and when it cannot be told.
    /// </summary>
    public static bool Lacks(ITypeSymbol id, Compilation compilation, CancellationToken cancellationToken)
    {
        if (id is not INamedTypeSymbol named
            || named.TypeKind == TypeKind.Error
            || compilation.GetTypeByMetadataName(KnownTypes.CreatableEntityIdInterface) is not { } creatable)
        {
            return false;
        }

        if (named.AllInterfaces.Contains(creatable.Construct(named), SymbolEqualityComparer.Default))
        {
            return false;
        }

        // A type of a referenced project, or one of this project that no generator completes, is what it shows.
        var partial = named.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
        if (!partial)
        {
            return true;
        }

        // A partial type without the attribute that shows IEntityId was written so by hand: what a generator adds is
        // not in its compilation, and the author's part of the id the generator writes for an [AggregateRoot<Guid>]
        // shows no IEntityId, since a part that does is not taken for one (DDD00007). It is what it shows, and that
        // lacks the interface. A part that shows nothing cannot be told from here.
        if (DeclaredValueOf(named) is not { } value)
        {
            return ShowsIEntityId(named);
        }

        // What the id generator will make of an [EntityId<T>] of this project. One it refuses gets nothing, and its
        // own diagnostic says why.
        if (DefinitionFactory.EntityIdIsRefused(named))
        {
            return false;
        }

        return DefinitionFactory.DeclaredCreateOf(named) switch
        {
            DeclaredCreate.Fits => false,
            DeclaredCreate.None => !IsGuid(value),
            _ => true,
        };
    }

    /// <summary>
    /// Whether code that closes <paramref name="parameter"/> over <paramref name="argument"/> asks it for a <c>Create()</c>
    /// it lacks: what a registration or the class a package's use cases are named through would be the compiler's
    /// error over, in code nobody wrote.
    /// </summary>
    public static bool IsUnmet(ITypeParameterSymbol parameter, ITypeSymbol argument, Compilation compilation, CancellationToken cancellationToken)
        => IsRequiredBy(parameter) && Lacks(argument, compilation, cancellationToken);

    /// <summary>
    /// Whether DDD00067 says it on the class declared with the template whose attribute has
    /// <paramref name="templateMetadataName"/>: its marker says the package makes the ids of the template's classes,
    /// so what is closed over the class's id stands back without a second word.
    /// </summary>
    public static bool IsSaidOnTheClass(string templateMetadataName, Compilation compilation)
        => compilation.GetTypeByMetadataName(templateMetadataName) is { } attribute && EntityDeclarations.TemplateOf(attribute) is { CreatesIds: true };

    /// <summary>
    /// What a new value of <paramref name="value"/> could be, phrased to follow "with a new long made in code:", for
    /// the message that asks for a <c>Create()</c>.
    /// </summary>
    public static string ExampleFor(ITypeSymbol? value)
        => value?.SpecialType switch
        {
            SpecialType.System_Int64 or SpecialType.System_UInt64
                => "a snowflake, or the next number of a block a HiLo sequence hands out",

            // A snowflake is 64 bits, more than an int holds.
            SpecialType.System_Int32 or SpecialType.System_UInt32
                => "the next number of a block a HiLo sequence hands out",
            SpecialType.System_String => "a ULID, or another text no other row has",
            _ when value is not null && IsGuid(value) => "Guid.CreateVersion7(), for one in time order",
            _ => "one no other row has",
        };

    /// <summary>The value an id is declared over, <c>T</c> of its <c>[EntityId&lt;T&gt;]</c>, or what it holds as <c>Value</c>; null when neither shows.</summary>
    public static ITypeSymbol? ValueOf(INamedTypeSymbol id)
        => DeclaredValueOf(id) ?? id.GetMembers("Value").OfType<IPropertySymbol>().FirstOrDefault()?.Type;

    /// <summary>The <c>T</c> of an id's <c>[EntityId&lt;T&gt;]</c>, or null for a type without the attribute.</summary>
    private static ITypeSymbol? DeclaredValueOf(INamedTypeSymbol id)
    {
        foreach (var attribute in id.GetAttributes())
        {
            if (attribute.AttributeClass is { TypeArguments.Length: 1 } attributeClass && EntityDeclarations.Is(attributeClass, KnownTypes.EntityIdAttribute))
            {
                return attributeClass.TypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>
    /// What DDD00067 says <paramref name="id"/> lacks and what to write, and which code fix fits it. An id the
    /// generator completes, one with <c>[EntityId&lt;T&gt;]</c>, gets its <c>Create()</c> in its partial declaration
    /// and the interface from the generator; one written by hand needs both written; an id that has a fitting
    /// <c>Create()</c> lacks only the interface, which the generator adds where the id's project can see it; and a
    /// <c>Create()</c> that does not fit is made to. A fix is offered only where what it adds is all that is missing.
    /// </summary>
    public static IdShortfall ShortfallOf(INamedTypeSymbol id)
    {
        var name = id.Name;
        var value = ValueOf(id);
        var made = "with a new " + (value?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "value") + " made in code: " + ExampleFor(value);
        var creatable = "ICreatableEntityId<" + name + ">";
        var completed = DeclaredValueOf(id) is not null;
        var inThisProject = !id.DeclaringSyntaxReferences.IsEmpty;

        switch (DefinitionFactory.DeclaredCreateOf(id))
        {
            case DeclaredCreate.Fits when completed:
                // Only an id of a referenced project: one of this project is completed with the interface.
                return new IdShortfall(
                    "has a public static Create() but does not implement " + creatable,
                    "The generator implements it where the project that declares " + name + " can see it, in the net10.0 build of "
                    + "DDDToolkit.Abstractions: have that project target net10.0",
                    IdShortfall.NoFix);

            case DeclaredCreate.Fits:
                return new IdShortfall(
                    "has a public static Create() but does not implement " + creatable,
                    "Add " + creatable + " to the interfaces " + name + " implements" + (inThisProject ? string.Empty : ", in a project that targets net10.0"),
                    inThisProject ? IdShortfall.AddInterface : IdShortfall.NoFix);

            case DeclaredCreate.Other:
                return new IdShortfall(
                    "has a Create() that is not a public static one answering " + name,
                    "Make it public static " + name + " Create() => new(...), " + made,
                    IdShortfall.NoFix);

            default:
                return completed
                    ? new IdShortfall(
                        "has no public static Create()",
                        "Declare it in the partial declaration of " + name + ", public static " + name + " Create() => new(...), " + made,
                        IdShortfall.AddCreate)
                    : new IdShortfall(
                        "has no public static Create()",
                        "Declare it on " + name + ", public static " + name + " Create() => new(...), " + made + ", and add " + creatable
                        + " to the interfaces it implements",
                        inThisProject ? IdShortfall.AddCreateAndInterface : IdShortfall.NoFix);
        }
    }

    /// <summary>Whether a type shows <c>IEntityId</c>, as an id written by hand does and the author's part of a generated one does not.</summary>
    private static bool ShowsIEntityId(INamedTypeSymbol type)
        => type.AllInterfaces.Any(static @interface => @interface.ToDisplayString() == KnownTypes.EntityIdInterface);

    private static bool IsGuid(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid";
}

/// <summary>
/// What an id lacks to be made with <c>TId.Create()</c>, in the two phrases DDD00067 is written with, and the code fix
/// that adds it.
/// </summary>
/// <param name="Lacks">What the id has not, phrased to follow its name: "has no public static Create()".</param>
/// <param name="Advice">What to write, a sentence of its own without the full stop.</param>
/// <param name="Fix">Which fix the code fix offers, one of the constants below; <see cref="NoFix"/> for none.</param>
internal readonly record struct IdShortfall(string Lacks, string Advice, string Fix)
{
    /// <summary>The key of <see cref="Fix"/> among the diagnostic's properties.</summary>
    public const string Property = "IdFix";

    /// <summary>
    /// No fix: what is missing is not in a declaration of this project, a <c>Create()</c> that does not fit is the
    /// author's to change, or the project that declares the id targets a framework without the interface.
    /// </summary>
    public const string NoFix = "";

    /// <summary>A <c>Create()</c> in the partial declaration of an id the generator completes with the interface.</summary>
    public const string AddCreate = "Create";

    /// <summary>A <c>Create()</c> and the interface, for an id of this project written by hand.</summary>
    public const string AddCreateAndInterface = "CreateAndInterface";

    /// <summary>The interface alone, for an id of this project written by hand with a fitting <c>Create()</c>.</summary>
    public const string AddInterface = "Interface";
}
