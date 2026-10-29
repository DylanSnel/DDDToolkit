using System.Globalization;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.Managers;

/// <summary>
/// What a new tenant is provisioned with, for <c>TenantManager.ProvisionAsync</c>. Your code makes it as
/// <c>TenantToProvision</c>, a record the generator writes, closed over your classes and ids:
/// <code>
/// var tenant = await tenants.ProvisionAsync(new TenantToProvision(
///     Slug: command.Slug,
///     Name: command.Name,
///     Shape: TenantShape.Hierarchical,
///     RootName: command.Name,
///     AdminIdentity: admin), cancellationToken);
/// </code>
/// Imports and seeding that need fixed ids pass them in: <paramref name="TenantId"/>, <paramref name="RootId"/>,
/// <paramref name="AdminSeatId"/> and <paramref name="RoleIds"/>.
/// </summary>
/// <param name="Slug">The slug it is selected by; trimmed and lowercased.</param>
/// <param name="Name">The tenant's name, which is its organization's.</param>
/// <param name="Shape">Whether its organization is flat or a tree.</param>
/// <param name="RootName">The root unit's name.</param>
/// <param name="AdminIdentity">
/// The verified identity of the first administrator. What their seat is shown by is the application's, set in
/// <paramref name="ConfigureFirstSeat"/> when its seat class keeps a name.
/// </param>
/// <param name="TenantId">The tenant's id, for imports and seeding; a new one otherwise.</param>
/// <param name="RootId">The root's id, for imports and seeding; a new one otherwise.</param>
/// <param name="AdminSeatId">The first seat's id, for imports and seeding; a new one otherwise.</param>
/// <param name="RoleIds">The ids of the roles copied from the starter roles, by starter role key; the others get new ids.</param>
/// <param name="Language">
/// The language the tenant's roles are named in: each starter role's name and description are asked of the
/// application's <see cref="IStarterRoleTexts"/> in it. <see langword="null"/>, or an application that
/// registered no texts, keeps the catalogue's own; the default administrators' starter role, which the package adds,
/// falls back to the package's own texts in it, English or Dutch. The package keeps no language itself:
/// where the tenant remembers its language is a field of the application's tenant class, set in
/// <paramref name="ConfigureTenant"/>.
/// </param>
/// <param name="ConfigureTenant">
/// Sets the fields the application added to its tenant class, on the new tenant before it is activated, so
/// they are saved in the same transaction and a domain event the class raises there goes out with the
/// provisioning. When it throws, nothing is saved.
/// </param>
/// <param name="ConfigureRoot">
/// The same for the fields the application added to its unit class, on the root: what kind of unit it is,
/// say, when the class keeps one. A unit raises no events of its own; the organization raises the root's.
/// </param>
/// <param name="ConfigureFirstSeat">
/// The same for the first administrator's seat, which is placed at the root and holds the administrators'
/// role by then: the name it is shown by, say, when the class keeps one.
/// </param>
public record TenantToProvision<
    [TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant,
    [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
    [TemplateType(typeof(OrganizationUnitAttribute<>), Take = TemplateArgumentKind.Type)] TUnit,
    [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
    [TemplateType(typeof(SeatAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TSeat,
    [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
    [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId>(
    string Slug,
    string Name,
    TenantShape Shape,
    string RootName,
    Guid AdminIdentity,
    TTenantId? TenantId = null,
    TUnitId? RootId = null,
    TSeatId? AdminSeatId = null,
    IReadOnlyDictionary<string, TRoleId>? RoleIds = null,
    CultureInfo? Language = null,
    Action<TTenant>? ConfigureTenant = null,
    Action<TUnit>? ConfigureRoot = null,
    Action<TSeat>? ConfigureFirstSeat = null)
    where TTenant : TenantAggregate<TTenantId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>;
