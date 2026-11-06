; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DDD00001 | DTS.DDD.ValueObjects | Error | Value objects must be records
DDD00002 | DTS.DDD.Entities | Error | Entities must be classes
DDD00003 | DTS.DDD.EntityIds | Error | Entity ids must be records
DDD00004 | DTS.DDD.EntityIds | Warning | Entity id structs should be readonly
DDD00005 | DTS.DDD.Usage | Error | DDDToolkit types must be partial
DDD00006 | DTS.DDD.Usage | Error | DDDToolkit types cannot be generic
DDD00007 | DTS.DDD.Entities | Error | The generated identifier name is already taken
DDD00008 | DTS.DDD.Entities | Error | The identifier type argument is not supported
DDD00009 | DTS.DDD.Entities | Error | A type is either an entity or an aggregate root
DDD00010 | DTS.DDD.ValueObjects | Error | Value object properties must use protected setters
DDD00011 | DTS.DDD.ValueObjects | Error | Value object properties must use init setters
DDD00013 | DTS.DDD.ValueObjects | Error | Value objects cannot be sealed
DDD00014 | DTS.DDD.Usage | Warning | The generators cannot read the project's MSBuild properties
DDD00020 | DTS.DDD.Entities | Error | Generated collection properties must be get-only
DDD00021 | DTS.DDD.Entities | Warning | Reference another aggregate by its id
DDD00022 | DTS.DDD.Modules | Warning | Use only what another module publishes
DDD00023 | DTS.DDD.Modules | Warning | Do not hold another module's entity
DDD00024 | DTS.DDD.Invariants | Warning | An invariant must be nested inside the entity it is about
DDD00025 | DTS.DDD.Invariants | Warning | An invariant is nested inside a type it is not about
DDD00026 | DTS.DDD.Invariants | Warning | Two invariants of one entity share a code
DDD00027 | DTS.DDD.Invariants | Error | An invariant needs an accessible parameterless constructor
DDD00028 | DTS.DDD.Entities | Error | A key part belongs on an entity or aggregate root
DDD00029 | DTS.DDD.Entities | Warning | A key part should not have a public setter
DDD00030 | DTS.DDD.Entities | Error | Declare all key parts of a type in one file
DDD00031 | DTS.DDD.Supabase | Error | A [SupabaseMigrations] context or factory must be one the build can make
DDD00032 | DTS.DDD.GraphQL | Warning | Do not ask HotChocolate's generator for a toolkit identifier's node id serializer
DDD00033 | DTS.DDD.IntegrationEvents | Warning | The generated integration event registration must be able to construct the class
DDD00034 | DTS.DDD.Events | Warning | An event's class name and its Version disagree
DDD00035 | DTS.DDD.Events | Error | An event's class name ends in something that is not a version
DDD00036 | DTS.DDD.Events | Error | Two events of one module share a name and version
DDD00037 | DTS.DDD.Events | Error | Two event names give one constant name
DDD00038 | DTS.DDD.Access | Error | A row access rule, an access function or a database question has the shape the generator reads
DDD00039 | DTS.DDD.Access | Error | A row access rule can only say what the database can check
DDD00040 | DTS.DDD.Access | Error | A row access rule guards an aggregate root
DDD00041 | DTS.DDD.Access | Error | A row access rule reads the aggregate's entities through an access function
DDD00042 | DTS.DDD.Entities | Error | A parent for entities is an abstract generic class whose first type parameter is the id
DDD00043 | DTS.DDD.Entities | Error | A template's first type argument is an entity id
DDD00044 | DTS.DDD.Entities | Error | A template takes a type from a class nobody declares
DDD00045 | DTS.DDD.Entities | Error | A template takes a type from a class declared more than once
DDD00046 | DTS.DDD.Entities | Error | A template attribute fills exactly the type parameters of its parent
DDD00047 | DTS.DDD.Entities | Error | A class is declared an entity or aggregate root once
DDD00048 | DTS.DDD.Entities | Error | A class a template takes meets its parent's constraints
DDD00049 | DTS.DDD.Entities | Error | A template registration needs a class declared with each of its templates
DDD00050 | DTS.DDD.Entities | Error | A type a template registration takes meets the method's constraints
DDD00051 | DTS.DDD.Access | Error | A set-shaped question is asked once per statement, so its arguments do not read the row
DDD00052 | DTS.DDD.Access | Error | A function named without its schema belongs to a module
DDD00053 | DTS.DDD.Entities | Error | A type argument of a template meets its parent's constraints
DDD00054 | DTS.DDD.Supabase | Warning | A package's row access contribution is made from what the application marks
DDD00055 | DTS.DDD.Supabase | Warning | A context's migration files are named after its module
DDD00056 | DTS.DDD.Access | Error | A request interface is one a behavior can be written for
DDD00057 | DTS.DDD.Access | Error | The mediator's pipeline behavior has the shape the generator writes a behavior for
DDD00058 | DTS.DDD.Access | Error | A notification implements no request interface
DDD00059 | DTS.DDD.Membership | Warning | The member list of a resource is written from what the resource declares
DDD00060 | DTS.DDD.Membership | Warning | A member class names an aggregate root whose members it is
DDD00061 | DTS.DDD.Access | Warning | A request that declares its access is sent, not handed to its handler
DDD00062 | DTS.DDD.GraphQL | Error | A class of one GraphQL schema is one the toolkit alone registers
DDD00064 | DTS.DDD.Modules | Warning | DDD_Module declares the module where the package's build step runs
DDD00065 | DTS.DDD.Entities | Info | The class a package's use cases are named through is written where each of its templates has one class
DDD00066 | DTS.DDD.Entities | Error | A package's switch writes a class or an id where its name is free and its id is known
DDD00067 | DTS.DDD.Entities | Error | A class whose package makes its new ids is declared over an id with a Create()
DDD00068 | DTS.DDD.Modules | Warning | DDD_ModuleContracts makes a project its module's contracts where the project can name the attribute
DDD00069 | DTS.DDD.Supabase | Warning | A module's row access contribution is listed by the project that runs the export
DDD00070 | DTS.DDD.Supabase | Error | A member a library marks for a package's row access contribution is public
DDD00071 | DTS.DDD.Supabase | Error | What a package's row access contribution is made from is found once, and is what it takes
DDD00072 | DTS.DDD.Supabase | Error | A row access contribution a package writes is not listed again
DDD00073 | DTS.DDD.Supabase | Warning | What [assembly: LeaveOutRowAccessContribution] names is a contribution a package writes, and a context
DDD00074 | DTS.DDD.EntityFramework | Warning | A design-time factory keeps the migration history where the application does
DDD00075 | DTS.DDD.Entities | Warning | Every class a package's use cases are named through has a name of its own where a project sees it
DDD00076 | DTS.DDD.Entities | Warning | [assembly: TemplateFacadeName] names, once, a class this project gets
DDD00077 | DTS.DDD.Access | Error | A request says what it requires of its caller
DDD00078 | DTS.DDD.Access | Error | A request says what it requires in one way
DDD00079 | DTS.DDD.Access | Error | [AllowAnonymous] goes with no other requirement attribute
DDD00080 | DTS.DDD.Access | Error | A permission marker names its permission, and a marker argument belongs to one marker
DDD00081 | DTS.DDD.Access | Error | A marker is on a member of the id it marks
DDD00082 | DTS.DDD.Access | Error | A requirement for queries only is on a query
DDD00083 | DTS.DDD.Access | Error | A request's requirement attributes are read where its module's interface is partial
DDD00084 | DTS.DDD.Access | Warning | A requirement attribute beside another adds what that one does not require already
DDD00085 | DTS.DDD.Access | Error | [OrOwn] pairs with one permission, and [Own] needs [OwnOnly]
DDD00086 | DTS.DDD.Access | Error | [OrOwn] and [Own] mark a member of a type something says the caller's own of
DDD00087 | DTS.DDD.Access | Error | [Owner] marks the one owner of an aggregate root
DDD00088 | DTS.DDD.Access | Error | [Owner] marks a property of a type the caller has an own of
DDD00089 | DTS.DDD.Access | Error | A module's default requirement is one any of its requests may take
DDD00090 | DTS.DDD.Supabase | Warning | The names of resources with members are those of the newest access file
DDD00091 | DTS.DDD.Membership | Error | [Members] names a resource with what can be converted to a code and a function's name
DDD00092 | DTS.DDD.Access | Error | A permission is a public constant of a [Permissions] class, with a level
DDD00093 | DTS.DDD.Membership | Error | A permission's level fits what it is asked on
DDD00094 | DTS.DDD.Membership | Error | The rules of a resource are made from the permissions declared on it
DDD00095 | DTS.DDD.Tenancy | Error | A module's permissions are declared in a [Permissions] class, not on a list of its own
DDD00096 | DTS.DDD.Access | Warning | [OrOwn] beside a permission the owner of the resource holds already says nothing more
DDD00097 | DTS.DDD.Access | Error | [At] marks the one place an aggregate root hangs at
DDD00098 | DTS.DDD.Membership | Error | Rules that reach a resource from above read where it hangs from its [At] property
DDD00099 | DTS.DDD.Access | Warning | A permission says what it lets somebody do
DDD00100 | DTS.DDD.Tenancy | Error | A restrictive rule narrows a table it can reach
DDD00101 | DTS.DDD.Tenancy | Error | CurrentSeat answers with the application's seat class
DDD00102 | DTS.DDD.Access | Error | A marker of a requirement's argument goes beside the attribute whose call it is
DDD00103 | DTS.DDD.Tenancy | Error | An operator's command takes its tenant from the request or from itself
DDD00104 | DTS.DDD.Tenancy | Error | [Operator] stands beside no requirement an operator cannot meet
DDD00105 | DTS.DDD.Tenancy | Warning | [Operator] on a query names no tenant and no scope
DDD00106 | DTS.DDD.Membership | Info | A member or role manager is not written
DDD00107 | DTS.DDD.Access | Error | The registration of an access behavior is written and in scope where it is called
DDD00108 | DTS.DDD.Supabase | Warning | The migrations make the model the access files are written from
DDD00109 | DTS.DDD.Access | Error | A permission marker on a collection of ids takes no marker argument and no [OrOwn]
DDD00110 | DTS.DDD.Mediator | Error | A request, a command or a query has one handler
