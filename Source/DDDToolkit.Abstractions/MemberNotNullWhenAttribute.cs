// Polyfill: the nullable analysis of ICurrentUser.IsAuthenticated needs this attribute, which netstandard2.0 lacks.
// The compiler reads it by its name, so an internal copy works for this assembly and for the projects that reference it.
#if !NET5_0_OR_GREATER
namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
internal sealed class MemberNotNullWhenAttribute : Attribute
{
    public MemberNotNullWhenAttribute(bool returnValue, string member)
    {
        ReturnValue = returnValue;
        Members = [member];
    }

    public MemberNotNullWhenAttribute(bool returnValue, params string[] members)
    {
        ReturnValue = returnValue;
        Members = members;
    }

    public bool ReturnValue { get; }

    public string[] Members { get; }
}
#endif
