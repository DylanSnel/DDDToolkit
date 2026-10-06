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
    private const string Events = "DDDToolkit.Events";
    private const string Access = "DDDToolkit.Access";
    private const string Membership = "DDDToolkit.Membership";
    private const string Tenancy = "DDDToolkit.Tenancy";

    /// <summary>
    /// The reference page of docs/diagnostics.md on the docs site, where every id is a heading of its own.
    /// An IDE opens it from the id in the error list, and an AI agent reading the build output follows it.
    /// </summary>
    private const string HelpLinkBase = "https://dylansnel.github.io/DDDToolkit/docs/diagnostics#";

    private static DiagnosticDescriptor Create(
        string id,
        string title,
        string messageFormat,
        string category,
        DiagnosticSeverity defaultSeverity,
        bool isEnabledByDefault,
        string? description = null)
        => new(id, title, messageFormat, category, defaultSeverity, isEnabledByDefault, description, helpLinkUri: HelpLinkBase + id.ToLowerInvariant());

    public static readonly DiagnosticDescriptor ValueObjectShouldBeRecord = Create(
        id: "DDD00001",
        title: "Value objects must be records",
        messageFormat: "'{0}' is annotated with [{1}] and must be declared as a partial record class",
        category: ValueObjects,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The DDDToolkit generates a base type and equality members for value objects; that requires a (non-struct) record. Nothing is generated for this type until it is a record.");

    public static readonly DiagnosticDescriptor EntityShouldBeClass = Create(
        id: "DDD00002",
        title: "Entities must be classes",
        messageFormat: "'{0}' is annotated with [{1}] and must be declared as a partial class (not a record or struct)",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Entities and aggregate roots have identity, not value semantics, and derive from a generated base class. Nothing is generated for this type until it is a class.");

    public static readonly DiagnosticDescriptor EntityIdShouldBeRecord = Create(
        id: "DDD00003",
        title: "Entity ids must be records",
        messageFormat: "'{0}' is annotated with [EntityId] and must be declared as a partial record class or a partial record struct",
        category: EntityIds,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Entity ids rely on record equality. Use 'partial record' for a reference id or 'readonly partial record struct' for an allocation-free id. Nothing is generated for this type until it is a record.");

    public static readonly DiagnosticDescriptor EntityIdStructShouldBeReadonly = Create(
        id: "DDD00004",
        title: "Entity id structs should be readonly",
        messageFormat: "'{0}' is a record struct entity id; declare it 'readonly' so it cannot be mutated and to avoid defensive copies",
        category: EntityIds,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TypeShouldBePartial = Create(
        id: "DDD00005",
        title: "DDDToolkit types must be partial",
        messageFormat: "'{0}' is annotated with [{1}] and must be declared 'partial' so the generator can add members",
        category: Usage,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator adds a second declaration of the type. Nothing is generated for this type until it is partial.");

    public static readonly DiagnosticDescriptor TypeCannotBeGeneric = Create(
        id: "DDD00006",
        title: "DDDToolkit types cannot be generic",
        messageFormat: "'{0}' is annotated with [{1}]; it must not have type parameters, nor be nested in a type that has them",
        category: Usage,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated members name the type from places that cannot see a type parameter - an attribute argument and a registration method outside the type - so an open generic cannot be completed this way. Nothing is generated for this type until the type parameters are gone.");

    public static readonly DiagnosticDescriptor GeneratedIdNameTaken = Create(
        id: "DDD00007",
        title: "The generated identifier name is already taken",
        messageFormat: "'{0}' is annotated with [{1}] over a raw value, so the toolkit would generate the identifier '{2}', but '{2}' already exists here; write [{1}<{2}>] if that type is the identifier, or rename one of the two",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The identifier generated from an entity declaration is named after the entity, with 'Id' appended. Another type of that name in the same namespace or containing type would be a duplicate definition. The generator can add members to an existing 'partial record struct' of that name, so an author can extend the identifier; anything else is reported here. Nothing is generated for this entity until the clash is gone.");

    public static readonly DiagnosticDescriptor UnsupportedIdTypeArgument = Create(
        id: "DDD00008",
        title: "The identifier type argument is not supported",
        messageFormat: "'{0}' is annotated with [{1}] over '{2}', which is neither a strongly typed identifier nor a value the toolkit can generate one from; use a type marked with [EntityId<T>], or a value type or string",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The type argument names either the identifier itself (a type marked with [EntityId<T>], or any type implementing IEntityId) or the raw value a generated identifier should wrap. A reference type other than string is neither: it can be null, it is not copied by value, and an identifier has to be both. Nothing is generated for this entity until the type argument is one of the two.");

    public static readonly DiagnosticDescriptor ConflictingEntityAttributes = Create(
        id: "DDD00009",
        title: "A type is either an entity or an aggregate root",
        messageFormat: "'{0}' carries both [Entity] and [AggregateRoot]; keep the one that describes it",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An aggregate root is the consistency boundary; a child entity lives inside one. A type cannot be both, and the two attributes generate different base types for the same declaration. Nothing is generated for this type until one of them is removed.");

    public static readonly DiagnosticDescriptor UseProtectedSetters = Create(
        id: "DDD00010",
        title: "Value object properties must use protected setters",
        messageFormat: "Property '{0}' should have a protected setter (use 'protected init')",
        category: ValueObjects,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A record with non-protected setters can be cloned with the 'with' keyword, which would allow an object to be created in an invalid state. The generated always-valid twin also needs to be able to copy the value.");

    public static readonly DiagnosticDescriptor UseInitSetters = Create(
        id: "DDD00011",
        title: "Value object properties must use init setters",
        messageFormat: "Property '{0}' should have an init setter (use 'protected init')",
        category: ValueObjects,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "With a non-init setter every type deriving from this record could mutate the object after creation. Value objects are immutable.");

    public static readonly DiagnosticDescriptor ValueObjectsCantBeSealed = Create(
        id: "DDD00013",
        title: "Value objects cannot be sealed",
        messageFormat: "'{0}' is sealed, so its always-valid twin 'Valid{0}' cannot be generated",
        category: ValueObjects,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator derives an always-valid twin from every value object record. Remove 'sealed'.");

    public static readonly DiagnosticDescriptor BuildPropertiesNotDeclared = Create(
        id: "DDD00014",
        title: "The generators cannot read the project's MSBuild properties",
        messageFormat: "'{0}' is compiled without the DDDToolkit build properties, so DDD_Module is ignored and generated names fall back to the assembly name; let the build assets of the DDDToolkit.Analyzers package through, or add <CompilerVisibleProperty Include=\"DDD_Module\" /> to the project",
        category: Usage,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A source generator only sees an MSBuild property the project lists as a CompilerVisibleProperty. The DDDToolkit.Analyzers package lists them in its build and buildTransitive props, for every project its generators arrive in. A property that is listed and not set reaches the generator as an empty value; here it did not reach it at all, so that file was not imported: the reference excludes the package's build assets, or the generator was added as a bare analyzer assembly. Whatever the project sets is then ignored, and {Module}EventNames, Add{Module}Converters, Add{Module}IntegrationEvents and Add{Module}GraphQlRuntimeBindings are named after the assembly. Not reported for an assembly that declares [assembly: Module]: the module names all four, and the property is not read.");

    public static readonly DiagnosticDescriptor CollectionPropertyMustBeGetOnly = Create(
        id: "DDD00020",
        title: "Generated collection properties must be get-only",
        messageFormat: "Partial collection property '{0}' must be get-only; the generator exposes a read-only view over the generated backing field '{1}'",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Declare the property as 'public partial IReadOnlyList<T> Items { get; }'. Mutate the collection through the generated private field from inside the entity.");

    public static readonly DiagnosticDescriptor ReferenceOtherAggregatesById = Create(
        id: "DDD00021",
        title: "Reference another aggregate by its id",
        messageFormat: "'{0}.{1}' holds the aggregate root '{2}' directly; hold '{3}' instead, so each aggregate stays a separate loading and consistency boundary",
        category: Entities,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An aggregate root is the boundary of one load and one transaction. A field or property typed as another root pulls that root inside this one: Entity Framework builds a navigation from it, a single save then writes two roots, and neither concurrency version guards its own aggregate any more. Holding the other root's id keeps the boundary intact and makes loading the other aggregate a decision you write down. The one reference this rule allows is a child entity navigating back to the root that owns it, which is the inverse navigation Entity Framework needs.");

    public static readonly DiagnosticDescriptor TypeIsNotPublishedByItsModule = Create(
        id: "DDD00022",
        title: "Use only what another module publishes",
        messageFormat: "'{0}' belongs to module '{1}' and is not part of its published contract, so module '{2}' cannot name it; mark it [ModuleContract] in '{1}' if it really is published, or go through something that is",
        category: Modules,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A module is worth having only if the code outside it is held to its front door. The published contract of a module is every type it marks with [ModuleContract], plus every type it marks with [IntegrationEvent]; everything else is an implementation detail that the owning team is free to change. This rule reports where one module names another module's implementation detail. It is silent unless both assemblies declare [assembly: Module], so a framework assembly, a NuGet package or a shared kernel is never in the way.");

    public static readonly DiagnosticDescriptor DoNotHoldAnotherModulesEntity = Create(
        id: "DDD00023",
        title: "Do not hold another module's entity",
        messageFormat: "'{0}.{1}' holds '{2}', an entity of module '{3}'; hold its identifier, or react to what module '{3}' publishes, so the two modules stay separately loadable and deployable",
        category: Modules,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "This is the boundary a modular monolith is built to keep. A field or property typed as another module's entity or aggregate root is a navigation: Entity Framework loads across the boundary, one save writes into two modules, and the modules can no longer be tested, versioned or split apart on their own. Publishing the entity does not fix it, which is why this rule fires whether or not the type is part of the other module's contract. Hold the other module's published identifier when you need to point at it, and let an integration event tell you when it changes.");

    public static readonly DiagnosticDescriptor InvariantMustBeNestedInItsSubject = Create(
        id: "DDD00024",
        title: "An invariant must be nested inside the entity it is about",
        messageFormat: "'{0}' implements IInvariant<{1}> but is not nested inside an entity, so nothing will ever run it; declare it inside '{1}'",
        category: Invariants,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The toolkit discovers rules by looking at the nested types of the entity it is generating, which is what lets a rule read the entity's private state and what keeps discovery free of a scan over the whole compilation. A rule declared anywhere else compiles, reads well, is covered by its own unit tests and never runs: it is the one failure this library is built to make impossible to ship unnoticed. Move the type inside the entity it is about, in a part of your own under an Invariants folder if you want a file per rule.");

    public static readonly DiagnosticDescriptor InvariantIsAboutAnotherType = Create(
        id: "DDD00025",
        title: "An invariant is nested inside a type it is not about",
        messageFormat: "'{0}' is nested inside '{1}' but implements IInvariant<{2}>, so '{1}' will never run it; nest it inside '{2}', or state the rule as IInvariant<{1}>",
        category: Invariants,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An entity runs the nested rules that are about itself. A rule about another type is as invisible as one declared outside an entity altogether, and looks even more convincing because it is in the right kind of place. Usually the type argument was copied from a neighbouring rule.");

    public static readonly DiagnosticDescriptor InvariantCodeMustBeUnique = Create(
        id: "DDD00026",
        title: "Two invariants of one entity share a code",
        messageFormat: "'{0}' returns the code '{1}', which '{2}' already returns; a caller that branches on the code cannot tell the two rules apart",
        category: Invariants,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The code exists so that a caller can act on a broken rule without matching on its message. Two rules of one entity answering to one code takes that away again, and the collection of violations then holds two entries a caller has no way to distinguish. Only codes this analyzer can read as a constant are compared: a code computed at run time is not guessed at.");

    public static readonly DiagnosticDescriptor InvariantNeedsAParameterlessConstructor = Create(
        id: "DDD00027",
        title: "An invariant needs an accessible parameterless constructor",
        messageFormat: "'{0}' has no parameterless constructor that '{1}' can reach, so the generated code cannot create it; give it one, and keep the rule stateless",
        category: Invariants,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator creates one instance of every rule per entity type and reuses it for every check, which is why a rule must be stateless and constructible without arguments. Nothing is generated for a rule that is not, so this is an error rather than a warning: a rule the generator silently dropped would be exactly the kind of silence the rest of these diagnostics exist to prevent.");

    public static readonly DiagnosticDescriptor KeyPartOutsideAnEntity = Create(
        id: "DDD00028",
        title: "A key part belongs on an entity or aggregate root",
        messageFormat: "'{0}.{1}' is marked [KeyPart], but '{0}' is neither an [AggregateRoot] nor an [Entity]; only those have a primary key for it to join",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[KeyPart] puts a property into the primary key ahead of the identifier. A value object, a plain class or an integration event has no identifier and no key, so the attribute would do nothing there, and an attribute that silently does nothing is the kind of mistake this toolkit reports instead of ignoring.");

    public static readonly DiagnosticDescriptor KeyPartHasPublicSetter = Create(
        id: "DDD00029",
        title: "A key part should not have a public setter",
        messageFormat: "Key part '{0}.{1}' has a public setter; a key part that can be reassigned after the row is written is a bug waiting to happen, so make the setter private or remove it",
        category: Entities,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A key part is part of the primary key, and a primary key does not change once the row exists: Entity Framework refuses to save a modified key value, and every owned child's foreign key carries the same value. Set it once, through the constructor, and expose it get-only or with a private setter.");

    public static readonly DiagnosticDescriptor KeyPartsSpreadOverFiles = Create(
        id: "DDD00030",
        title: "Declare all key parts of a type in one file",
        messageFormat: "'{0}' declares key parts in more than one file ({1}); the key follows declaration order, which is only defined within one file, so move them into one part of the class",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "With more than one [KeyPart], they join the primary key in the order they are declared. Across the files of a partial class there is no declaration order, only the order the compiler happens to read the files in, and a key whose column order depends on that could change with a rename. Nothing is generated for the type until its key parts are declared together.");

    public static readonly DiagnosticDescriptor SupabaseMigrationsFactoryUnusable = Create(
        id: "DDD00031",
        title: "A [SupabaseMigrations] factory must be one the build can create",
        messageFormat: "'{0}' is marked [SupabaseMigrations] but {1}, so its migrations are not exported",
        category: Supabase,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The build exports a marked factory's migrations by creating the factory from generated code in the project that turns the export on. That needs a public, non-abstract, non-generic class with a public parameterless constructor that implements IDesignTimeDbContextFactory<TContext>. A factory that is not one is left out, and that is an error rather than a warning: a module whose migrations silently never reached Supabase would be found by a failing deployment instead of by the build.");

    public static readonly DiagnosticDescriptor NodeIdSerializerFromToolkitId = Create(
        id: "DDD00032",
        title: "Do not ask HotChocolate's generator for a toolkit identifier's node id serializer",
        messageFormat: "AddNodeIdValueSerializerFrom<{0}>() writes a serializer that stores nothing: '{0}' is a toolkit identifier, and the generated GraphQL runtime bindings already register a working one",
        category: GraphQL,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "HotChocolate's generator builds the serializer from the properties the type declares in source. A toolkit identifier's Value is written by the toolkit's own generator, and source generators do not see each other's output, so HotChocolate finds no property and emits a serializer that writes an empty node id and reads every node id back as an empty identifier. It compiles and runs without a sign of trouble. The generated Add{Module}GraphQlRuntimeBindings() already registers a serializer that works for every identifier; remove this call.");

    public static readonly DiagnosticDescriptor IntegrationEventClassNotConstructible = Create(
        id: "DDD00033",
        title: "The generated integration event registration must be able to construct the class",
        messageFormat: "'{0}' is left out of the generated integration event registration: {1}",
        category: IntegrationEvents,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The generated Add{Module}IntegrationEvents() constructs every outbound class and every handler with new, taking each constructor parameter from the scope the message is delivered in. It needs one accessible constructor with the most parameters, parameters it can resolve (no ref, out or params), and parameter types this assembly can see. A class it cannot construct is left out of the registration, so its events are not published or its contract is not handled; register it by hand or give it a constructor the registration can call. The registration also names the domain events of the module's projects that do not reference Entity Framework, such as its domain project, and a domain event of one of those that this project cannot see is reported on its [assembly: Module] attribute: make the event public.");

    public static readonly DiagnosticDescriptor EventVersionDisagreesWithItsName = Create(
        id: "DDD00034",
        title: "An event's class name and its Version disagree",
        messageFormat: "'{0}' is version {2}, as its [IntegrationEvent] says, so the V{1} its name ends in is ignored; rename the class to end in V{2}, or remove Version if the name was right",
        category: Events,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A class name that ends in V and a number is the event's version by convention: OrderPlacedV2 is version 2 of its event. Version on [IntegrationEvent] states it explicitly, and a stated version wins wherever the version is read. Written both ways and different, the name says one thing and the event is another, which is how a reader ends up writing an upcaster for the wrong version, so the build says so.");

    public static readonly DiagnosticDescriptor EventVersionSuffixIsNotAVersion = Create(
        id: "DDD00035",
        title: "An event's class name ends in something that is not a version",
        messageFormat: "'{0}' ends in 'V{1}', which reads as a version but is not one: versions start at V1 and have no leading zeros",
        category: Events,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A class name that ends in V and a number carries the event's version, and every event class name is read that way. V0 and a number with a leading zero, such as V01, cannot be a version, and reading them as part of the name instead would give one event a version everywhere else and not here. Rename the class: V1 for a first version, or a name that does not end in V and digits.");

    public static readonly DiagnosticDescriptor EventNameTaken = Create(
        id: "DDD00036",
        title: "Two events of one module share a name and version",
        messageFormat: "'{0}' and '{1}' are both stored or published as '{2}' version {3}, so a message could not say which of them it is; rename one, or pin another name on one with {4}",
        category: Events,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An event is found by its name and version when a stored row or a delivered message is read back. Two domain events, or two published contracts, of one module under the same name and version cannot both be found, and the registry would refuse the second at start-up. Usually two classes in different namespaces share a class name, and the convention gives both the module's name and that class name. The name is deliberately not made unique from the namespace: that would change a name the moment the class moved, or the moment a second class of the same name appeared.");

    public static readonly DiagnosticDescriptor EventNameConstantTaken = Create(
        id: "DDD00037",
        title: "Two event names give one constant name",
        messageFormat: "'{0}' in the generated {3} would be the constant for both '{1}' and '{2}'; pin one of the two names to something that reads differently",
        category: Events,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every name the module's events are stored or published under gets a constant in the generated {Module}EventNames class, named after the name without its module: ordering.order-placed becomes OrderPlaced. Two names that differ only in punctuation, such as order-placed and order.placed, would give one constant. Whichever name it held, code that used it for the other event would bind a topic or a test to the wrong event without a word, so the build stops instead. It is reported on the events of both names, because neither is more wrong than the other.");

    public static readonly DiagnosticDescriptor RowAccessRuleShape = Create(
        id: "DDD00038",
        title: "A row access rule, an access function or a database question has the shape the generator reads",
        messageFormat: "'{0}' is a [RowAccess] rule, an [AccessFunction], an access function's contract or a database question, and needs {1}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator translates a rule's Allows method into SQL and writes the result into another part of the class. So the class is static and partial, and Allows is a static method that takes the aggregate the rule is about and a Caller, returns bool, and has a single expression for a body, either after => or as its only return statement. An [AccessFunction] has the same shape, and may take strings, numbers, flags, Guids and ids after the caller, which become the SQL function's parameters. A contract declares nothing, or one static partial Allows that takes the key first, or one static partial Ids that returns AccessSet of the key. A question of an [AccessFunctions] class is a static partial method: an [AccessSet] one returns AccessSet<T>, an [AccessScalar] one returns a value, and their parameters are strings, numbers, flags, Guids, ids or a type parameter constrained to IEntityId. A function's name is schema.name, owner/name, or a name relative to its owner, each part letters, digits and underscores. A set-shaped function answers with the keys of the rows it allows, so its aggregate's key is one column. A column rule, a rule with Columns, is for RowOperations.Change alone, and names properties of its aggregate, or of a value object it holds written with a dot as \"Value.Property\", and no collection of its entities; it is asked in a trigger whose search path is empty, so a function it calls with Sql.Call names its schema. Nothing is generated until the class has its shape, and a rule without SQL is never written into the database.");

    public static readonly DiagnosticDescriptor RowAccessRuleUntranslatable = Create(
        id: "DDD00039",
        title: "A row access rule can only say what the database can check",
        messageFormat: "'{0}' cannot be part of a row access rule: a rule compares, and-s, or-s and negates properties of the aggregate, constants, the caller's UserId, IsSignedIn, Role and Claim(\"...\"), the time as DateTimeOffset.UtcNow or DateTime.UtcNow, SQL written with Sql.Call or Sql.Raw, the Allows or Ids of an [AccessFunction] or its contract, and the questions of an [AccessFunctions] class",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every row access rule becomes a condition the database evaluates for each row, so it can only use what the database knows: the aggregate's own columns, constants written in the rule, and the caller's claims. A method call, a local variable or a field of another object has no column and no claim to become, and a rule that quietly left it out would let the database answer differently from the C# method. The clock is the database's now(), and a question only the database can answer is asked through an [AccessFunction] or an [AccessFunctions] class, whose SQL function the export writes or knows. A set-shaped question is asked with Contains, and nothing else. The error is on the part that cannot be translated.");

    public static readonly DiagnosticDescriptor RowAccessRuleNotOnAggregateRoot = Create(
        id: "DDD00040",
        title: "A row access rule guards an aggregate root",
        messageFormat: "'{0}' is about '{1}', which is not an aggregate root; put the rule on the root, and its entities follow it",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An aggregate is read and changed as a whole. A rule on one of its entities could hide some of an order's lines and not the order, and Entity Framework would load half an aggregate whose invariants then check half the data. So rules are written for the root, and the export gives every table of the aggregate's entities a policy that follows the root: a line is visible exactly when its order is.");

    public static readonly DiagnosticDescriptor RowAccessRuleReadsEntities = Create(
        id: "DDD00041",
        title: "A row access rule reads the aggregate's entities through an access function",
        messageFormat: "'{0}' reads the entities of '{1}', which a policy on its own table cannot do; put it in an [AccessFunction<{1}>] and call its Allows from the rule",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The tables of an aggregate's entities have policies that ask the aggregate's table whether their row is visible. A policy on the aggregate's table that read those tables would therefore ask itself, and Postgres stops the query with infinite recursion. An [AccessFunction] runs as its owner, SECURITY DEFINER, so it reads the entities without their policies; the rule calls it with the row's id, and the question is written once however many rules ask it.");

    public static readonly DiagnosticDescriptor EntityBaseShape = Create(
        id: "DDD00042",
        title: "A parent for entities is an abstract generic class whose first type parameter is the id",
        messageFormat: "'{0}' is marked [{1}] and needs {2}",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A package ships a parent for the application's own classes to derive from, and the generator writes its base class the way it does for any aggregate root or entity: AggregateRoot<TId> or Entity<TId>, closed over the parent's first type parameter. So the parent is an abstract partial class, since only what derives from it is ever created; it has type parameters, the id first; it is not nested in a generic type; and its id parameter is constrained with where TId : IEntityId, IEquatable<TId>, which is what the toolkit's base classes require. Nothing is generated for the parent until it has that shape.");

    public static readonly DiagnosticDescriptor TemplateIdIsNotAnEntityId = Create(
        id: "DDD00043",
        title: "A template's first type argument is an entity id",
        messageFormat: "'{0}' is declared with [{1}<{2}>], but '{2}' is not an entity id; declare it with [EntityId<T>], in the contracts project when other modules refer to it",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A template attribute such as [Subscription<SubscriptionId>] names the id of the class it declares, and that class derives from a parent closed over the id. Unlike [AggregateRoot<Guid>], a template never generates an id from a raw value: the id belongs to the application, which declares it where every module that refers to it can see it. Nothing is generated for the class until its id is an [EntityId<T>].");

    public static readonly DiagnosticDescriptor TemplateArgumentSourceMissing = Create(
        id: "DDD00044",
        title: "A template takes a type from a class nobody declares",
        messageFormat: "'{0}' is declared with [{1}], whose parent takes {2} from the class declared with [{3}], and neither this project nor a project it references declares one; declare one, once",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Some parents need more than the id of the class that derives from them: the parent of an invoice needs the subscription's id and the class of its lines. The template attribute takes those from the one class declared with the template it names, in this project or, when this project declares none, in a project it references, so each is declared once and every class agrees on it. Nothing is generated for the class until one of them declares the class this message names.");

    public static readonly DiagnosticDescriptor TemplateArgumentSourceAmbiguous = Create(
        id: "DDD00045",
        title: "A template takes a type from a class declared more than once",
        messageFormat: "'{0}' is declared with [{1}], which takes {2} from the class declared with [{3}], and there are several: {4}; {5}",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The template attribute takes a type argument of its parent from the one class declared with another template, such as the subscription's id from the class declared with [Subscription<TId>], and a [TemplateRegistration] method takes its type arguments the same way. With two such classes, in this project or in the projects it references when it declares none itself (only those of its own module, when it is a module's project that takes the registration's classes from the module's others), there is no telling which one is meant, and picking one would bind the parent, or the registration, to it without a word. Nothing is generated for the class, or no registration is written, until one is left. A template whose marker says AllowSeveral = true is declared once per thing an application has, and several classes are then no mistake for a registration: it is written once per class, each named after its class. It is still refused when two of the method's templates each have several classes, because there is no telling which class of the one goes with which of the other, and when two of the classes share a name, because the registrations are named after them. A method that says what its registration is called, with [TemplateRegistration(Name = ...)], names each after what its class fills the name with, and classes whose registrations would be called the same are refused for the same reason: a call could not tell them apart. That is reported once for the classes, however many of the package's registrations it stops, on the class to fix: where the type the registrations are named after holds one of the classes in a property or field of its own, as a resource holds its members, on another of them, and otherwise on the one declared last.");

    public static readonly DiagnosticDescriptor TemplateDoesNotFitItsParent = Create(
        id: "DDD00046",
        title: "A template attribute fills exactly the type parameters of its parent",
        messageFormat: "[{0}] cannot declare {1}: {2}",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "This is a mistake in the package that declares the template attribute. It is reported on the attribute when the package is built, and on a class that uses an attribute from a package built without the generator. The marker names an open parent marked [AggregateRootBase] for [AggregateRootTemplate], or [EntityBase] for [EntityTemplate]. The attribute's own type arguments fill the parent's first type parameters, the id first, and every parameter after them is filled by exactly one [TemplateArgument]. Type arguments the attribute has beyond what the parent takes are no mistake: they are the template's own, and a registration takes them by position. A parameter that takes the application's class (Take = TemplateArgumentKind.Type) is not constrained new(), because the parameterless constructor the generator writes is never public. Nothing is generated for a class declared with the attribute until the package is fixed.");

    public static readonly DiagnosticDescriptor ConflictingEntityDeclarations = Create(
        id: "DDD00047",
        title: "A class is declared an entity or aggregate root once",
        messageFormat: "'{0}' is declared with {1}; keep the one that describes it",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[AggregateRoot<TId>], [Entity<TId>], [AggregateRootBase], [EntityBase] and a package's template attributes each give the class a base class, and a class has only one. Nothing is generated for the class until one of them is left. [AggregateRoot<TId>] together with [Entity<TId>] is DDD00009.");

    public static readonly DiagnosticDescriptor TemplateArgumentMissesConstraint = Create(
        id: "DDD00048",
        title: "A class a template takes meets its parent's constraints",
        messageFormat: "'{0}' is declared with [{1}], whose parent takes '{2}' as '{3}', which requires {4}; '{2}' does not meet it",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A [TemplateArgument] with Take = Type hands one of the application's own classes to the parent as a type argument, such as the class of an invoice's lines. The parent may ask more of that class than being declared with the right template, such as an interface it creates the class through. A class that does not have it would make the parent closed over it a compile error inside generated code, where there is nothing to fix. Nothing is generated for the class until the class this message names meets the requirement.");

    public static readonly DiagnosticDescriptor TemplateRegistrationSourceMissing = Create(
        id: "DDD00049",
        title: "A template registration needs a class declared with each of its templates",
        messageFormat: "A class declared with [{1}] is needed by {0}, and neither this project nor a project it references declares one; declare one, once",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A package's [TemplateRegistration] method, such as a modelBuilder.AddTenancy(), is written into every project that declares a class with one of its templates, closed over that project's classes: each of its [TemplateType] type parameters takes the class declared with a template, or that class's id. The project declares a class with one of the method's templates, so it is meant to get the registration, and one of the other templates has no class, in this project or in the projects it references. It is reported once for the template, naming every registration that needs it, on the first class of the project declared with one of the methods' templates, and no registration is written until the class this message names is declared. Where a class of the project derives from a parent that takes a type from that template, DDD00044 says it on that class, with the same fix, and this one is not reported. A project of a module that declares none of the classes itself, such as a module's infrastructure project next to its domain project, gets the registration built from the classes the module's other projects declare, and only those: then one of the method's templates has a class there and another has none, and it is reported on the project's [assembly: Module] attribute. The class belongs in the project that declares the others.");

    public static readonly DiagnosticDescriptor TemplateRegistrationMissesConstraint = Create(
        id: "DDD00050",
        title: "A type a template registration takes meets the method's constraints",
        messageFormat: "'{1}' takes '{0}' as '{2}', which requires {3}; '{0}' does not meet it",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A [TemplateType] with Take = Type hands one of the application's own classes to a package's [TemplateRegistration] method as a type argument. The method may ask more of that class than being declared with the template, such as an interface. A class that does not have it would make the registration written for the project a compile error inside generated code, where there is nothing to fix. A [TemplateType] that takes an id is held to the same rule as far as a struct or a class goes: an id declared as a record class where the method asks for a struct is reported too. A later type argument of the template, taken with Argument = n, is whatever type the application wrote there, and is judged the way DDD00053 judges it against the parent: a struct or a class always, and the method's other constraints when the type is one no generator will still complete. A later type argument whose id the method takes, with IdOfArgument = true, is a class declared an entity or an aggregate root, since that is where its id is read from; a later type argument that names an entity or an aggregate root of this project is judged by what the generator will make of that class, as a class the method takes is, so a child entity where the method asks for an aggregate root is reported rather than left to fail inside the registration; and a type the registration is named after, with [TemplateRegistration(Name = ...)], has a name of its own, which an array has not. No registration is written until the type this message names meets the requirement.");

    public static readonly DiagnosticDescriptor SetQuestionArgumentReadsTheRow = Create(
        id: "DDD00051",
        title: "A set-shaped question is asked once per statement, so its arguments do not read the row",
        messageFormat: "An argument of '{0}' reads the row, so the database would ask it once per row. Pass constants, the caller, or the function's own parameters.",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A set-shaped question, an [AccessSet] method or the Ids of a set-shaped access function, becomes column = ANY (ARRAY(SELECT f(arguments))) in the policy. Postgres works the set out once, before it reads the table, and then finds the rows through the column's index. An argument that reads the row, a column of it or a question about it, would make the set different for every row, so Postgres would call the function once per row instead, which is what the set-shaped form exists to avoid. The value compared with the set, the argument of Contains, is what reads the row. Ask a question about one row through an access function's Allows instead. Nothing is generated for the rule until its arguments are constants, the caller, or the parameters of the access function the rule is part of.");

    public static readonly DiagnosticDescriptor FunctionNameWithoutOwner = Create(
        id: "DDD00052",
        title: "A function named without its schema belongs to a module",
        messageFormat: "'{0}' is named without its schema, and nothing says which module it belongs to. Add [assembly: Module(...)], give the class [AccessFunctions(Owner = ...)], or write schema.name.",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A function name without a schema is relative to the module that owns it: is_member in the Projects module is projects/is_member, and the export writes it as the function of that name in the schema of the context that defines it, whatever the host calls that schema. The owner is the name of the module the declaring assembly declares with [assembly: Module], or the Owner of the class's [AccessFunctions], which a package that declares no module uses. With neither there is no owner to make the name relative to, and a function of the same name in another module could not be told from it. Nothing is generated for the class until it has an owner or the name has its schema.");

    public static readonly DiagnosticDescriptor TemplateArgumentFailsConstraint = Create(
        id: "DDD00053",
        title: "A type argument of a template meets its parent's constraints",
        messageFormat: "'{0}' is declared with [{1}], but '{2}' does not meet the parent's constraint on '{3}', which requires {4}",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A class declared with a template derives from the package's parent, closed over the type arguments the template supplies: the attribute's own, the id first, and the ids a [TemplateArgument] takes from other classes. The parent may ask more of an id than being an entity id, most often a struct, because it holds ids by value. An id declared as a record class would make the parent closed over it a compile error inside generated code, where there is nothing to fix. Whether the type is a struct or a class is always judged. The parent's other constraints, an interface for instance, are judged of a type from a referenced project and of one written out in full; a partial type declared in this project may still be completed by a generator, as an [EntityId<T>] is, so what it does not show yet is left to the compiler. An [EntityId<T>] with an error of its own is not completed and is judged as it stands. Nothing is generated for the class until the type this message names meets the requirement: declare the id as a readonly partial record struct, or give the type what the message names.");

    public static readonly DiagnosticDescriptor RowAccessContributionNotUsed = Create(
        id: "DDD00054",
        title: "Use the row access contributions your references offer",
        messageFormat: "'{0}' offers the row access contribution '{1}', which this application does not use. {2}, to write its SQL into your migrations.",
        category: Supabase,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A package or a module offers a class that writes row level security of its own, SQL functions, policies and statements, with [assembly: RowAccessContribution]. The Supabase export writes it into this application's migrations only when the project that runs the export lists it with [assembly: UseRowAccessContribution], because the migrations run it as the role that owns the tables: nothing a reference offers gets there without the application's say. Without it, the package's tables may have no policies at all, and its rules' functions may be missing. List it, a class of yours derived from it, or, for a generic one, the class closed with your own types, to use it; if leaving it out is deliberate, suppress this warning for the project with <NoWarn>. The export creates what is listed with new X(), so a contribution that is generic, or whose constructor takes what only the application knows, its rules say, is listed through a class of yours: closed over your types, with a constructor that takes nothing and hands the base what it needs. The message says which of these the offer needs.");

    public static readonly DiagnosticDescriptor SupabaseMigrationsWithoutModule = Create(
        id: "DDD00055",
        title: "A context's migration files are named after its module",
        messageFormat: "'{0}' is in an assembly that declares no [assembly: Module], so its Supabase migration files are named after the context, '{1}'. Declare the module, and the file names stay the same when the context is renamed.",
        category: Supabase,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The Supabase export names every file it writes after the module the context belongs to, as in 20260922120000_AddOrders.ordering.ddd.sql, and finds a module's files again by that name. The module is the one the assembly of the factory or of the context declares with [assembly: Module]. With neither, the name is taken from the context's class instead, so renaming the class changes the name every file is expected under: the export then recognizes none of the files it wrote, reports every migration as VersionTaken, because the file under the old name holds its timestamp, and writes the module's access file once more. Add [assembly: Module(\"...\")] to the project that holds the context.");

    public static readonly DiagnosticDescriptor AccessRequestsShape = Create(
        id: "DDD00056",
        title: "A request interface is one a behavior can be written for",
        messageFormat: "'{0}' is marked [AccessRequests] and {1}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[AccessRequests] marks the interface a module's commands and queries implement to say what they require of their caller, and in a project that uses the Mediator library the generator writes the pipeline behavior that holds them to it, named after the interface and declared beside it. So the interface derives from IRequireAccess, which is where a request's requirement is read from; it has no type parameters, since the behavior and the module's set of checks are closed over it; it is not declared inside another type and is not file-local, since the behavior is written beside it, in a file of its own, and has to name it; and no other marked interface of the same namespace gives the behavior the same name. No behavior is written for the interface until it has that shape, so nothing checks its requests: put it right, or call AccessChecks<TRequests>.RequireAsync in front of the handlers yourself.");

    public static readonly DiagnosticDescriptor PipelineBehaviorShapeUnknown = Create(
        id: "DDD00057",
        title: "The Mediator library's pipeline behavior has the shape the generator writes a behavior for",
        messageFormat: "No access behavior is written for '{0}': the Mediator library this project references declares {2} otherwise than the generator knows it, {1}. Write the behavior yourself: its Handle is async, awaits AccessChecks<{0}>.RequireAsync(message, cancellationToken), and then awaits the next step.",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "For an interface marked [AccessRequests] the generator writes a class that implements the Mediator library's IPipelineBehavior<TMessage, TResponse>. It reads how from the library itself: one method, Handle, that takes the message, a cancellation token and the delegate that runs the next step, in whatever order the referenced version declares them, and answers a ValueTask or a Task of the response; the delegate takes the message and the token. Where the library has IStreamPipelineBehavior<TMessage, TResponse>, the pipeline of the messages that are answered with a stream, a second class is written for it the same way, whose Handle answers an IAsyncEnumerable of the response. A version of the library that declares either interface otherwise is one the generator does not know, and it writes nothing rather than guess: a behavior that did not compile, or one that never ran the check, would be worse than none, and so would one of the two without the other. It is an error because without the behavior the requests of the interface reach their handlers unchecked. Write the behavior by hand against the version you use, and remove [AccessRequests] from the interface, which then still names the module's checks.");

    public static readonly DiagnosticDescriptor NotificationRequiresAccess = Create(
        id: "DDD00058",
        title: "A notification implements no request interface",
        messageFormat: "'{0}' implements '{1}', which is marked [AccessRequests], and is a notification of the Mediator library. A notification is published to its handlers through no pipeline, so nothing asks what it requires. Send what needs a check as a command or a query, or take '{1}' off the notification and call AccessChecks<{1}>.RequireAsync where it is published.",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The behavior written for an [AccessRequests] interface is part of the Mediator library's pipeline for commands and queries, and of its pipeline for the messages that are answered with a stream. The library publishes a notification to its handlers through neither, so a notification that implements the interface declares a requirement that no behavior ever asks: every handler of it runs for whoever published it, while the declaration reads as if it were checked. It is an error for that reason. What needs a check before it is handled is sent as a command or a query. A notification says that something happened, and whoever publishes it has passed its own check already; where a notification's handlers must not run for every publisher, ask the checks yourself, AccessChecks<TRequests>.RequireAsync, before publishing.");

    public static readonly DiagnosticDescriptor HandlerCalledDirectly = Create(
        id: "DDD00061",
        title: "A request that declares its access is sent, not handed to its handler",
        messageFormat: "'{0}' is handed to its handler directly, past the pipeline, so nothing asks what it requires: it implements '{1}', which is marked [AccessRequests], and only the access behavior in the pipeline asks the checks. Send it with ISender instead.",
        category: Access,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The behavior written for an [AccessRequests] interface asks the module's checks in the Mediator library's pipeline, which a command or a query passes when it is sent: ISender.Send, or ISender.CreateStream for a query answered with a stream. A handler called directly, Handle on a class or an interface of the library's handlers (ICommandHandler, IQueryHandler, IRequestHandler and their stream kinds) with such a request, runs with nothing having asked what the request declares, and the database's policies, where there are any, are all that stand in its way: per table they are coarser than one request's requirement. So it is reported at the call, and at a reference to Handle that makes a delegate of it. Constructing or injecting a handler is not: the call is where the check is skipped. Calls in generated code, the library's own dispatch, are not reported, and neither is base.Handle in a handler that overrides it, nor a decorator that hands the handler it wraps the message it was given: both hand on the request that passed the pipeline on its way in. A dispatcher that calls Handle on a handler of a message type parameter with no constraint to a marked interface cannot be told from any other, and is not reported either. A test project, one with IsTestProject or IsTestingPlatformApplication set, reports nothing: a test that calls a handler on purpose tests the handler alone. Anywhere else, a call that is meant suppresses the warning where it is made, #pragma warning disable DDD00061, with the reason. A code fix sends the request with an ISender the code can reach, a parameter, a local, a field or a property, where there is one.");

    public static readonly DiagnosticDescriptor MemberClassOfNoResource = Create(
        id: "DDD00060",
        title: "A member class names an aggregate root whose members it is",
        messageFormat: "'{0}' is declared a member of '{1}', and {2}",
        category: Membership,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A class declared with the Membership package's member template, [Member<TId, TMemberId, TRoleId, TResource>], names the resource its members are of: an aggregate root of the application's, which keeps them in a collection of the member class and gets the member list written over it. Two things make that impossible, and this says which, on the member class, where the mistake is. The resource is not declared an aggregate root: a child entity, the member class itself, or a class that is no entity, whose members could not be kept with an aggregate. Or the resource keeps its members as another member class already, in a collection of that class: a resource has one member class, and the list is written for the one it keeps. A project that also gets the package's registrations hears the same from them, DDD00050 or DDD00045, which are errors there, and this one is not reported beside them. It is a warning otherwise, because nothing else is wrong with the class, and nothing is written for it.");

    public static readonly DiagnosticDescriptor MemberListNotWritten = Create(
        id: "DDD00059",
        title: "The member list of a resource is written from what the resource declares",
        messageFormat: "'{0}' has no member list, and the toolkit cannot write one over its '{1}': {2}. Put that right, or write the list yourself: private MemberList<{3}> Members => new(members, owner, newId, codes);.",
        category: Membership,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "For a class declared with the Membership package's member template, [Member<TId, TMemberId, TRoleId, TResource>], the toolkit writes the member list on the resource the template names: a private property Members, of MemberList<TMember, TId, TMemberId, TRoleId>, which the resource's own methods change its members through. It is written from four things the resource declares, and only when each can be told without a guess. The members: exactly one get-only partial property of IReadOnlyList<TMember>, IReadOnlyCollection<TMember> or IEnumerable<TMember>, which the toolkit backs with a list. The owner: exactly one property of TMemberId on the resource. New rows: TId is an [EntityId<Guid>], and a new one is made in time order. The codes: exactly one static property or field of MembershipCodes on the resource, the codes its member rules refuse under. And the resource has no member called Members of its own, and the member class's types are ones the generator can see: the id of an [AggregateRoot<Guid>] is written by another generator, which no generator sees, so an id a member is known by is declared with [EntityId<Guid>]. A resource that declares a MemberList itself, under whatever name, is left alone and hears nothing: that is the form for every other shape, a resource with two properties of the member's id, a member row keyed by something else than a Guid, or codes kept elsewhere. This warning is for a resource that has no member list at all: its members could not be changed, so say what is missing, or write the property by hand. A member class that names no aggregate root, or names one whose members are another class, is DDD00060 on the member class instead.");

    public static readonly DiagnosticDescriptor TenancyPermissionsUnreadable = Create(
        id: "DDD00063",
        title: "A module's keys marked [TenancyPermissions] are a list the project that composes the modules can read",
        messageFormat: "'{0}' is marked [TenancyPermissions] and {1}, so its keys reach no catalogue. Make it a public static property or field with a getter, declared in a class that is not generic, whose type is a sequence of Permission.",
        category: Tenancy,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A module states its permission keys once, on a static list marked [TenancyPermissions], and Tenancy's generator collects every marked list into each project that declares no module with [assembly: Module], such as the host: TenancyPermissionsOfModules.All, which an export builds the catalogue from, and services.AddTenancyPermissionsOfModules(), which the host registers them with. It reads a list as the expression Type.Member, so the list is a static property or field, readable, declared in a class that is not generic (nor nested in one) and that code can name, and its type is a sequence of Permission: IReadOnlyList<Permission>, IEnumerable<Permission> or an array. A static virtual or abstract member of an interface, and a member of an extension block or of a file-local type, cannot be read that way. In a library the list is also public, in public types: the projects that reference the library, the one that composes the modules among them, see nothing less of it, whether the library declares a module or not. Only an application, the program the modules are composed in, may keep a list of its own internal, since it collects that one itself; not private or protected, though, since the class it is collected into reads it from outside its type. A list that is none of these is left out of every collection, and nothing else would say so: the module's keys would be missing from the catalogue the host runs with, and asking about one would throw when it is asked. It is an error for that reason.");

    public static readonly DiagnosticDescriptor ModuleNotDeclaredByEveryProject = Create(
        id: "DDD00064",
        title: "Every project named after a module declares it",
        messageFormat: "This project and project '{0}', which it references, set DDD_Module to '{1}', and {2}, so {3}. Declare module '{1}' in {4}: <DDD_DeclareModule>true</DDD_DeclareModule> beside DDD_Module, or [assembly: Module(\"{1}\")].",
        category: Modules,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A module may be several projects, and the generators take the projects that declare the same module together: the project that holds the context gets the converters, the integration event registration and a package's registrations, such as AddTenancy(), written from what the module's other projects declare. A project that sets DDD_Module to the module's name and declares no module is left out of that: DDD_Module alone only names generated code. Without this warning the build goes on without a word, and the first sign is a call that does not compile, CS1061 or CS0234, or a converter missing when the model is built. It is reported about a project and a project it references that set the same DDD_Module, in the one that references the other, in three cases. Only the referenced project declares the module: this project is left out of it. Only this project declares it: the referenced project's ids, domain events and template classes are left out of what is written here. Neither declares it, and the referenced project declares classes with a package's templates that a registration this project can call takes, and cannot call that registration itself: the registration is written for those classes nowhere. The last case asks for the templates because several projects that set one DDD_Module and declare no module are also how an application without modules names its generated code; a registration written nowhere is what makes it a mistake. The name the two projects share is what makes each case certain, so nothing is reported about projects that merely belong together. Declare the module, with DDD_DeclareModule set to true where DDD_Module is set, which a Directory.Build.props can do for a whole folder, or with [assembly: Module]. A project that is meant to be no module, while it carries the module's name, says so with DDD_DeclareModule set to false, and a test project is never reported. Reported at the project file, since no line of code is wrong; its severity is therefore set with NoWarn, WarningsAsErrors or a global analyzer config (is_global = true), which reach a diagnostic outside the source, and not in an .editorconfig section for *.cs files, which does not.");

    public static readonly DiagnosticDescriptor GraphQLSchemaClassMisdeclared = Create(
        id: "DDD00062",
        title: "A class of one GraphQL schema is one the toolkit alone registers",
        messageFormat: "'{0}' is marked [GraphQLSchema], and {1}",
        category: GraphQL,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A class marked [GraphQLSchema(\"admin\", OperationType.Query)] belongs to the schema of that name and to no other: the toolkit's generator registers its public static methods as fields of that operation type in the module's Add{Module}GraphQlRuntimeBindings(), for a builder of that name only. HotChocolate's own generator registers everything it finds in a project in one method, which goes into every schema the project is added to. A class it finds would therefore be in every schema after all, an administration field in the schema every user is offered, so the class carries nothing that generator registers: no [QueryType], [MutationType], [SubscriptionType], [ExtendObjectType] or [ObjectType], no base class such as ObjectTypeExtension, and no static method marked [Query], [Mutation] or [Subscription]; the attribute on the class says what its methods are. The attribute also names a schema, and the class is one generated code can name: not generic, not a file class, and not private or protected inside another class. Each public method is a field, so it is static: nothing makes an instance of the class, and an instance method, such as a former [QueryType] class has, would be dropped. A class with no field at all is reported too, and so are two methods that would be one field of one schema, two overloads or a GetLedger beside a GetLedgerAsync: HotChocolate keeps one of them and drops the other. A method is found by its name, and only a name two public static methods share by the types of its parameters as well, so such a method takes no parameter of a type another generator writes, such as a data loader's interface, which this generator does not see. It is an error, because each of these would put a field where it was meant not to be, or leave it out where it was meant to be, without another word.");
    public static readonly DiagnosticDescriptor TemplateFacadeNotWritten = Create(
        id: "DDD00065",
        title: "The class a package's use cases are named through is written where each of its templates has one class",
        messageFormat: "'{0}' is not written, so no project can name the types nested in {1} through it: {2}",
        category: Entities,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A package that nests its use cases in one class generic over the application's classes, as Tenancy does with TenancyUseCases<...>, asks with [assembly: TemplateFacade] for a class of the application's own that derives from it closed over them, named after the module: ShopTenancy for [assembly: Module(\"Shop\")]. The toolkit's generator writes it into the project that declares the classes, and every project that references that one names the use cases through it. When it cannot be written, the projects above only hear that the name does not exist, CS0246, so this says why where the classes are declared: a template that no class of this project or of the projects of its module it references is declared with, several classes of one template, a class that does not meet what the package's class asks of it, or a type of that name this project declares in a namespace, which a class of the name in the global namespace would hide wherever that namespace is imported. It is information rather than a warning: a module whose classes are split over two projects is told it in the first of them, and gets the class in the second, where they are complete. A template whose class a parent of this project needs is reported as an error by that class, DDD00044 or DDD00045, and one a registration this project can call takes from by that registration, DDD00049 or DDD00045, and neither again here; a class that cannot be generated has its own diagnostic too. A type, a namespace or an alias of the name the application keeps in the global namespace is its own way of naming the classes, and is left alone without a word.");
}
