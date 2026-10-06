; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DDD00001 | DDDToolkit.ValueObjects | Error | Value objects must be records
DDD00002 | DDDToolkit.Entities | Error | Entities must be classes
DDD00003 | DDDToolkit.EntityIds | Error | Entity ids must be records
DDD00004 | DDDToolkit.EntityIds | Warning | Entity id structs should be readonly
DDD00005 | DDDToolkit.Usage | Error | DDDToolkit types must be partial
DDD00006 | DDDToolkit.Usage | Error | DDDToolkit types cannot be generic
DDD00007 | DDDToolkit.Entities | Error | The generated identifier name is already taken
DDD00008 | DDDToolkit.Entities | Error | The identifier type argument is not supported
DDD00009 | DDDToolkit.Entities | Error | A type is either an entity or an aggregate root
DDD00010 | DDDToolkit.ValueObjects | Error | Value object properties must use protected setters
DDD00011 | DDDToolkit.ValueObjects | Error | Value object properties must use init setters
DDD00013 | DDDToolkit.ValueObjects | Error | Value objects cannot be sealed
DDD00014 | DDDToolkit.Usage | Warning | The generators cannot read the project's MSBuild properties
DDD00020 | DDDToolkit.Entities | Error | Generated collection properties must be get-only
DDD00021 | DDDToolkit.Entities | Warning | Reference another aggregate by its id
DDD00022 | DDDToolkit.Modules | Warning | Use only what another module publishes
DDD00023 | DDDToolkit.Modules | Warning | Do not hold another module's entity
DDD00024 | DDDToolkit.Invariants | Warning | An invariant must be nested inside the entity it is about
DDD00025 | DDDToolkit.Invariants | Warning | An invariant is nested inside a type it is not about
DDD00026 | DDDToolkit.Invariants | Warning | Two invariants of one entity share a code
DDD00027 | DDDToolkit.Invariants | Error | An invariant needs an accessible parameterless constructor
DDD00028 | DDDToolkit.Entities | Error | A key part belongs on an entity or aggregate root
DDD00029 | DDDToolkit.Entities | Warning | A key part should not have a public setter
DDD00030 | DDDToolkit.Entities | Error | Declare all key parts of a type in one file
DDD00031 | DDDToolkit.Supabase | Error | A [SupabaseMigrations] factory must be one the build can create
DDD00032 | DDDToolkit.GraphQL | Warning | Do not ask HotChocolate's generator for a toolkit identifier's node id serializer
DDD00033 | DDDToolkit.IntegrationEvents | Warning | The generated integration event registration must be able to construct the class
DDD00034 | DDDToolkit.Events | Warning | An event's class name and its Version disagree
DDD00035 | DDDToolkit.Events | Error | An event's class name ends in something that is not a version
DDD00036 | DDDToolkit.Events | Error | Two events of one module share a name and version
DDD00037 | DDDToolkit.Events | Error | Two event names give one constant name
DDD00038 | DDDToolkit.Access | Error | A row access rule, an access function or a database question has the shape the generator reads
DDD00039 | DDDToolkit.Access | Error | A row access rule can only say what the database can check
DDD00040 | DDDToolkit.Access | Error | A row access rule guards an aggregate root
DDD00041 | DDDToolkit.Access | Error | A row access rule reads the aggregate's entities through an access function
DDD00042 | DDDToolkit.Entities | Error | A parent for entities is an abstract generic class whose first type parameter is the id
DDD00043 | DDDToolkit.Entities | Error | A template's first type argument is an entity id
DDD00044 | DDDToolkit.Entities | Error | A template takes a type from a class nobody declares
DDD00045 | DDDToolkit.Entities | Error | A template takes a type from a class declared more than once
DDD00046 | DDDToolkit.Entities | Error | A template attribute fills exactly the type parameters of its parent
DDD00047 | DDDToolkit.Entities | Error | A class is declared an entity or aggregate root once
DDD00048 | DDDToolkit.Entities | Error | A class a template takes meets its parent's constraints
DDD00049 | DDDToolkit.Entities | Error | A template registration needs a class declared with each of its templates
DDD00050 | DDDToolkit.Entities | Error | A type a template registration takes meets the method's constraints
DDD00051 | DDDToolkit.Access | Error | A set-shaped question is asked once per statement, so its arguments do not read the row
DDD00052 | DDDToolkit.Access | Error | A function named without its schema belongs to a module
DDD00053 | DDDToolkit.Entities | Error | A type argument of a template meets its parent's constraints
DDD00054 | DDDToolkit.Supabase | Warning | A package's row access contribution is made from what the application marks
DDD00055 | DDDToolkit.Supabase | Warning | A context's migration files are named after its module
DDD00056 | DDDToolkit.Access | Error | A request interface is one a behavior can be written for
DDD00057 | DDDToolkit.Access | Error | The Mediator library's pipeline behavior has the shape the generator writes a behavior for
DDD00058 | DDDToolkit.Access | Error | A notification implements no request interface
DDD00059 | DDDToolkit.Membership | Warning | The member list of a resource is written from what the resource declares
DDD00060 | DDDToolkit.Membership | Warning | A member class names an aggregate root whose members it is
DDD00061 | DDDToolkit.Access | Warning | A request that declares its access is sent, not handed to its handler
DDD00062 | DDDToolkit.GraphQL | Error | A class of one GraphQL schema is one the toolkit alone registers
DDD00063 | DDDToolkit.Tenancy | Error | A module's keys marked [TenancyPermissions] are a list the project that composes the modules can read
DDD00064 | DDDToolkit.Modules | Warning | DDD_Module declares the module where the package's build step runs
DDD00065 | DDDToolkit.Entities | Info | The class a package's use cases are named through is written where each of its templates has one class
DDD00066 | DDDToolkit.Entities | Error | A package's switch writes a class or an id where its name is free and its id is known
DDD00067 | DDDToolkit.Entities | Error | A class whose package makes its new ids is declared over an id with a Create()
DDD00068 | DDDToolkit.Modules | Warning | DDD_ModuleContracts makes a project its module's contracts where the project can name the attribute
DDD00066 | DDDToolkit.Supabase | Error | What a package's row access contribution is made from is found once, and is what it takes
DDD00067 | DDDToolkit.Supabase | Error | A row access contribution a package writes is not listed again
DDD00068 | DDDToolkit.Supabase | Warning | What [assembly: LeaveOutRowAccessContribution] names is a contribution a package writes, and a context
DDD00069 | DDDToolkit.Supabase | Warning | A module's row access contribution is listed by the project that runs the export
DDD00070 | DDDToolkit.Supabase | Error | A member a library marks for a package's row access contribution is public
