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
