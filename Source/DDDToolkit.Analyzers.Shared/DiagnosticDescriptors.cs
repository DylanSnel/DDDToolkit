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
    private const string EntityFramework = "DDDToolkit.EntityFramework";

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
        description: "A source generator only sees an MSBuild property the project lists as a CompilerVisibleProperty. The DDDToolkit.Analyzers package lists them in its build and buildTransitive props, for every project its generators arrive in. A property that is listed and not set reaches the generator as an empty value; here it did not reach it at all, so that file was not imported: the reference excludes the package's build assets, or the generator was added as a bare analyzer assembly. Whatever the project sets is then ignored, and {Module}EventNames, Add{Module}Converters, Add{Module}IntegrationEvents and Add{Module}GraphQlRuntimeBindings are named after the assembly. The module DDD_Module declares is declared by the build step that arrives with the same build assets, so a CompilerVisibleProperty added by hand brings the name back and not the module, which DDD00064 then says. Not reported for an assembly that declares [assembly: Module]: the module names all four, and the property is not read.");

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
        description: "A module is worth having only if the code outside it is held to its front door. The published contract of a module is every type it marks with [ModuleContract], plus every type it marks with [IntegrationEvent], plus every public type of a project that says it is the module's contracts, with [assembly: ModuleContracts] or DDD_ModuleContracts set to true, and never because of its name; everything else is an implementation detail that the owning team is free to change. This rule reports where one module names another module's implementation detail. It is silent unless both assemblies declare a module, with DDD_Module or [assembly: Module], so a framework assembly, a NuGet package or a shared kernel is never in the way; a shared kernel that sets DDD_Module to name its generated code sets DDD_DeclareModule to false beside it to stay no module.");

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
        title: "A [SupabaseMigrations] context or factory must be one the build can make",
        messageFormat: "'{0}' is marked [SupabaseMigrations] but {1}, so its migrations are not exported",
        category: Supabase,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The build exports a marked context's migrations through the design-time factory beside it, and a marked factory's through that factory, which it creates from generated code in the project that turns the export on. For a marked context the build writes that factory itself, in the context's project, unless the project has one of its own: that needs a context that is neither abstract nor generic, with a constructor that takes its options alone, DbContextOptions<TContext>, and sets its required members, in a project that references Npgsql.EntityFrameworkCore.PostgreSQL. Where it cannot write one, write a factory of your own beside the context, which the build then uses. The factory, the build's or your own, is in the context's assembly, where dotnet ef looks for it, or in the exporting application itself, where dotnet ef looks first, and only one is in either: with more than one, mark the one the export makes the context with. A factory needs a public, non-abstract, non-generic class with a public parameterless constructor that implements IDesignTimeDbContextFactory<TContext>, and the project that exports has to see it and its context. A context or factory that is none of these is left out, and that is an error rather than a warning: a module whose migrations silently never reached Supabase would be found by a failing deployment instead of by the build.");

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
        description: "The generated Add{Module}IntegrationEvents() constructs every outbound class and every handler with new, taking each constructor parameter from the scope the message is delivered in. It needs one accessible constructor with the most parameters, parameters it can resolve (no ref, out or params), and parameter types this assembly can see. A class it cannot construct is left out of the registration, so its events are not published or its contract is not handled; register it by hand or give it a constructor the registration can call. The registration also names the domain events of the module's projects that do not reference Entity Framework, such as its domain project, and a domain event of one of those that this project cannot see is reported on its [assembly: Module] attribute, or at its project file where DDD_Module declares the module: make the event public.");

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
        description: "The generator translates a rule's Allows method into SQL and writes the result into another part of the class. So the class is static and partial, and Allows is a static method that takes the aggregate the rule is about and an ICurrentUser, returns bool, and has a single expression for a body, either after => or as its only return statement. An [AccessFunction] has the same shape, and may take strings, numbers, flags, Guids and ids after the user, which become the SQL function's parameters. A contract declares nothing, or one static partial Allows that takes the key first, or one static partial Ids that returns AccessSet of the key. A resource access contract's key is the id of the resource's aggregate, declared with [EntityId<T>] or written by the toolkit beside an aggregate root of the same project declared with a value; the contract is declared in the module that owns the id, since a module publishes the sets of its own resources and other modules ask its contract; and it declares nothing, or the Ids its set is asked with: no parameters for the resources seen, the key as a string for those it is held on. A question of an [AccessFunctions] class is a static partial method: an [AccessSet] one returns AccessSet<T>, an [AccessScalar] one returns a value, and their parameters are strings, numbers, flags, Guids, ids or a type parameter constrained to IEntityId. A function's name is schema.name, owner/name, or a name relative to its owner, each part letters, digits and underscores. A set-shaped function answers with the keys of the rows it allows, so its aggregate's key is one column. A column rule, a rule with Columns, is for RowOperations.Change alone, and names properties of its aggregate, or of a value object it holds written with a dot as \"Value.Property\", and no collection of its entities; it is asked in a trigger whose search path is empty, so a function it calls with Sql.Call names its schema. Nothing is generated until the class has its shape, and a rule without SQL is never written into the database.");

    public static readonly DiagnosticDescriptor RowAccessRuleUntranslatable = Create(
        id: "DDD00039",
        title: "A row access rule can only say what the database can check",
        messageFormat: "'{0}' cannot be part of a row access rule: a rule compares, and-s, or-s and negates properties of the aggregate, constants, the current user's Id, IsAuthenticated, Role and FindClaim(\"...\"), the time as DateTimeOffset.UtcNow or DateTime.UtcNow, SQL written with Sql.Call or Sql.Raw, the Allows or Ids of an [AccessFunction] or its contract, the Ids of a [ResourceAccessContract], and the questions of an [AccessFunctions] class",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every row access rule becomes a condition the database evaluates for each row, so it can only use what the database knows: the aggregate's own columns, constants written in the rule, and the caller's claims. A method call, a local variable or a field of another object has no column and no claim to become, and a rule that quietly left it out would let the database answer differently from the C# method. The clock is the database's now(), and a question only the database can answer is asked through an [AccessFunction] or an [AccessFunctions] class, whose SQL function the export writes or knows, or through a [ResourceAccessContract], whose set the function a contribution says answers it for the resource answers. A set-shaped question is asked with Contains, and nothing else. The error is on the part that cannot be translated.");

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
        description: "A package's [TemplateRegistration] method, such as a modelBuilder.AddTenancy(), is written into every project that declares a class with one of its templates, closed over that project's classes: each of its [TemplateType] type parameters takes the class declared with a template, or that class's id. The project declares a class with one of the method's templates, so it is meant to get the registration, and one of the other templates has no class, in this project or in the projects it references. It is reported once for the template, naming every registration that needs it, on the first class of the project declared with one of the methods' templates, and no registration is written until the class this message names is declared. Where a class of the project derives from a parent that takes a type from that template, DDD00044 says it on that class, with the same fix, and this one is not reported. A project of a module that declares none of the classes itself, such as a module's infrastructure project next to its domain project, gets the registration built from the classes the module's other projects declare, and only those: then one of the method's templates has a class there and another has none, and it is reported on the project's [assembly: Module] attribute, or at its project file where DDD_Module declares the module. The class belongs in the project that declares the others.");

    public static readonly DiagnosticDescriptor TemplateRegistrationMissesConstraint = Create(
        id: "DDD00050",
        title: "A type a template registration takes meets the method's constraints",
        messageFormat: "'{1}' takes '{0}' as '{2}', which requires {3}; '{0}' does not meet it",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A [TemplateType] with Take = Type hands one of the application's own classes to a package's [TemplateRegistration] method as a type argument. The method may ask more of that class than being declared with the template, such as an interface. A class that does not have it would make the registration written for the project a compile error inside generated code, where there is nothing to fix. A [TemplateType] that takes an id is held to the same rule as far as a struct or a class goes: an id declared as a record class where the method asks for a struct is reported too. So is an id without a public static Create() where the method asks for ICreatableEntityId<T>, unless the template of its class says its package makes the ids, CreatesIds = true: then DDD00067 says it on that class, and the registration stands back without a second word. A later type argument of the template, taken with Argument = n, is whatever type the application wrote there, and is judged the way DDD00053 judges it against the parent: a struct or a class always, and the method's other constraints when the type is one no generator will still complete. A later type argument whose id the method takes, with IdOfArgument = true, is a class declared an entity or an aggregate root, since that is where its id is read from; a later type argument that names an entity or an aggregate root of this project is judged by what the generator will make of that class, as a class the method takes is, so a child entity where the method asks for an aggregate root is reported rather than left to fail inside the registration; and a type the registration is named after, with [TemplateRegistration(Name = ...)], has a name of its own, which an array has not. No registration is written until the type this message names meets the requirement.");

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
        description: "A function name without a schema is relative to the module that owns it: is_member in the Projects module is projects/is_member, and the export writes it as the function of that name in the schema of the context that defines it, whatever the host calls that schema. The owner is the name of the module the declaring assembly declares, with DDD_Module or [assembly: Module], or the Owner of the class's [AccessFunctions], which a package that declares no module uses. With neither there is no owner to make the name relative to, and a function of the same name in another module could not be told from it. Nothing is generated for the class until it has an owner or the name has its schema.");

    public static readonly DiagnosticDescriptor TemplateArgumentFailsConstraint = Create(
        id: "DDD00053",
        title: "A type argument of a template meets its parent's constraints",
        messageFormat: "'{0}' is declared with [{1}], but '{2}' does not meet the parent's constraint on '{3}', which requires {4}",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A class declared with a template derives from the package's parent, closed over the type arguments the template supplies: the attribute's own, the id first, and the ids a [TemplateArgument] takes from other classes. The parent may ask more of an id than being an entity id, most often a struct, because it holds ids by value. An id declared as a record class would make the parent closed over it a compile error inside generated code, where there is nothing to fix. Whether the type is a struct or a class is always judged. The parent's other constraints, an interface for instance, are judged of a type from a referenced project and of one written out in full; a partial type declared in this project may still be completed by a generator, as an [EntityId<T>] is, so what it does not show yet is left to the compiler. An [EntityId<T>] with an error of its own is not completed and is judged as it stands. Nothing is generated for the class until the type this message names meets the requirement: declare the id as a readonly partial record struct, or give the type what the message names.");

    public static readonly DiagnosticDescriptor RowAccessContributionNotMarked = Create(
        id: "DDD00054",
        title: "A package's row access contribution is made from what the application marks",
        messageFormat: "'{0}' writes the row access contribution '{1}' into this application's migrations, made from {2}, and this application marks none: nothing of it is written. Mark it, or write none of it with [assembly: LeaveOutRowAccessContribution(typeof({3}))].",
        category: Supabase,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A package that declares itself a contributor with [assembly: RowAccessContribution] writes its row level security, SQL functions, policies and statements, into the migrations of every application that references it: referencing it is the application's consent, since a package such as Tenancy on Postgres only works with its SQL in the database. The build makes the package's class in the project that runs the Supabase export, before any host exists, so what its SQL depends on, a resource's rules say, is a static property or field the application marks with an attribute the package names ([FromApplication] on the class's constructor). Where the application marks nothing the class cannot do without, the class is not made, and none of its SQL reaches the migrations: the package's tables may have no policies at all, and its functions are missing for every rule that asks them. Mark the member, with the attribute the message names; or, if the application must not have the package's SQL, say so with [assembly: LeaveOutRowAccessContribution(typeof(X))], which this warning then no longer reports.");

    public static readonly DiagnosticDescriptor SupabaseMigrationsWithoutModule = Create(
        id: "DDD00055",
        title: "A context's migration files are named after its module",
        messageFormat: "'{0}' is in an assembly that declares no module, so its Supabase migration files are named after the context, '{1}'. Declare the module, with DDD_Module or [assembly: Module], and the file names stay the same when the context is renamed.",
        category: Supabase,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The Supabase export names every file it writes after the module the context belongs to, as in 20260922120000_AddOrders.ordering.ddd.sql, and finds a module's files again by that name. The module is the one the assembly of the factory or of the context declares, with DDD_Module or [assembly: Module]. With neither, the name is taken from the context's class instead, so renaming the class changes the name every file is expected under: the export then recognizes none of the files it wrote, reports every migration as VersionTaken, because the file under the old name holds its timestamp, and writes the module's access file once more. Declare the module in the project that holds the context, with <DDD_Module> in its project file or [assembly: Module(\"...\")].");

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
        messageFormat: "No access behavior is written for '{0}': the Mediator library this project references declares {2} otherwise than the generator knows it, {1}. Write the behavior yourself: its Handle is async, awaits AccessChecks<{0}>.RequireAsync(message, cancellationToken), and then awaits the next step inside using (checks.BeginWork(message, isQuery)).",
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
        messageFormat: "'{0}' has no member list, and the toolkit cannot write one over its '{1}': {2}. Put that right, or write the list yourself: private MemberList<{3}> Members => new(members, owner, codes);.",
        category: Membership,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "For a class declared with the Membership package's member template, [Member<TId, TMemberId, TRoleId, TResource>], the toolkit writes onto the resource the template names what makes it a resource with members: its name, the codes it refuses under, as a static property Codes, and the scope of its module's system work, which its rules read through its type, MembershipRules.For<TResource>; and the member list, a private property Members, of MemberList<TMember, TId, TMemberId, TRoleId>, which the resource's own methods change its members through. The list is written from what the resource declares, and only when each can be told without a guess. The members: exactly one get-only partial property of IReadOnlyList<TMember>, IReadOnlyCollection<TMember> or IEnumerable<TMember>, which the toolkit backs with a list. The owner: the one property marked [Owner], of TMemberId, since the owner is a member. New rows: TId has a public static Create(), which makes a new row's id: the generator writes one for an [EntityId<Guid>], and an id over anything else declares it. The codes: the ones the toolkit writes, unless the resource declares one static property or field of MembershipCodes of its own, the form for codes a resource words itself; two of them, or a member called Codes that is not one, cannot be told. And the resource has no member called Members of its own, and the member class's types are ones the generator can see: the id of an [AggregateRoot<Guid>] is written by another generator, which no generator sees, so an id a member is known by is declared with [EntityId<Guid>]. A resource that declares a MemberList itself, under whatever name, is left alone and hears nothing: that is the form for every other shape. This warning is for a resource that has no member list at all: its members could not be changed, so say what is missing, or write the property by hand. A member class that names no aggregate root, or names one whose members are another class, is DDD00060 on the member class instead.");

    public static readonly DiagnosticDescriptor ModuleNotDeclaredByTheBuild = Create(
        id: "DDD00064",
        title: "DDD_Module declares the module where the package's build step runs",
        messageFormat: "DDD_Module names this project's generated code after '{0}' and declares no module, because the build step of the DDDToolkit.Analyzers package that declares it did not run: the project is no project of module '{0}'. Import the package's targets file beside its props file, or declare [assembly: Module(\"{0}\")].",
        category: Modules,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "DDD_Module declares the project's module through a build step: the targets file the DDDToolkit.Analyzers package carries beside its props file writes the declaration into the project, where every generator of it, and of the projects that reference it, reads it. A package reference brings both files. Where the generators arrive another way, as an analyzer assembly or a project reference with OutputItemType=\"Analyzer\", and the property reaches them through the props file alone or a CompilerVisibleProperty the project adds itself, as DDD00014 suggests, the name arrives and the declaration does not. The generated code is then named after the module and the project is no module: its domain events are stored and published without the module's name, the boundary is not checked, and the module's other projects do not take it for one of theirs, so a registration such as AddTenancy() is not written into a project of the module that declares none of the classes. Without this warning the first sign would be a call that does not compile, or an event stored under another name. Import the targets file as well, or declare [assembly: Module]. The targets hand the generators DDD_DeclareModule, which is how they tell the step ran: where it ran, a test project and a project that sets DDD_DeclareModule to false are no module on purpose, and are not reported. A test project is not reported without the targets either, where the props file says it is one. Reported at the project file, since no line of code is wrong; its severity is therefore set with NoWarn, WarningsAsErrors or a global analyzer config (is_global = true), which reach a diagnostic outside the source, and not in an .editorconfig section for *.cs files, which does not.");

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
        messageFormat: "'{0}' is not written, so no project can name {1} through it: {2}",
        category: Entities,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A package that ships a class generic over the application's classes, as Tenancy does with SeatManager<...>, asks with [assembly: TemplateFacade] for a class of the application's own that derives from it closed over them, named as the package's class is without its type parameters: SeatManager. The toolkit's generator writes it into the project that declares the classes, and every project that references that one names the class through it. When it cannot be written, the projects above only hear that the name does not exist, CS0246, so this says why where the classes are declared: a template that no class of this project or of the projects of its module it references is declared with, several classes of one template, a class that does not meet what the package's class asks of it, or a type of that name this project declares in a namespace, which a class of the name in the global namespace would hide wherever that namespace is imported. It is information rather than a warning: a module whose classes are split over two projects is told it in the first of them, and gets the class in the second, where they are complete. A template whose class a parent of this project needs is reported as an error by that class, DDD00044 or DDD00045, and one a registration this project can call takes from by that registration, DDD00049 or DDD00045, and neither again here; a class that cannot be generated has its own diagnostic too. A type, a namespace or an alias of the package's name the application keeps in the global namespace is its own way of naming the classes, and is left alone without a word; a name the application gives that a type or a namespace there has is DDD00076. A package's interface, such as Tenancy's ITenancyStore<...>, gets a global alias instead of a class, TenancyStore, in the project that declares the classes and in every project above that sees them; the same causes keep it out, and a type of its name a project above declares in a namespace keeps it out of that project, said on that type.");

    public static readonly DiagnosticDescriptor TemplateDefaultNotWritten = Create(
        id: "DDD00066",
        title: "A package's switch writes a class or an id where its name is free and its id is known",
        messageFormat: "[assembly: {0}] does not write '{1}': {2}",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A package's switch, such as [assembly: GenerateTenancyClasses], has the generator write the package's classes a project leaves out, and their ids, as the package ships them: a class called after its template, Tenant for [TenantAggregate], and an id called after the class, TenantId, both public, in the project's root namespace. It writes nothing that would take the place of something the project has already. A class is not written where something of its full name is there: a type that is not declared with the template, the project's or a referenced project's, or a namespace, such as a folder of that name directly under the project; the two would be one name, and the compiler's error would be in code nobody wrote. An id is taken rather than written when a type of its name can be found, in the project or in a project of its module that it references; when several can be found, the one found is no entity id, or the generator writes an id of that name for an [AggregateRoot<Guid>] or [Entity<Guid>] class of the project, there is no telling which one a class should be declared with, and nothing that takes it is written. A class that takes a type from one that is not written, or shares its id, is not written either, no id is written for a class that is not, and the registrations and the class the package's use cases are named through stand back for it rather than report it missing (DDD00049, DDD00065): the cause is said once, here. A class of the project's own in the way that is meant to be the package's class becomes it with the template, [RoleAggregate<RoleId>]; otherwise declare the package's class yourself under a name of your own, or rename what is in the way, or mark the id [EntityId<T>]. A class or an id the project declares itself always wins, and is never reported.");

    public static readonly DiagnosticDescriptor TemplateIdWithoutCreate = Create(
        id: "DDD00067",
        title: "A class whose package makes its new ids is declared over an id with a Create()",
        messageFormat: "'{0}' {3}, and '{1}' is declared with [{2}], whose package makes each new {0} with {0}.Create(), in code and before the save. {4}.",
        category: Entities,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ids are made in code before the save, never by the database, so a change and every event of it know the id from the start. A package that makes the ids of new classes of its template itself, as Tenancy makes a new tenant's, seat's, unit's, role's and invitation's, says so on the template, [AggregateRootTemplate(..., CreatesIds = true)], and makes them with TId.Create(), the static member of ICreatableEntityId<TId>. The generator writes Create() for every [EntityId<Guid>], making a time-ordered id as CreateSequential() does, and implements the interface with it; an id over a long, an int, a string or anything else has none until its partial declaration says how a new one is made: public static TenantId Create() => new(...), with a snowflake or a number of a HiLo block for a long, and the generator then implements the interface with that one. A Create() of your own wins over the generator's for a Guid too. An id written by hand, without [EntityId<T>], is not completed by the generator, so it declares both, the Create() and ICreatableEntityId<TId>; and an id of a project that does not target net10.0 has a Create() but not the interface, which only the net10.0 build of DDDToolkit.Abstractions has. The message says which of these it is. It is reported on the class declared with the template, or, for a class a package's switch writes, on the id when the id is declared in the same project, and on the switch when it is another project's; the code fix adds what is missing to the id's declaration, a Create() that throws until you write the body, the interface, or both. The package's registrations and the class its use cases are named through, which ask for the same with a constraint, stand back for the class rather than fail inside generated code. Other ids, which no package makes, need no Create().");

    public static readonly DiagnosticDescriptor ModuleContractsNotWritten = Create(
        id: "DDD00068",
        title: "DDD_ModuleContracts makes a project its module's contracts where the project can name the attribute",
        messageFormat: "DDD_ModuleContracts is true, and the project publishes nothing by it: the toolkit's generator writes [assembly: ModuleContracts] from it, and no DDDToolkit.Abstractions the project references declares that attribute. Reference the DDDToolkit.Abstractions of the same version as DDDToolkit.Analyzers, or mark each type the other modules may name [ModuleContract].",
        category: Modules,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "DDD_ModuleContracts set to true makes a project its module's contracts: the toolkit's generator writes [assembly: ModuleContracts] into it, and every public type of it is published to the other modules. The attribute is declared in DDDToolkit.Abstractions and the generator ships in DDDToolkit.Analyzers, two packages a project may reference separately, as a contracts project that references nothing else does. Where the project references a DDDToolkit.Abstractions older than the attribute, or none, there is nothing to write, and the project's explicit choice would be dropped without a word: the first sign would be DDD00022 in every module that names one of its types, telling that module to have the type marked [ModuleContract], which points away from the cause. Reference the DDDToolkit.Abstractions of the same version as the analyzers, which declares the attribute, or mark each type the other modules may name [ModuleContract]. A project whose property is anything but true is not reported. Reported at the project file, since no line of code is wrong; its severity is therefore set with NoWarn, WarningsAsErrors or a global analyzer config (is_global = true), which reach a diagnostic outside the source, and not in an .editorconfig section for *.cs files, which does not.");

    public static readonly DiagnosticDescriptor RowAccessContributionOfAModuleNotListed = Create(
        id: "DDD00069",
        title: "A module's row access contribution is listed by the project that runs the export",
        messageFormat: "'{0}', a module, offers the row access contribution '{1}', which this project does not list, so its SQL is not written into the migrations. {2}.",
        category: Supabase,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A module of the application that writes row level security of its own, a trigger or a policy as SQL, declares the class with [assembly: RowAccessContribution], as a package does. An assembly that declares a module, with DDD_Module or [assembly: Module], offers it rather than writing it by itself: it is the application's own SQL, so the project that runs the Supabase export says what its database gets, with [assembly: UseRowAccessContribution(typeof(X))], and the export makes what it lists with new X(). The declaration is what makes a forgotten line visible. Without it nothing would say that a module's trigger or policy never reached the migrations, and what it holds the rows to would hold nowhere. List the class, a class of the application's derived from it, or a generic one closed with the application's types; the message says which the offer needs, since the export makes it with a constructor that takes nothing. A module whose SQL this application's database must not have is left out by not listing it, with this warning suppressed for the project, <NoWarn>, and the reason beside it. A package, an assembly that declares no module, writes its SQL by itself and needs no line.");

    public static readonly DiagnosticDescriptor ApplicationMarkNotPublic = Create(
        id: "DDD00070",
        title: "A member a library marks for a package's row access contribution is public",
        messageFormat: "'{0}' is marked {1} and is not public, in public types, with a public getter: the project that runs the Supabase export references this library and sees nothing less of it, so it would make the package's contribution as if nothing were marked. Make it public.",
        category: Supabase,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A package's row access contribution is made in the project that runs the Supabase export, from the static members the application marks with the attributes the package names, [TenancyCatalogue], [TenancyOperators] or [MembershipRules<TMember>] among them; each such attribute says so with [ApplicationMark]. That project finds the marks in the projects it references, and of a library it sees what is public and nothing else: a member that is internal, or declared in a type that is, is not there for it at all. The export would then write Tenancy's policies from the default catalogue rather than the application's, or leave a resource's functions out, and nothing in that project could say why, since it never saw the mark. So the library that declares the member reports it, where it is declared: make it public, in public types, with a public getter. An application, the program that runs the export itself or the host, may keep its own marks internal, since no project references it for them.");

    public static readonly DiagnosticDescriptor RowAccessContributionCannotBeMade = Create(
        id: "DDD00071",
        title: "What a package's row access contribution is made from is found once, and is what it takes",
        messageFormat: "The row access contribution '{0}' of '{1}' cannot be made: {2}",
        category: Supabase,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The build makes the row access contribution of every package the project that runs the Supabase export references, from the static properties and fields the application marks with the attributes the package names: [TenancyCatalogue] for Tenancy's catalogue, [MembershipRules<TMember>] for a resource's rules. A marked member is read by the code the build writes into that project, so it is static, has a getter, is declared in a type that is not generic and that code can name, and that project can read it; and it is of a type the package's class takes, an ApplicationCatalogue for [TenancyCatalogue] and not the catalogue built from it. A member another project declares that is not public is one the project that runs the export does not see at all, so it cannot be reported here: the build of the project that declares it reports it, DDD00070. What the class takes once is marked once in all the projects the export sees: of two, the build cannot tell which the application runs with, and it writes neither rather than guess, since policies written from another catalogue or other rules than the host's would grant other access than the application does. A class the package declares in a way the build cannot make, one whose constructor takes something it does not say where to find, or a generic one nothing the application marks closes, is reported too: that is the package's to fix. It is an error because the contribution is not made, and its SQL would otherwise be missing from the migrations, or written from what the application does not run with.");

    public static readonly DiagnosticDescriptor RowAccessContributionWrittenTwice = Create(
        id: "DDD00072",
        title: "A row access contribution a package writes is not listed again",
        messageFormat: "{0}, which '{1}' writes into this application's migrations already, because this project references it: written again, it would be written twice. {2}.",
        category: Supabase,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A package that declares itself a contributor with [assembly: RowAccessContribution] writes its row level security into the migrations of every application that references it, from what the application marks, without a line or a class of the application's. Listing it with [assembly: UseRowAccessContribution], or a class derived from it, or its closing over a type the package already closes it with, would hand the export the same SQL twice: two functions of one name, or two contributions that keep the same tables to themselves, which the export refuses when it runs. This is what an application that wrote a class to hand the package its catalogue or its rules, as the toolkit once asked, still has: take the line out, and the class with it, and mark what it handed over instead. An application that writes the package's SQL with a class of its own on purpose leaves the package's out with [assembly: LeaveOutRowAccessContribution(typeof(X))] and keeps listing its own. A contribution another referenced assembly declares, derived from a package's, is the same SQL a second time as well, and is reported too.");

    public static readonly DiagnosticDescriptor LeaveOutRowAccessContributionLeavesNothingOut = Create(
        id: "DDD00073",
        title: "What [assembly: LeaveOutRowAccessContribution] names is a contribution a package writes, and a context",
        messageFormat: "{0} leaves nothing out: {1}",
        category: Supabase,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "[assembly: LeaveOutRowAccessContribution(typeof(X))] keeps the SQL of a package's row access contribution out of the migrations of the project that runs the Supabase export, and with Context = typeof(C) out of the access file of that one context. It names the contribution as the package declares it, open where it is generic, or one closing of it the application marks; and the context is a class derived from Entity Framework's DbContext, closed where it is generic, since a context's access file is what the SQL is kept out of. A line that names anything else leaves nothing out, while it reads as if it did: a type that is no context, a context left open, a class no package this project references declares a contributor (a module's own contribution, which only a line of [assembly: UseRowAccessContribution] writes, or a class of the application's), or a closing nothing the application marks makes. So it is reported, at the line, and nothing is left out for it. Name what the package declares, or take the line out; for a module's own SQL, take its [assembly: UseRowAccessContribution] line out instead.");

    public static readonly DiagnosticDescriptor DesignTimeFactoryWithoutTheToolkit = Create(
        id: "DDD00074",
        title: "A design-time factory keeps the migration history where the application does",
        messageFormat: "'{0}' makes its '{1}' without UseDDDToolkitDesignTime(), so dotnet ef and the Supabase export record the migrations in the provider's default schema, while a host that wires the context with UseDDDToolkit reads them in the context's own. Add .UseDDDToolkitDesignTime() to its options, after the provider.",
        category: EntityFramework,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "UseDDDToolkit and UseDDDToolkitCore keep a context's migration history in the context's default schema, beside its tables: ordering.\"__EFMigrationsHistory\" for a model with HasDefaultSchema(\"ordering\"). dotnet ef and the Supabase export make the context through its IDesignTimeDbContextFactory instead, before any host exists, so the factory says the same with UseDDDToolkitDesignTime(), which adds what UseDDDToolkit adds that needs no service. Without it, dotnet ef database update, a migrations script, a migration bundle and the exported files record every migration in the provider's default schema, public or dbo, while the running application reads the history in the context's schema: Migrate() tries to apply the first migration again and fails on a table that is there, and on Supabase the start-up check reports every migration missing. Reported at CreateDbContext of a factory, in a project that references DDDToolkit.EntityFramework, whose class calls none of UseDDDToolkitDesignTime, UseDDDToolkit, UseDDDToolkitCore and MigrationsHistoryTable, in CreateDbContext or in a method, property or field of the class it reads. Options made by a helper of another class are not looked into: the factory adds the call after the helper, which adds nothing the options have already. A context the host does not wire with the toolkit, and that keeps its history where Entity Framework keeps it, names the table in the factory's options and the host's alike, MigrationsHistoryTable(HistoryRepository.DefaultTableName), or suppresses the warning. A code fix adds .UseDDDToolkitDesignTime() in front of the options' .Options.");

    public static readonly DiagnosticDescriptor TemplateFacadeNameMet = Create(
        id: "DDD00075",
        title: "Every class a package's use cases are named through has a name of its own where a project sees it",
        messageFormat: "'{0}' is the name of more than one {4} in this project: {1}. Give {2} a name of its own, with {3}.",
        category: Entities,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A package that ships a class generic over the application's classes, as Tenancy does with SeatManager<...>, asks with [assembly: TemplateFacade] for a class of the application's own that derives from it closed over them, and the toolkit's generator writes it into the project that declares the classes, in the global namespace, under the package's class's name without its type parameters: SeatManager. Two modules of one application that each declare the package's classes get one such class each, of the same name, and the two meet in every project that sees both, the host and the tests first, where naming either would be the compiler's error CS0433. A module whose projects see no other module's class is not told. A project that sees two such classes the projects it references have says so at its project file, since no line of its code is wrong. A project that declares the classes itself, and references another module's project that has a class of its name, gets its own all the same and says so on its first class: C# binds the name in its own code to its own class, CS0436, so that code is closed over its own module's classes, and every project above it sees both, is told there, and cannot name either. Two classes one project gets that come to one name, two packages' classes of one name or a name given that another class has, are both left out, and said on the first class as well. The message writes out the line that gives one of them a name of its own, [assembly: TemplateFacadeName(\"SeatManager\", \"CustomersSeatManager\")] (DDDToolkit.Abstractions.Attributes), and the project it goes in: the one that declares that module's classes; where the application named that class already, it names the line to give another name instead. Any name a class can have that no namespace or type the projects see has already will do; the projects above that module's name the use cases by it. A type, a namespace or an alias of the package's name the application keeps in the global namespace is its own way of naming the classes, and is not this. A package's interface gets a global alias instead of a class, such as TenancyStore, written into every project that sees the classes: a project that sees two modules' classes would get two aliases of one name, gets neither, and is told so at its project file, with the line that names one of them in that module's project.");

    public static readonly DiagnosticDescriptor TemplateFacadeNameNamesNothing = Create(
        id: "DDD00076",
        title: "[assembly: TemplateFacadeName] names, once, a class this project gets",
        messageFormat: "[assembly: TemplateFacadeName(\"{0}\", \"{1}\")] changes nothing: {2}",
        category: Entities,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "[assembly: TemplateFacadeName(\"SeatManager\", \"CustomersSeatManager\")] gives the class a package's use cases are named through, which the toolkit's generator writes into the project that declares the classes and calls after the package's class, a name of the application's own. It names the class by the package's class's name without its type parameters, or by that name with its namespace where two packages' classes of one name meet in the project. A module whose classes are split over two projects may give the line in either: the project that writes the class reads it in the projects of its module it takes classes from. A line that names a class no package the project references asks for, two such classes, a class a line before it names already, or that gives a name no class can have, a keyword or one with a space, changes nothing, while it reads as if it did; so does a line in a project that declares none of the classes, such as an application project above the domain project, since the class is written, and named, where they are declared. A line that gives a name a namespace or a type in the global namespace has already, the module's root namespace say, would leave the class out without a word, and the projects above with only CS0234; it is reported too, while an alias of that name the project writes itself stays its own. A line of a project the class's module takes classes from that gives another name than the one the class has is reported where the class is written. Each is reported at the line, with what the project can name. Correct the line, or move it into the project that declares the classes.");

    public static readonly DiagnosticDescriptor RequestStatesNoRequirement = Create(
        id: "DDD00077",
        title: "A request says what it requires of its caller",
        messageFormat: "'{0}' implements '{1}' and says nothing of what it requires of its caller: {2}. Put a requirement attribute on it, [SignedIn] say, or [RequiresPermission(permission)] with a marker of where the permission is held, or answer RequiredAccess where the requirement is computed; [AllowAnonymous] where anyone may send it.",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every command and query of a module says what it requires of its caller, who may send it, and the module's checks hold the caller to that before the handler runs. A request says it with an attribute, which the generator turns into RequiredAccess in its module's interface, [AccessRequests] and partial, or by answering RequiredAccess itself. Where the module's interface is partial the compiler no longer asks a request for RequiredAccess, since the interface implements it, so a request that says neither is reported here: it would be stopped every time it is sent, with an exception that says it declares nothing, and a forgotten requirement never opens a door. A request whose module's interface is declared in another project gets no line from this one either, and is reported the same way. An abstract request is not reported: what derives from it is. Nor is a request of a module whose interface carries a default, [AccessRequests, InTenant] say, which the developer wrote for that: the request takes the default, on a line of its own the generated switch shows.");

    public static readonly DiagnosticDescriptor RequestStatesRequirementTwice = Create(
        id: "DDD00078",
        title: "A request says what it requires in one way",
        messageFormat: "'{0}' carries {1} and answers RequiredAccess as well{2}, and the property wins over the attribute: keep one, the attribute where the requirement reads no value of the request, the property where it computes one",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A request says what it requires with an attribute, [SignedIn], [InTenant] or [RequiresPermission(permission)] with its marker, or by answering RequiredAccess itself, for a requirement computed from the request's values. Both on one request read as two requirements, while C# takes the request's own property and the attribute does nothing: a review that reads the attribute is misled. So it is an error. A request that derives from one that answers RequiredAccess answers it too, and carries no requirement attribute of its own; so does a request whose interface between it and the module's gives RequiredAccess a default of its own, which C# takes over the module's.");

    public static readonly DiagnosticDescriptor AnonymousBesideARequirement = Create(
        id: "DDD00079",
        title: "[AllowAnonymous] goes with no other requirement attribute",
        messageFormat: "'{0}' carries [AllowAnonymous] beside {1}: requirement attributes stacked on a request all hold, and beside what {1} requires, anyone may send it contradicts it; keep the one it requires",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Requirement attributes stacked on one request all hold, as several [Authorize] attributes do in ASP.NET Core: [RequiresPlan(Plan.Pro)] [RequiresPermission(BoardPermissions.Export)] is AccessRequirement.All of the two, in the order they are written. [AllowAnonymous] says that anyone may send the request, a caller who did not sign in too, so beside a requirement that says who may it contradicts it: read one way the door is open, read the other way it is closed, and a review cannot tell which was meant. So it is an error, whichever other requirement attribute it stands beside, [RequiresPermission] and [OwnOnly] included. A request anyone may send says [AllowAnonymous] alone.");

    public static readonly DiagnosticDescriptor RedundantRequirement = Create(
        id: "DDD00084",
        title: "A requirement attribute beside another adds what that one does not require already",
        messageFormat: "'{0}' carries {1} beside {2}, which requires it already: {1} adds nothing to what the request requires, so take it off",
        category: Access,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Requirement attributes stacked on one request all hold, so one that another beside it already requires says nothing more: [InTenant] beside a permission of Tenancy's, [RequiresPermission(permission)] with or without [AtUnit], which only a seat in the tenant or system work there holds, and [SignedIn] beside [Operator], which lets only a signed-in user with an operator's token role through. It does no harm to what is checked, but a reader takes it for a second condition and looks for what it adds. What a requirement already requires its package says, with Implies on [StatesRequirement], [PermissionMarker] or [PermissionWithoutMarker], so a module's own attribute is held to it the same way. The caller's own requires what its package says with Implies on [assembly: CallersOwn]: a Guid is the identity of a signed-in user, so [SignedIn] beside [OwnOnly] with [Own] on a Guid says nothing more, and Tenancy's seat id is a seat in the tenant, so [InTenant] beside [OwnOnly] with [Own] on it says nothing more either. A permission with [OrOwn] beside it lets the caller's own through as well, so only what both the permission and the caller's own require is counted for it. An attribute that the request it derives from carries already says nothing more either: the requirement attributes of a request and of the requests it derives from all hold. Nor do a request's own attributes that say what its module's default says, call for call: without them it takes the default, and requires the same. They are reported on a request nothing derives from, since one that derives from it holds to its attributes beside its own, and on the module's interface an attribute of its default that another there already requires is reported as on a request.");

    public static readonly DiagnosticDescriptor OwnMarkerOutOfPlace = Create(
        id: "DDD00085",
        title: "[OrOwn] goes beside [RequiresPermission], and [Own] with [OwnOnly]",
        messageFormat: "'{0}' {1}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A request about the caller's own says it one of two ways. [OrOwn] on the member that names the caller's own, beside [RequiresPermission(permission)], says the caller's own or the permission: AccessRequirement.Any(AccessRequirement.Own(request.Seat), the permission's case). [Own] on that member, with [OwnOnly] on the request, says the caller's own alone: AccessRequirement.Own(request.Seat). So [OrOwn] without [RequiresPermission] is the alternative of nothing, [Own] without [OwnOnly] is read by nothing, and [OwnOnly] without a member marked [Own] names no value; one member marked both ways, or two members marked one way, say two values where one is compared. [OrOwn] does not go beside a marker that says where a permission is checked without being what the request acts on, Tenancy's [AtUnit]: a caller who may act on its own would send a unit where it holds the permission for another subject, so a permission held at one unit would act on a seat anywhere; such a request computes its requirement as RequiredAccess. A package's marker says it may, with OwnAlternative on [PermissionMarker], which names the case the caller's own way through still meets where the marker says, as Membership's [OnResource] does: beside it, the caller sees the resource, MemberAccess.Sees. A marker goes on a positional parameter of a record, which is its property, or on a property the module's interface can read.");

    public static readonly DiagnosticDescriptor OwnOnATypeWithoutOwn = Create(
        id: "DDD00086",
        title: "[OrOwn] and [Own] mark a member of a type something says the caller's own of",
        messageFormat: "'{0}' marks '{1}' [{2}], {3}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The check compares the value a request names with the caller's own of the same type, at the door, so the request need not know who sends it. What the caller's own is, whoever knows says, with an ICallersOwn<TValue>: the core for a Guid, the caller's verified identity, which is the id its token names; Tenancy for the application's seat id, the caller's seat in the tenant of the request; and an application for a kind of its own, its own user id say, with a small implementation beside its requests, which the host registers with services.AddCallersOwn<UserId, CallersUser>(). On a member of a type nothing says that for, the request would be stopped every time it is sent, so it is reported where the project can tell: a package says its types with [assembly: CallersOwn], and an application's ICallersOwn is found in the requests' project or one it references. A member that may be null, SeatId?, names no value where it is left out: a requirement computed from whether it is given is written as RequiredAccess. And the caller's own is a value compared with the caller's, a struct that is IEquatable of itself, so a member that is a string, a class or an enum is reported whatever the project sees.");

    public static readonly DiagnosticDescriptor OwnerOutOfPlace = Create(
        id: "DDD00087",
        title: "[Owner] marks the one owner of an aggregate root",
        messageFormat: "'{0}' {1}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[Owner] marks the property of an aggregate root that names its owner, a fact about what the aggregate is: [Owner] public UserId Owner { get; private set; }. A request about the owner's own names the aggregate by its id, [Own] BoardId Board with [OwnOnly], or [OrOwn] BoardId Board beside [RequiresPermission(permission)], and the check reads that one column of the one row the id names, as the caller. So it goes on an instance property of the aggregate root itself: an entity inside an aggregate is no row a request names by its own id, and a type that is no aggregate root has none either. And it goes on one property: an aggregate has one owner, and several owners are members, a resource with members, which Membership keeps. An id the project sees as the id of two aggregate roots that each mark an owner cannot tell which one a request is about, and is reported at the request.");

    public static readonly DiagnosticDescriptor OwnerOfATypeWithoutOwn = Create(
        id: "DDD00088",
        title: "[Owner] marks a property of a type the caller has an own of",
        messageFormat: "'{0}' {1}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The check of a request about the owner's own compares the owner the row holds with the caller's own of the same type, so the property marked [Owner] is of a type something says the caller's own of: a Guid, the caller's verified identity; Tenancy's seat id, the caller's seat in the tenant of the request; or a kind of the application's own, with an ICallersOwn<TValue> the host registers with services.AddCallersOwn<UserId, CallersUser>(). It may be nullable, and a row whose owner is null is nobody's. The caller's own is a value compared with the caller's, a struct that is IEquatable of itself, so an owner that is a string, a class or an enum can never be compared, and is reported where the property is declared. Whether something says the caller's own of the owner's type is told where the requests are, since an application's ICallersOwn is declared beside them: there a request that names the aggregate is reported where the project can tell that nothing does, and the request would be stopped every time it is sent.");

    public static readonly DiagnosticDescriptor PermissionMarkerOutOfPlace = Create(
        id: "DDD00080",
        title: "A marker on a request says where the permission of its [RequiresPermission] is held",
        messageFormat: "'{0}' {1}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[RequiresPermission(permission)] says the permission a request's caller holds, and a marker on one of the request's members says where: none for the whole tenant (Tenancy's), [AtUnit] at that unit or above it (Tenancy's), [OnResource] on that resource through its members (Membership's), with [ExpectedVersion] beside it for the version the caller read. So a marker without [RequiresPermission] says where nothing is held; two markers of where, or [AtUnit] with [OnResource], say two places for one permission; [ExpectedVersion] without [OnResource] is the version of nothing; a marker twice of one kind gives the call two arguments for one. A marker goes on a positional parameter of a record, which is its property, or on a property: a parameter of a class's primary constructor is no member the requirement can read, and neither is a private or protected property, since the line is written in the module's interface. A permission without a marker is held where a package says, [assembly: PermissionWithoutMarker], which Tenancy does: a project that references no such package, or two, cannot tell, and asks for a marker.");

    public static readonly DiagnosticDescriptor PermissionMarkerOnWrongType = Create(
        id: "DDD00081",
        title: "A marker is on a member of the id it marks",
        messageFormat: "'{0}' marks '{1}' [{2}], {3}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A marker of where a permission is held reads the member it marks: [AtUnit] the application's unit id, the id its class declared with [OrganizationUnit<TUnitId>] has, and [OnResource] the id of a resource with members, an aggregate root a class declared with [Member<TId, TMemberId, TRoleId, TResource>] names. The call it gives takes any id, so a unit marked on a seat's id compiles, and lets nobody through: the package's check stops a case over an id it does not decide. So a member of another type is reported, where the project sees the classes the package's marker names; where it sees none, nothing can be told, and the check says it when the request is sent. A member that may be null, OrganizationUnitId?, is a requirement computed from whether it is given, which the request writes as RequiredAccess. [ExpectedVersion] marks a member of the type the call takes there, a long?.");

    public static readonly DiagnosticDescriptor QueryRequirementOnCommand = Create(
        id: "DDD00082",
        title: "A requirement for queries only is on a query",
        messageFormat: "'{0}' is {2} and carries [{1}], which its package declares for queries only: on anything but a query it holds the caller to nothing the request changes. Where it only reads, make it a query, IQuery or IStreamQuery; where it changes something, require of its caller what the change needs, [RequiresPermission(permission)] with a marker of where the permission is held, and carry out what no caller may do as system work behind that requirement.",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Some requirements are for queries only, and their packages say so ([StatesRequirement(..., QueriesOnly = true)]). Membership's [SeenWith<TResourceId>(permission)] refuses nobody for want of the permission: it lets a signed-in caller through, one that holds nothing too, and says what the query's own statement filters by, which is the check; on a command the handler would run unchecked. A message of the Mediator library that is no query and carries it is reported: a command, ICommand or IStreamCommand, and a plain IRequest or IStreamRequest, which may change as much as a command. A request of another dispatcher, which the generator cannot tell for a command or a query, is not; nor is the same requirement written as RequiredAccess, which a test holds.");

    public static readonly DiagnosticDescriptor RequirementAttributesNotRead = Create(
        id: "DDD00083",
        title: "A request's requirement attributes are read where its module's interface is partial",
        messageFormat: "'{0}' carries {1}, which nothing turns into what it requires: {2}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator turns a request's requirement attributes into RequiredAccess at compile time, one line per request, in the request's module's interface: the interface marked [AccessRequests] that the request implements, declared partial in the same project, so the generator can write the property into it. Without that, the attributes are read by nothing, and the request reads as if it said what it requires while it says nothing: an interface that is not partial, a request that implements no marked interface, one whose interface is declared in another project, one with type parameters, which a line cannot name, one the interface cannot see, nested private in another type, and one that implements two such interfaces, each of which implements the property, so C# takes neither (CS8705). So each is reported, with what to change: declare the interface partial, implement the module's interface, or answer RequiredAccess. The same holds for the attributes on an interface, the default of a module's requests: they are read on the module's interface, [AccessRequests] and partial, and on any other interface by nothing.");

    public static readonly DiagnosticDescriptor DefaultRequirementRefused = Create(
        id: "DDD00089",
        title: "A module's default requirement is one any of its requests may take",
        messageFormat: "'{0}' carries {1} as the default of its module's requests: {2}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A requirement attribute on a module's interface, [AccessRequests, InTenant] say, is the default of its requests: one that states nothing of its own, by attribute or by answering RequiredAccess, requires what the default says, on a line of its own in the generated switch. So every request that forgets its own requirement takes it, a command that forgets its permission included, and a default is a requirement any of them may take. [AllowAnonymous] is none: a request that forgot would let anyone in, and a forgotten requirement never opens a door. A requirement for queries only, Membership's [SeenWith<TResourceId>(permission)], is none either, since a command that says nothing would take it (DDD00082); [OwnOnly] reads a member of a request marked [Own], and a default reads none. And an attribute whose class's AttributeUsage leaves out interfaces is no default, which the compiler says as well (CS0592): a module's own requirement attribute is one where its class allows AttributeTargets.Interface. Each is reported at the attribute, and the interface's last arm stops a request that states nothing, as it does without a default.");

    public static readonly DiagnosticDescriptor MembershipNamesChanged = Create(
        id: "DDD00090",
        title: "The names of resources with members are those of the newest access file",
        messageFormat: "Membership names changed since the last access file. Gone: {0}. New: {1}. Codes clients read change with them, and the database gets new functions. A class you renamed keeps its old name with [Members(\"...\")].",
        category: Supabase,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A resource with members is named after its class, the plural of it, or what [Members(\"...\")] says, and the name is what its codes start with and what its four functions in the database are named after. Renaming the class renames both: clients that branch on boards.already-member read another code, and the next access file makes new functions beside the old ones. The Supabase export the build runs compares the names of the resources it writes with those the newest access file of each module recorded, with the class each came from, and says once, in that build, which are gone and which are new. It does not guess which became which. A class that was renamed on purpose and should keep its name says so with [Members(\"OldName\")]; one that should take the new name needs nothing more, and the warning is not said again once the new access file is there. Without the export, SupabaseMigrationsExport unset, nothing compares the names, and no warning is given.");

    public static readonly DiagnosticDescriptor MembershipNameUnreadable = Create(
        id: "DDD00091",
        title: "[Members] names a resource with what can be converted to a code and a function's name",
        messageFormat: "'{0}' is named '{1}' with [Members], which cannot be converted to a code and a function's name. Write it as a class's name, such as People or CraneBoards, or as it comes out, crane-boards: letters and digits, with - or _ between words, starting with a letter.",
        category: Membership,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A resource with members is named after its class unless [Members(\"...\")] says otherwise, and the name is written in two forms: in lower case with a dash between words for its codes, boards.already-member, and with an underscore for its functions in the database, boards_i_see. What [Members] says is converted the same way a class's name is, PascalCase split into words, and taken as it is, not made plural. A space, a symbol other than - and _, a digit or a separator first, or a separator last cannot be converted, and nothing of the resource is written: its rules, MembershipRules.For<TResource>, do not compile until the name is put right.");

    public static readonly DiagnosticDescriptor PermissionMisdeclared = Create(
        id: "DDD00092",
        title: "A permission is a public constant of a [Permissions] class, with a level",
        messageFormat: "'{0}' {1}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A module declares its permissions in a static class marked [Permissions]: every public const string of it is a permission, which a request names in [RequiresPermission] and a role gives, and each carries its level, the attribute that says where it is asked: Tenancy's [AtTenant] or [AtUnit], or Membership's [On<TResource>]. The packages' generators read the class and nothing else: Tenancy's puts every permission into its catalogue, Membership's every permission on a resource into the rules of that resource. So a class marked [Permissions] that is not static, a public const string without a level, a level on a field that is no public const string, or on one of a class that is not marked [Permissions], and a key declared twice in one class, would each be read as something it is not, or not at all, and are refused.");

    public static readonly DiagnosticDescriptor PermissionLevelMisfits = Create(
        id: "DDD00093",
        title: "A permission's level fits what it is asked on",
        messageFormat: "'{0}' {1}",
        category: Membership,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[On<TResource>] says a permission is asked on one resource with members, and how it is held there. TResource is the aggregate root a member class names, [Member<TId, TMemberId, TRoleId, TResource>]: on another class the permission would be asked on something that has no members, and its rules could not be made. Of the permissions on one resource, one at most is held by every member (EveryMember), since that is also the one that sees the resource from above; one at most locks the members (LocksMembers) and one at most locks the owner (LocksOwner), since each is the one key the database's lock asks for. A second is refused where it is declared, among the permissions the project sees.");

    public static readonly DiagnosticDescriptor RulesWithoutPermissions = Create(
        id: "DDD00094",
        title: "The rules of a resource are made from the permissions declared on it",
        messageFormat: "MembershipRules.For<{0}> finds no permission declared on '{0}' in this project or in the projects it references. Mark the permissions asked on it [On<{0}>] in a [Permissions] class.",
        category: Membership,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "MembershipRules.For<TResource> makes the rules of a resource from the permissions declared on it: the constants of a [Permissions] class marked [On<TResource>], in the project that makes the rules or in a project it references, which the package's generator hands over when the project is first used. Rules without a permission give the owner nothing and every role nothing, and the call would throw when the rules are made, so it is said where they are made.");

    public static readonly DiagnosticDescriptor HandWrittenTenancyPermissions = Create(
        id: "DDD00095",
        title: "A module's permissions are declared in a [Permissions] class, not on a list of its own",
        messageFormat: "'{0}' is a list of permissions marked [TenancyPermissions] by hand. Declare each permission as a constant of a static class marked [Permissions], with its level, [AtTenant], [AtUnit] or [On<TResource>]: the generator writes the module's list from it.",
        category: Tenancy,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A module declares its permissions once, as the constants of a static class marked [Permissions], each with its level and, in its summary or its Description, what it lets somebody do. Tenancy's generator writes the module's list for its catalogue from that class, under the module DDD_Module or [assembly: Module] names, and the project that composes the modules collects every such list. [TenancyPermissions] is the mark the generator puts on what it writes; a list a module marks with it by hand says the same keys a second time, apart from the constants a request requires, and is refused.");

    public static readonly DiagnosticDescriptor PlaceMisdeclared = Create(
        id: "DDD00097",
        title: "[At] marks the one place an aggregate root hangs at",
        messageFormat: "'{0}' {1}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[At] marks the property of an aggregate root that says where it hangs: the unit of an organization a board belongs to, the bay a crate stands in. A fact of the domain, which the rules of a resource with members read where they let it be reached from above: a key held at that place, or above it, reaches the aggregate. So it goes on one instance property of an aggregate root, the root itself and never an entity inside it, and that property is never empty: an aggregate that hangs nowhere could not be reached from above, which is what [At] says. A static property, one of another type, a second one and a nullable one are each refused.");

    public static readonly DiagnosticDescriptor ReachFromAboveWithoutPlace = Create(
        id: "DDD00098",
        title: "Rules that reach a resource from above read where it hangs from its [At] property",
        messageFormat: "MembershipRules.For<{0}> says reachFromAbove, and '{0}' marks no property [At]: reachFromAbove needs the place the resource hangs at: mark one property [At]",
        category: Membership,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "reachFromAbove in the rules of a resource with members says that a permission held where the resource hangs, or above it, reaches the resource. Where it hangs is a fact of the domain, said on the resource's class with [At] on the property that holds it, and read from there by the mapping and the functions in the database. Rules that reach from above a resource that marks no such property would be refused when the resource is first asked about, and when its functions are written; this says it where the rules are made.");

    public static readonly DiagnosticDescriptor OwnAlternativeRedundant = Create(
        id: "DDD00096",
        title: "[OrOwn] beside a permission the owner of the resource holds already says nothing more",
        messageFormat: "[OrOwn] on '{0}' says nothing more: the owner of the {1} holds '{2}' already, which is declared [On<{1}>] with Owner = true and is asked on it. Remove [OrOwn].",
        category: Access,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "[OrOwn] on a request's member says the request is about the caller's own, or the caller holds the permission of its [RequiresPermission]. On the id of a resource with members, beside [OnResource] on it, the permission is asked on that resource, and its owner holds every permission declared [On<TResource>] with Owner = true by owning it: the owner is let through by the permission already, so [OrOwn] adds nothing and only makes the request read as if it did. [Own] and [OrOwn] stay meant for an aggregate with an [Owner] and no members, and for a permission the owner does not hold, one declared with Owner = false.");

    public static readonly DiagnosticDescriptor PermissionWithoutDescription = Create(
        id: "DDD00099",
        title: "A permission says what it lets somebody do",
        messageFormat: "'{0}' has no description, and the screens that put permissions into roles show it: say it on its level, [{1}(Description = \"...\")], or in the constant's <summary>",
        category: Access,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The catalogue gives every permission a description, which a screen that puts permissions into roles shows beside it: what holding it lets somebody do. It is said with Description on the permission's level, [AtUnit(Description = \"Open a board at a unit\")] say, which wins where the constant has a <summary> too; where no level says one, the constant's <summary> is the description, read from the comment above it whether the project writes a documentation file or not. A permission with neither would show as a bare name. A retired permission is shown nowhere, and needs none. A translation, in a resx your failure localizer reads, under the permission's name, is read on top of either.");
    public static readonly DiagnosticDescriptor RestrictiveRuleOutOfReach = Create(
        id: "DDD00100",
        title: "A restrictive rule narrows a table it can reach",
        messageFormat: "'{0}' is a restrictive rule on '{1}', {2} of Tenancy's, and a restrictive rule narrows the organization, the seats, the roles and the application's own aggregates alone: put it on one of those, or leave Restrictive out",
        category: Tenancy,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A restrictive row access rule, Restrictive = true, narrows what the permissive policies allow on its aggregate's table and the tables of its entities. On Tenancy's tables it reaches the organization with its units and the closure of its tree, the seats with their placements and grants, and the roles: what an application narrows to keep a seat that is not an employee out of the organization. A tenant and an invitation are read by Tenancy's own work and by the person accepting an invitation, who has no seat yet, so a rule there is refused.");

    public static readonly DiagnosticDescriptor CallerSeatOfAnotherType = Create(
        id: "DDD00101",
        title: "TenancyRowAccess.CallerSeat answers with the application's seat class",
        messageFormat: "TenancyRowAccess.CallerSeat<{0}>() asks for a seat of '{0}', which is not the application's seat class: name the class declared with [SeatAggregate], whose row the function answers with",
        category: Tenancy,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "TenancyRowAccess.CallerSeat<TSeat>() is the calling seat's own row, read by a rule through the fields of the application's seat class: CallerSeat<Seat>().Kind == SeatKind.Employee. The function answers with the row of Tenancy's seats table, which the seat class is mapped to, so TSeat is that class, the one declared with [SeatAggregate]. For the seat's id in a rule of a module whose context does not map the seats, ask TenancyRowAccess.CallerSeatId<TSeatId>().");

    public static readonly DiagnosticDescriptor RequirementArgumentOutOfPlace = Create(
        id: "DDD00102",
        title: "A marker of a requirement's argument goes beside the attribute whose call it is",
        messageFormat: "'{0}' {1}",
        category: Access,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A package marks an attribute of its own as one more argument of a requirement's call with [RequirementArgument(typeof(TAttribute))]. Tenancy's [ForTenant] is one: it goes on the tenant of a request that carries [Operator]. The generator hands the marked member to the call of that attribute. Without the attribute on the request the member is handed to nothing, and the request reads as if it said something it does not. Two members marked with one marker give the call two arguments where it takes one. A marker goes on a positional parameter of a record, which is its property, or on a property the module's interface can read. A parameter of a class's primary constructor is no member the requirement can read, and neither is a private or protected property, since the line is written in the module's interface.");

    public static readonly DiagnosticDescriptor OperatorTenantTwice = Create(
        id: "DDD00103",
        title: "An operator's command takes its tenant from the request or from itself",
        messageFormat: "'{0}' carries [Operator(Tenant = OperatorTenant.FromRequest)] and marks '{1}' [ForTenant], so its work would act in the tenant the request names and in the one the command names: keep [ForTenant] for a command that names its tenant, or FromRequest for one that takes the tenant the request names",
        category: Tenancy,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[Operator] on a command runs its handler as operator work. The work acts in the tenant of the command's [ForTenant] member. With Tenant = OperatorTenant.FromRequest it acts in the tenant the request names beside the command, as tenant selection reads it from the Tenant header. With neither it acts in no tenant, which is what provisioning needs. A command says one of the three. Both at once name two tenants for one piece of work.");

    public static readonly DiagnosticDescriptor OperatorBesideSeatRequirement = Create(
        id: "DDD00104",
        title: "[Operator] stands beside no requirement an operator cannot meet",
        messageFormat: "'{0}' carries [Operator] beside {1}, which no operator meets, since an operator holds no seat and no permission in any tenant, so the two refuse every caller: keep [Operator] for a request of the application's staff or {1} for a request of a tenant's seats, and answer RequiredAccess with AccessRequirement.Any for a request either may send",
        category: Tenancy,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Requirement attributes stacked on one request all hold. [Operator] lets through only a signed-in user with an operator's token role. An operator holds no seat in any tenant and no permission, so [InTenant] and [RequiresPermission(permission)] refuse every operator. Stacked, the two refuse every caller, and the request can never be sent. A request that an operator or a seat with a permission may send answers RequiredAccess itself, with AccessRequirement.Any(TenancyAccess.Operator(), TenancyAccess.AtTenant(permission)).");

    public static readonly DiagnosticDescriptor OperatorWorkOnAQuery = Create(
        id: "DDD00105",
        title: "[Operator] on a query names no tenant and no scope",
        messageFormat: "'{0}' is a query and carries [Operator({1})], but nothing begins for a query, so {1} says nothing: take it off, since an operator's query reads across tenants through the database role of the operator's token",
        category: Tenancy,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "On a command, [Operator] runs the handler as operator work, and Tenant and Scope say where the work acts and in which scope. On a query nothing begins: an operator reads across tenants through the database role of its token. So Tenant and Scope on a query say nothing, and a reader takes them for the tenant the query reads in. [ForTenant] on a query is not reported: it still refuses a request that names another tenant than the query does.");

}
