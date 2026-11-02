namespace DDDToolkit.Access;

/// <summary>
/// Makes an attribute a package ships a marker of one more argument of a <see cref="PermissionMarkerAttribute"/> marker's
/// call. It goes beside that marker and nowhere else. Membership declares the version a caller read this way, bare and
/// with the resource it is of:
/// <code>
/// [PermissionMarkerArgument(typeof(OnResourceAttribute))]
/// [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
/// public sealed class ExpectedVersionAttribute : Attribute;
///
/// [PermissionMarkerArgument(typeof(OnResourceAttribute))]
/// [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
/// public sealed class ExpectedVersionAttribute&lt;TResource&gt; : Attribute where TResource : class;
/// </code>
/// so <c>[OnResource(DocumentPermissions.Share)] DocumentId Document</c> with <c>[ExpectedVersion] long? ExpectedVersion</c>
/// is <c>MemberAccess.On(DocumentPermissions.Share, request.Document, request.ExpectedVersion)</c>.
/// </summary>
/// <remarks>
/// The marked member is handed to the call of the marker it goes with, after the member that marker marks. It is of a
/// type the call's parameter there takes, or it is DDD00081.
/// <para>
/// The bare form goes with the one marker of <see cref="Marker"/> on the request. Beside two or more such markers, the
/// generic form names which one, with its type argument: the class whose id that marker's member is, an aggregate root
/// such as <c>Board</c>. <c>[ExpectedVersion&lt;Board&gt;]</c> goes with the <c>[OnResource]</c> on a <c>BoardId</c>, and
/// the generic form is allowed beside one marker too. The generator takes both forms, of one name and namespace, for one
/// argument, so a call gets it once. Without a marker it goes with, or beside two markers it cannot tell apart, the
/// request is DDD00080.
/// </para>
/// </remarks>
/// <param name="marker">The marker the argument goes with: <c>typeof(OnResourceAttribute)</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PermissionMarkerArgumentAttribute(Type marker) : Attribute
{
    /// <summary>The marker the argument goes with.</summary>
    public Type Marker { get; } = marker;
}
