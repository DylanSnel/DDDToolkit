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
