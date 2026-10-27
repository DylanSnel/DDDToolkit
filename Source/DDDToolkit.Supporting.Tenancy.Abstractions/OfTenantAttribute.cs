using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Marks the property of an aggregate root that says which tenant it belongs to. It is a fact about what the aggregate
/// is, said in the domain beside its <c>[Owner]</c> and its <c>[At]</c>. It is no rule about who may do what with it:
/// <code>
/// [AggregateRoot&lt;ProjectId&gt;]
/// public sealed partial class Project
/// {
///     [OfTenant] public TenantId TenantId { get; private set; }
/// }
/// </code>
/// Tenancy's Entity Framework package maps it through <c>AddDDDToolkitConventions()</c>. Every context that maps the
/// class keeps it to the current caller's tenant, with the tenant filter on its reads and Tenancy's save check on its
/// writes, as <c>ScopeToTenant(row =&gt; row.TenantId)</c> does. The context writes no line for it.
/// </summary>
/// <remarks>
/// The property is of the application's tenant id, never nullable, since a row of no tenant is kept to none. A class has
/// one, on the root of a hierarchy the context stores. On a type the context owns it marks nothing, since an owned row is
/// kept to its tenant through its owner. A model that breaks one of these is refused while it is built.
/// <para>
/// A context that calls <c>ScopeToTenant</c> on the class keeps that mapping instead, for a tenant held in another
/// property or a column of its own. Without Tenancy's Entity Framework package in the application, a model with the
/// marker is refused while it is built.
/// </para>
/// <para>
/// The marker is in a package of its own, with no Entity Framework and nothing else of Tenancy's. A domain project that
/// sees only Tenancy's ids references this package. Tenancy's own package references it, so a project with Tenancy has
/// the marker as well.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
[ModelConvention("DDDToolkit.Supporting.Tenancy.EntityFramework.OfTenantConvention, DDDToolkit.Supporting.Tenancy.EntityFramework")]
public sealed class OfTenantAttribute : Attribute;
