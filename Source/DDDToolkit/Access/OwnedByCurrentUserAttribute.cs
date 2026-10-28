namespace DDDToolkit.Access;

/// <summary>
/// Says, of the assembly of a package, a type that package says the caller's own of, with an
/// <see cref="IOwnedByCurrentUser{TValue}"/> it registers itself: so the generator knows that <see cref="OrOwnAttribute"/> and
/// <see cref="OwnAttribute"/> on a member of that type compare it with something. The core says it for a
/// <see cref="Guid"/>, the caller's verified identity, and Tenancy for the seat id an application declares its seat
/// class with:
/// <code>
/// [assembly: OwnedByCurrentUser(typeof(Guid))]
/// [assembly: OwnedByCurrentUser(IdOf = typeof(SeatAggregateAttribute&lt;&gt;))]
/// </code>
/// </summary>
/// <remarks>
/// An application needs none: the generator finds its own <see cref="IOwnedByCurrentUser{TValue}"/> where the requests' project
/// sees it, its own or one it references. A package needs it where its handle is written over a type parameter, the
/// application's seat id say, which the generator cannot read off the class: <see cref="IdOf"/> names the template the
/// application declares the class with, as <see cref="PermissionMarkerAttribute.IdOf"/> does, and the id of every class the
/// project sees declared with it is one. Where the project sees no such class, nothing can be told, and nothing is
/// reported.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class OwnedByCurrentUserAttribute : Attribute
{
    /// <summary>A package's handle for a type it can name: <c>typeof(Guid)</c>.</summary>
    /// <param name="valueType">The type.</param>
    public OwnedByCurrentUserAttribute(Type valueType) => ValueType = valueType;

    /// <summary>A package's handle for the id of a class an application declares with a template: set <see cref="IdOf"/>.</summary>
    public OwnedByCurrentUserAttribute()
    {
    }

    /// <summary>The type the caller's own is said of; <see langword="null"/> where <see cref="IdOf"/> names it.</summary>
    public Type? ValueType { get; }

    /// <summary>
    /// The template whose classes' ids the caller's own is said of, as <c>[TemplateType]</c> names one: the open generic
    /// attribute an application declares the class with, <c>typeof(SeatAggregateAttribute&lt;&gt;)</c>.
    /// </summary>
    public Type? IdOf { get; set; }

    /// <summary>
    /// The requirement attributes a caller who has an own of the type meets already, as
    /// <see cref="PermissionMarkerAttribute.Implies"/> says them of a permission: only a signed-in user has a verified identity, so the
    /// core's <see cref="Guid"/> names <see cref="SignedInAttribute"/>, and only a seat in the tenant, a signed-in user, has a
    /// seat of its own there, so Tenancy's seat id names <c>[InTenant]</c> and <c>[SignedIn]</c>. Beside <c>[OwnOnly]</c> on
    /// a member of the type, or beside a permission with <c>[OrOwn]</c> that requires it too, one of them is DDD00084, a warning.
    /// Empty by default.
    /// </summary>
    public Type[] Implies { get; set; } = [];
}
