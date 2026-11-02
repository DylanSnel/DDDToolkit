namespace DDDToolkit.Access;

/// <summary>
/// Marks an attribute class as an argument marker of a permission marker (<see cref="PermissionMarkerAttribute"/>). The
/// member it marks becomes one more argument of the permission marker's call. An argument marker goes on a request beside
/// its permission marker and nowhere else. Membership declares <c>[ExpectedVersion]</c>, the version the caller read, this
/// way, in a bare form and in a form that names the resource:
/// <code>
/// [PermissionMarkerArgument(typeof(OnResourceAttribute))]
/// [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
/// public sealed class ExpectedVersionAttribute : Attribute;
///
/// [PermissionMarkerArgument(typeof(OnResourceAttribute))]
/// [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
/// public sealed class ExpectedVersionAttribute&lt;TResource&gt; : Attribute where TResource : class;
/// </code>
/// So <c>[OnResource(DocumentPermissions.Share)] DocumentId Document</c> with <c>[ExpectedVersion] long? ExpectedVersion</c>
/// becomes <c>MemberAccess.On(DocumentPermissions.Share, request.Document, request.ExpectedVersion)</c>.
/// </summary>
/// <remarks>
/// The generator passes the marked member to the call of the permission marker, after the member that the permission
/// marker marks. The marked member's type must fit the call's parameter at that place, or it is DDD00081.
/// <para>
/// The bare form belongs to the only <see cref="Marker"/> on the request. When a request has two or more such markers,
/// the generic form says which one. Its type argument is the class whose id the marker's member is, an aggregate root
/// such as <c>Board</c>. <c>[ExpectedVersion&lt;Board&gt;]</c> belongs to the <c>[OnResource]</c> on a <c>BoardId</c>.
/// The generic form is also allowed beside one marker. The generator treats both forms of one name and namespace as one
/// argument, so a call gets the argument once. The request is DDD00080 when no permission marker matches the argument,
/// or when two permission markers match it.
/// </para>
/// </remarks>
/// <param name="marker">The marker the argument goes with: <c>typeof(OnResourceAttribute)</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PermissionMarkerArgumentAttribute(Type marker) : Attribute
{
    /// <summary>The marker the argument goes with.</summary>
    public Type Marker { get; } = marker;
}
