; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DDD00111 | DTS.DDD.Usage | Error | A Result is read or returned
DDD00112 | DTS.DDD.Usage | Error | [ErrorCode] is on a record that derives from Error
DDD00113 | DTS.DDD.Usage | Error | A placeholder in the message of [ErrorCode] names a property of the record
DDD00114 | DTS.DDD.Usage | Error | Two errors of an assembly have the same code
