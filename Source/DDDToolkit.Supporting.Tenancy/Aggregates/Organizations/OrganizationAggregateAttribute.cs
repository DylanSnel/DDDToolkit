using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Declares the application's organization class. The generator derives it from
/// <see cref="OrganizationAggregate{TTenantId, TUnit, TUnitId}"/>, closed over the tenant id given here and
/// the application's unit class and unit id, which it takes from the class declared with
/// <see cref="OrganizationUnitAttribute{TUnitId}"/>:
/// <code>
/// [OrganizationAggregate&lt;TenantId&gt;]
/// public sealed partial class ShopOrganization;
/// </code>
/// </summary>
/// <typeparam name="TTenantId">The application's tenant id: an organization shares its tenant's id.</typeparam>
[AggregateRootTemplate(typeof(OrganizationAggregate<,,>))]
[TemplateArgument(1, typeof(OrganizationUnitAttribute<>), Take = TemplateArgumentKind.Type)]
[TemplateArgument(2, typeof(OrganizationUnitAttribute<>))]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OrganizationAggregateAttribute<TTenantId> : Attribute;
