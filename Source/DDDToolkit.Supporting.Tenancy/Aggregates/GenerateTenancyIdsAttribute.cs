using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Has the generator write the ids of Tenancy's classes that this project does not declare, and no classes: for the
/// contracts project of a module split by layer, which publishes the ids to the other modules and holds no aggregates.
/// <code>
/// [assembly: GenerateTenancyIds]
/// </code>
/// The project then has a <c>TenantId</c>, an <c>OrganizationUnitId</c>, a <c>RoleId</c> and a <c>SeatId</c>, each an
/// <c>[EntityId&lt;Guid&gt;]</c> published with <c>[ModuleContract]</c>, public, in its root namespace, with everything
/// an id declared by hand gets. The module's domain project says <see cref="GenerateTenancyClassesAttribute"/>, and
/// takes these. An id this project declares itself wins: it is not written.
/// <para>
/// It is an attribute apart, rather than an option of <see cref="GenerateTenancyClassesAttribute"/>, so each project
/// says in its own words what it gets, and a contracts project cannot be handed aggregates by a word left out.
/// </para>
/// </summary>
[TemplateDefaults(
    typeof(TenantAggregateAttribute<>),
    typeof(OrganizationAggregateAttribute<>),
    typeof(OrganizationUnitAttribute<>),
    typeof(RoleAggregateAttribute<>),
    typeof(SeatAggregateAttribute<>),
    IdsOnly = true)]
[AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
public sealed class GenerateTenancyIdsAttribute : Attribute;
