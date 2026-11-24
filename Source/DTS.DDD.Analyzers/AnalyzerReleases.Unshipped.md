; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DDD00111 | DTS.DDD.Usage | Error | A Result is read or returned
DDD00112 | DTS.DDD.Usage | Error | [ErrorCode] is on a record that derives from Error
DDD00113 | DTS.DDD.Usage | Error | A placeholder in the message of [ErrorCode] names a property of the record
DDD00114 | DTS.DDD.Usage | Error | Two errors of an assembly have the same code
DDD00115 | DTS.DDD.ValueObjects | Error | A nested Validator calls only the rules the generator knows
DDD00116 | DTS.DDD.ValueObjects | Error | A lambda of a nested Validator is an expression over its own parameters
DDD00117 | DTS.DDD.ValueObjects | Error | WithError creates an error record with [ErrorCode] that derives from InvalidError
DDD00118 | DTS.DDD.ValueObjects | Error | The constructor of a nested Validator holds only rules
DDD00119 | DTS.DDD.ValueObjects | Error | A rule of a nested Validator uses no local, parameter or instance member of the Validator
