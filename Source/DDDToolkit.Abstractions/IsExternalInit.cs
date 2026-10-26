// Polyfill: records with init-only setters need this marker type, which netstandard2.0 lacks.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
#endif
