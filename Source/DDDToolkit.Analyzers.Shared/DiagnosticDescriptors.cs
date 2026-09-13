using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// Every diagnostic the DDDToolkit generators can report. Ids are stable; do not renumber.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string ValueObjects = "DDDToolkit.ValueObjects";
    private const string Entities = "DDDToolkit.Entities";
    private const string EntityIds = "DDDToolkit.EntityIds";
    private const string Usage = "DDDToolkit.Usage";

    public static readonly DiagnosticDescriptor ValueObjectShouldBeRecord = new(
        id: "DDD00001",
        title: "Value objects must be records",
        messageFormat: "'{0}' is annotated with [{1}] and must be declared as a partial record class",
        category: ValueObjects,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The DDDToolkit generates a base type and equality members for value objects; that requires a (non-struct) record. Nothing is generated for this type until it is a record.");

    public static readonly DiagnosticDescriptor EntityShouldBeClass = new(
        id: "DDD00002",
        title: "Entities must be classes",
        messageFormat: "'{0}' is annotated with [{1}] and must be declared as a partial class (not a record or struct)",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Entities and aggregate roots have identity, not value semantics, and derive from a generated base class. Nothing is generated for this type until it is a class.");

    public static readonly DiagnosticDescriptor EntityIdShouldBeRecord = new(
        id: "DDD00003",
        title: "Entity ids must be records",
        messageFormat: "'{0}' is annotated with [EntityId] and must be declared as a partial record class or a partial record struct",
        category: EntityIds,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Entity ids rely on record equality. Use 'partial record' for a reference id or 'readonly partial record struct' for an allocation-free id. Nothing is generated for this type until it is a record.");

    public static readonly DiagnosticDescriptor EntityIdStructShouldBeReadonly = new(
        id: "DDD00004",
        title: "Entity id structs should be readonly",
        messageFormat: "'{0}' is a record struct entity id; declare it 'readonly' so it cannot be mutated and to avoid defensive copies",
        category: EntityIds,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TypeShouldBePartial = new(
        id: "DDD00005",
        title: "DDDToolkit types must be partial",
        messageFormat: "'{0}' is annotated with [{1}] and must be declared 'partial' so the generator can add members",
        category: Usage,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator adds a second declaration of the type. Nothing is generated for this type until it is partial.");

    public static readonly DiagnosticDescriptor TypeCannotBeGeneric = new(
        id: "DDD00006",
        title: "DDDToolkit types cannot be generic",
        messageFormat: "'{0}' is annotated with [{1}]; it must not have type parameters, nor be nested in a type that has them",
        category: Usage,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated members name the type from places that cannot see a type parameter - an attribute argument and a registration method outside the type - so an open generic cannot be completed this way. Nothing is generated for this type until the type parameters are gone.");

    public static readonly DiagnosticDescriptor GeneratedIdNameTaken = new(
        id: "DDD00007",
        title: "The generated identifier name is already taken",
        messageFormat: "'{0}' is annotated with [{1}] over a raw value, so the toolkit would generate the identifier '{2}', but '{2}' already exists here; write [{1}<{2}>] if that type is the identifier, or rename one of the two",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The identifier generated from an entity declaration is named after the entity, with 'Id' appended. Another type of that name in the same namespace or containing type would be a duplicate definition. The generator can add members to an existing 'partial record struct' of that name, so an author can extend the identifier; anything else is reported here. Nothing is generated for this entity until the clash is gone.");

    public static readonly DiagnosticDescriptor UnsupportedIdTypeArgument = new(
        id: "DDD00008",
        title: "The identifier type argument is not supported",
        messageFormat: "'{0}' is annotated with [{1}] over '{2}', which is neither a strongly typed identifier nor a value the toolkit can generate one from; use a type marked with [EntityId<T>], or a value type or string",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The type argument names either the identifier itself (a type marked with [EntityId<T>], or any type implementing IEntityId) or the raw value a generated identifier should wrap. A reference type other than string is neither: it can be null, it is not copied by value, and an identifier has to be both. Nothing is generated for this entity until the type argument is one of the two.");

    public static readonly DiagnosticDescriptor ConflictingEntityAttributes = new(
        id: "DDD00009",
        title: "A type is either an entity or an aggregate root",
        messageFormat: "'{0}' carries both [Entity] and [AggregateRoot]; keep the one that describes it",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An aggregate root is the consistency boundary; a child entity lives inside one. A type cannot be both, and the two attributes generate different base types for the same declaration. Nothing is generated for this type until one of them is removed.");

    public static readonly DiagnosticDescriptor UseProtectedSetters = new(
        id: "DDD00010",
        title: "Value object properties must use protected setters",
        messageFormat: "Property '{0}' should have a protected setter (use 'protected init')",
        category: ValueObjects,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A record with non-protected setters can be cloned with the 'with' keyword, which would allow an object to be created in an invalid state. The generated always-valid twin also needs to be able to copy the value.");

    public static readonly DiagnosticDescriptor UseInitSetters = new(
        id: "DDD00011",
        title: "Value object properties must use init setters",
        messageFormat: "Property '{0}' should have an init setter (use 'protected init')",
        category: ValueObjects,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "With a non-init setter every type deriving from this record could mutate the object after creation. Value objects are immutable.");

    public static readonly DiagnosticDescriptor ValueObjectsCantBeSealed = new(
        id: "DDD00013",
        title: "Value objects cannot be sealed",
        messageFormat: "'{0}' is sealed, so its always-valid twin 'Valid{0}' cannot be generated",
        category: ValueObjects,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator derives an always-valid twin from every value object record. Remove 'sealed'.");

    public static readonly DiagnosticDescriptor CollectionPropertyMustBeGetOnly = new(
        id: "DDD00020",
        title: "Generated collection properties must be get-only",
        messageFormat: "Partial collection property '{0}' must be get-only; the generator exposes a read-only view over the generated backing field '{1}'",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Declare the property as 'public partial IReadOnlyList<T> Items { get; }'. Mutate the collection through the generated private field from inside the entity.");

    public static readonly DiagnosticDescriptor ReferenceOtherAggregatesById = new(
        id: "DDD00021",
        title: "Reference another aggregate by its id",
        messageFormat: "'{0}.{1}' holds the aggregate root '{2}' directly; hold '{3}' instead, so each aggregate stays a separate loading and consistency boundary",
        category: Entities,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An aggregate root is the boundary of one load and one transaction. A field or property typed as another root pulls that root inside this one: Entity Framework builds a navigation from it, a single save then writes two roots, and neither concurrency version guards its own aggregate any more. Holding the other root's id keeps the boundary intact and makes loading the other aggregate a decision you write down. The one reference this rule allows is a child entity navigating back to the root that owns it, which is the inverse navigation Entity Framework needs.");
}
