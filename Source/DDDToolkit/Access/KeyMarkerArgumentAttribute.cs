namespace DDDToolkit.Access;

/// <summary>
/// Makes an attribute a package ships a marker of one more argument of a <see cref="KeyMarkerAttribute"/> marker's call,
/// which goes beside that marker and nowhere else. Membership declares the version a caller read this way:
/// <code>
/// [KeyMarkerArgument(typeof(OnResourceAttribute))]
/// [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
/// public sealed class ExpectedVersionAttribute : Attribute;
/// </code>
/// so <c>[RequiresKey(key)]</c> with <c>[OnResource] DocumentId Document</c> and <c>[ExpectedVersion] long? ExpectedVersion</c>
/// is <c>MemberAccess.On(key, request.Document, request.ExpectedVersion)</c>.
/// </summary>
/// <remarks>
/// The marked member is handed to the call after the member <see cref="Marker"/> marks, and it is of a type the call's
/// parameter there takes. Without that marker on the request it is DDD00080, and of another type DDD00081.
/// </remarks>
/// <param name="marker">The marker the argument goes with: <c>typeof(OnResourceAttribute)</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class KeyMarkerArgumentAttribute(Type marker) : Attribute
{
    /// <summary>The marker the argument goes with.</summary>
    public Type Marker { get; } = marker;
}
