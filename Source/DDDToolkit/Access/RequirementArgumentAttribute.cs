namespace DDDToolkit.Access;

/// <summary>
/// Makes an attribute a package ships a marker of one more argument of the call a requirement attribute stands for
/// (<see cref="StatesRequirementAttribute"/>). The marker goes on a member of a request, beside that attribute. Tenancy
/// declares the tenant an operator's command acts in this way:
/// <code>
/// [RequirementArgument(typeof(OperatorAttribute), IdOf = typeof(TenantAggregateAttribute&lt;&gt;))]
/// [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
/// public sealed class ForTenantAttribute : Attribute;
/// </code>
/// so <c>[Operator]</c> on a request with <c>[ForTenant] TenantId Tenant</c> is <c>TenancyAccess.Operator(request.Tenant)</c>.
/// </summary>
/// <remarks>
/// The generator hands the marked member to the call after the attribute's constructor arguments, and before the
/// properties set where the request carries the attribute. A marker goes on a positional parameter of a record, which is
/// its property, or on a property, so declare it for both. A request that carries the marker without its attribute is
/// DDD00102, and so is one that marks two members with it. A member of another type than <see cref="IdOf"/> says is
/// DDD00081.
/// </remarks>
/// <param name="requirement">The requirement attribute whose call the argument goes to: <c>typeof(OperatorAttribute)</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequirementArgumentAttribute(Type requirement) : Attribute
{
    /// <summary>The requirement attribute whose call the argument goes to.</summary>
    public Type Requirement { get; } = requirement;

    /// <summary>
    /// The template whose classes' ids the marked member may be, as <c>[TemplateType]</c> names one: the open generic
    /// attribute an application declares the class with, <c>typeof(TenantAggregateAttribute&lt;&gt;)</c>. A member of
    /// another type is DDD00081. Where the project sees no class declared with the template, nothing is said. Null to take
    /// any type the method takes.
    /// </summary>
    public Type? IdOf { get; set; }
}
