namespace DDDToolkit.Access;

/// <summary>
/// Makes an attribute a package ships one that states a request's requirement, and says the call that makes it. The
/// core declares its own this way, and Tenancy and Membership theirs:
/// <code>
/// [StatesRequirement(typeof(TenancyAccess), nameof(TenancyAccess.InTenant))]
/// [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
/// public sealed class InTenantAttribute : Attribute;
/// </code>
/// A request that carries <c>[InTenant]</c> then answers <see cref="IRequireAccess.RequiredAccess"/> with
/// <c>TenancyAccess.InTenant()</c>.
/// </summary>
/// <remarks>
/// The toolkit's generator writes the call for every request that carries the attribute, in the module's request
/// interface: the method of <see cref="Factory"/> named <see cref="Method"/>, with the attribute's constructor
/// arguments in their order and its type arguments as the method's. So <c>[SeenWith&lt;DocumentId&gt;(DocumentPermissions.View)]</c>,
/// whose class is marked <c>[StatesRequirement(typeof(MemberAccess), nameof(MemberAccess.SeenWith))]</c>, is
/// <c>MemberAccess.SeenWith&lt;DocumentId&gt;("documents.view")</c>. The method is public and static, answers an
/// <see cref="AccessRequirement"/>, and takes what the attribute's constructor takes, in that order: what does not is
/// the package's mistake, which the compiler reports in the line the generator wrote.
/// <para>
/// After the constructor's arguments come the members of the request a marker of one more argument marks
/// (<see cref="RequirementArgumentAttribute"/>), and then each property set where the request carries the attribute, as
/// a named argument: the property's name with a lower case first letter. So Tenancy's
/// <c>[Operator(Scope = "projects")]</c> on a request with <c>[ForTenant] TenantId Tenant</c> is
/// <c>TenancyAccess.Operator(request.Tenant, scope: "projects")</c>, and the method has a parameter of that name.
/// </para>
/// <para>
/// Attributes stacked on one request all hold: the generator writes <c>AccessRequirement.All(...)</c> of their calls, in
/// the order they are written above the request, so a module's own attribute stacks beside a package's as theirs do.
/// None goes with a request that answers the property itself (DDD00078), and none with <c>[AllowAnonymous]</c>, which
/// would open the door the other closes (DDD00079). One that says less than another beside it, which already requires
/// it, is reported as redundant (DDD00084): say so with <see cref="Implies"/>.
/// </para>
/// <para>
/// On the module's interface, <c>[AccessRequests, InTenant]</c>, the attribute is the module's default: a request of it
/// that states nothing of its own gets a line with the call. An attribute is one there where its class's
/// <see cref="AttributeUsageAttribute"/> allows <see cref="AttributeTargets.Interface"/>, as <c>[InTenant]</c>'s does;
/// one for a request alone leaves it out, and the compiler refuses it on an interface (CS0592). One for queries only,
/// <see cref="QueriesOnly"/>, is no default, since a command that says nothing would take it, and neither is
/// <c>[AllowAnonymous]</c>, which would let anyone send a request that forgot its own: each is DDD00089.
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
    /// Whether only a query may state it: a requirement that refuses nobody for want of a permission and says what a query's
    /// own statement filters by, such as Membership's <c>SeenWith</c>. On a message of the Mediator library that is no
    /// query, a command or a plain request, it would let the handler run unchecked, so there it is DDD00082.
    /// A request of another dispatcher, which the generator cannot tell for a command or a query, is not reported.
    /// </summary>
    public bool QueriesOnly { get; set; }

    /// <summary>
    /// The requirement attributes this one already requires, so that one of them stacked beside it says nothing more:
    /// Tenancy's <c>[Operator]</c> lets only a signed-in user through, so it names <see cref="SignedInAttribute"/>,
    /// and <c>[SignedIn]</c> beside it is DDD00084, a warning. Empty by default.
    /// </summary>
    public Type[] Implies { get; set; } = [];
}
