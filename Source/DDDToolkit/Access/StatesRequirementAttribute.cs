namespace DDDToolkit.Access;

/// <summary>
/// Makes an attribute a package ships one that states a request's requirement, and says the call that makes it. The
/// core declares its own this way, and Tenancy and Membership theirs:
/// <code>
/// [StatesRequirement(typeof(TenancyAccess), nameof(TenancyAccess.InTenant))]
/// [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
/// public sealed class InTenantAttribute : Attribute;
/// </code>
/// A request that carries <c>[InTenant]</c> then answers <see cref="IRequireAccess.RequiredAccess"/> with
/// <c>TenancyAccess.InTenant()</c>.
/// </summary>
/// <remarks>
/// The toolkit's generator writes the call for every request that carries the attribute, in the module's request
/// interface: the method of <see cref="Factory"/> named <see cref="Method"/>, with the attribute's constructor
/// arguments in their order and its type arguments as the method's. So <c>[SeenWith&lt;DocumentId&gt;(DocumentKeys.View)]</c>,
/// whose class is marked <c>[StatesRequirement(typeof(MemberAccess), nameof(MemberAccess.SeenWith))]</c>, is
/// <c>MemberAccess.SeenWith&lt;DocumentId&gt;("documents.view")</c>. The method is public and static, answers an
/// <see cref="AccessRequirement"/>, and takes what the attribute's constructor takes, in that order: what does not is
/// the package's mistake, which the compiler reports in the line the generator wrote.
/// <para>
/// A request carries one requirement, so an attribute marked this way does not go with another, nor with
/// <see cref="RequiresKeyAttribute"/>, nor with a request that answers the property itself (DDD00078, DDD00079).
/// </para>
/// </remarks>
/// <param name="factory">The class whose static method makes the requirement: <c>typeof(TenancyAccess)</c>.</param>
/// <param name="method">The method's name: <c>nameof(TenancyAccess.InTenant)</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StatesRequirementAttribute(Type factory, string method) : Attribute
{
    /// <summary>The class whose static method makes the requirement.</summary>
    public Type Factory { get; } = factory;

    /// <summary>The name of the method that makes it.</summary>
    public string Method { get; } = method;

    /// <summary>
    /// Whether only a query may state it: a requirement that refuses nobody for want of a key and says what a query's
    /// own statement filters by, such as Membership's <c>SeenWith</c>, or one for a caller who changes nothing, such
    /// as Tenancy's operators. On a message of the Mediator library that is no query, a command or a plain request,
    /// it would let the handler run unchecked, or let through a caller who changes nothing, so there it is DDD00082.
    /// A request of another dispatcher, which the generator cannot tell for a command or a query, is not reported.
    /// </summary>
    public bool QueriesOnly { get; set; }
}
