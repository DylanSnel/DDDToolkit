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
    private const string Modules = "DDDToolkit.Modules";
    private const string Invariants = "DDDToolkit.Invariants";
    private const string Supabase = "DDDToolkit.Supabase";
    private const string GraphQL = "DDDToolkit.GraphQL";
    private const string IntegrationEvents = "DDDToolkit.IntegrationEvents";

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

    public static readonly DiagnosticDescriptor TypeIsNotPublishedByItsModule = new(
        id: "DDD00022",
        title: "Use only what another module publishes",
        messageFormat: "'{0}' belongs to module '{1}' and is not part of its published contract, so module '{2}' cannot name it; mark it [ModuleContract] in '{1}' if it really is published, or go through something that is",
        category: Modules,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A module is worth having only if the code outside it is held to its front door. The published contract of a module is every type it marks with [ModuleContract], plus every type it marks with [IntegrationEvent]; everything else is an implementation detail that the owning team is free to change. This rule reports where one module names another module's implementation detail. It is silent unless both assemblies declare [assembly: Module], so a framework assembly, a NuGet package or a shared kernel is never in the way.");

    public static readonly DiagnosticDescriptor DoNotHoldAnotherModulesEntity = new(
        id: "DDD00023",
        title: "Do not hold another module's entity",
        messageFormat: "'{0}.{1}' holds '{2}', an entity of module '{3}'; hold its identifier, or react to what module '{3}' publishes, so the two modules stay separately loadable and deployable",
        category: Modules,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "This is the boundary a modular monolith is built to keep. A field or property typed as another module's entity or aggregate root is a navigation: Entity Framework loads across the boundary, one save writes into two modules, and the modules can no longer be tested, versioned or split apart on their own. Publishing the entity does not fix it, which is why this rule fires whether or not the type is part of the other module's contract. Hold the other module's published identifier when you need to point at it, and let an integration event tell you when it changes.");

    public static readonly DiagnosticDescriptor InvariantMustBeNestedInItsSubject = new(
        id: "DDD00024",
        title: "An invariant must be nested inside the entity it is about",
        messageFormat: "'{0}' implements IInvariant<{1}> but is not nested inside an entity, so nothing will ever run it; declare it inside '{1}'",
        category: Invariants,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The toolkit discovers rules by looking at the nested types of the entity it is generating, which is what lets a rule read the entity's private state and what keeps discovery free of a scan over the whole compilation. A rule declared anywhere else compiles, reads well, is covered by its own unit tests and never runs: it is the one failure this library is built to make impossible to ship unnoticed. Move the type inside the entity it is about, in a part of your own under an Invariants folder if you want a file per rule.");

    public static readonly DiagnosticDescriptor InvariantIsAboutAnotherType = new(
        id: "DDD00025",
        title: "An invariant is nested inside a type it is not about",
        messageFormat: "'{0}' is nested inside '{1}' but implements IInvariant<{2}>, so '{1}' will never run it; nest it inside '{2}', or state the rule as IInvariant<{1}>",
        category: Invariants,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An entity runs the nested rules that are about itself. A rule about another type is as invisible as one declared outside an entity altogether, and looks even more convincing because it is in the right kind of place. Usually the type argument was copied from a neighbouring rule.");

    public static readonly DiagnosticDescriptor InvariantCodeMustBeUnique = new(
        id: "DDD00026",
        title: "Two invariants of one entity share a code",
        messageFormat: "'{0}' returns the code '{1}', which '{2}' already returns; a caller that branches on the code cannot tell the two rules apart",
        category: Invariants,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The code exists so that a caller can act on a broken rule without matching on its message. Two rules of one entity answering to one code takes that away again, and the collection of violations then holds two entries a caller has no way to distinguish. Only codes this analyzer can read as a constant are compared: a code computed at run time is not guessed at.");

    public static readonly DiagnosticDescriptor InvariantNeedsAParameterlessConstructor = new(
        id: "DDD00027",
        title: "An invariant needs an accessible parameterless constructor",
        messageFormat: "'{0}' has no parameterless constructor that '{1}' can reach, so the generated code cannot create it; give it one, and keep the rule stateless",
        category: Invariants,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator creates one instance of every rule per entity type and reuses it for every check, which is why a rule must be stateless and constructible without arguments. Nothing is generated for a rule that is not, so this is an error rather than a warning: a rule the generator silently dropped would be exactly the kind of silence the rest of these diagnostics exist to prevent.");

    public static readonly DiagnosticDescriptor KeyPartOutsideAnEntity = new(
        id: "DDD00028",
        title: "A key part belongs on an entity or aggregate root",
        messageFormat: "'{0}.{1}' is marked [KeyPart], but '{0}' is neither an [AggregateRoot] nor an [Entity]; only those have a primary key for it to join",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[KeyPart] puts a property into the primary key ahead of the identifier. A value object, a plain class or an integration event has no identifier and no key, so the attribute would do nothing there, and an attribute that silently does nothing is the kind of mistake this toolkit reports instead of ignoring.");

    public static readonly DiagnosticDescriptor KeyPartHasPublicSetter = new(
        id: "DDD00029",
        title: "A key part should not have a public setter",
        messageFormat: "Key part '{0}.{1}' has a public setter; a key part that can be reassigned after the row is written is a bug waiting to happen, so make the setter private or remove it",
        category: Entities,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A key part is part of the primary key, and a primary key does not change once the row exists: Entity Framework refuses to save a modified key value, and every owned child's foreign key carries the same value. Set it once, through the constructor, and expose it get-only or with a private setter.");

    public static readonly DiagnosticDescriptor KeyPartsSpreadOverFiles = new(
        id: "DDD00030",
        title: "Declare all key parts of a type in one file",
        messageFormat: "'{0}' declares key parts in more than one file ({1}); the key follows declaration order, which is only defined within one file, so move them into one part of the class",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "With more than one [KeyPart], they join the primary key in the order they are declared. Across the files of a partial class there is no declaration order, only the order the compiler happens to read the files in, and a key whose column order depends on that could change with a rename. Nothing is generated for the type until its key parts are declared together.");

    public static readonly DiagnosticDescriptor SupabaseMigrationsFactoryUnusable = new(
        id: "DDD00031",
        title: "A [SupabaseMigrations] factory must be one the build can create",
        messageFormat: "'{0}' is marked [SupabaseMigrations] but {1}, so its migrations are not exported",
        category: Supabase,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The build exports a marked factory's migrations by creating the factory from generated code in the project that turns the export on. That needs a public, non-abstract, non-generic class with a public parameterless constructor that implements IDesignTimeDbContextFactory<TContext>. A factory that is not one is left out, and that is an error rather than a warning: a module whose migrations silently never reached Supabase would be found by a failing deployment instead of by the build.");

    public static readonly DiagnosticDescriptor NodeIdSerializerFromToolkitId = new(
        id: "DDD00032",
        title: "Do not ask HotChocolate's generator for a toolkit identifier's node id serializer",
        messageFormat: "AddNodeIdValueSerializerFrom<{0}>() writes a serializer that stores nothing: '{0}' is a toolkit identifier, and the generated GraphQL runtime bindings already register a working one",
        category: GraphQL,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "HotChocolate's generator builds the serializer from the properties the type declares in source. A toolkit identifier's Value is written by the toolkit's own generator, and source generators do not see each other's output, so HotChocolate finds no property and emits a serializer that writes an empty node id and reads every node id back as an empty identifier. It compiles and runs without a sign of trouble. The generated Add{Module}GraphQlRuntimeBindings() already registers a serializer that works for every identifier; remove this call.");

    public static readonly DiagnosticDescriptor IntegrationEventClassNotConstructible = new(
        id: "DDD00033",
        title: "The generated integration event registration must be able to construct the class",
        messageFormat: "'{0}' is left out of the generated integration event registration: {1}",
        category: IntegrationEvents,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The generated Add{Module}IntegrationEvents() constructs every outbound class and every handler with new, taking each constructor parameter from the scope the message is delivered in. It needs one accessible constructor with the most parameters, parameters it can resolve (no ref, out or params), and parameter types this assembly can see. A class it cannot construct is left out of the registration, so its events are not published or its contract is not handled; register it by hand or give it a constructor the registration can call.");
}
