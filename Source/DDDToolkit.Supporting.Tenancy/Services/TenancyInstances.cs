using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Makes new instances of the application's own Tenancy classes, which the package cannot name, and puts
/// them in their first state with their creation event raised. The use cases create through these, and so
/// can tests, seeding and imports.
/// <para>
/// There are no factories to write. Each class is made through the parameterless constructor the generator
/// writes on every class declared with a template, and the parent does what a constructor would. A field
/// the application added starts at its default; set it in a method of the class after creation, or in a
/// handler of the creation event.
/// </para>
/// <para>
/// Each takes who makes the instance, <c>by</c>, for its creation event: the use cases pass the actor of their
/// caller, and seeding or an import that names nobody leaves it out.
/// </para>
/// <code>
/// var seat = TenancyInstances.NewSeat&lt;ShopSeat, SeatId, TenantId, OrganizationUnitId, RoleId&gt;(
///     SeatId.CreateSequential(), tenantId, identity, "Ada");
/// </code>
/// </summary>
public static class TenancyInstances
{
    /// <summary>A new tenant, being provisioned, with <see cref="TenantProvisioned{TTenantId, TSeatId}"/> raised.</summary>
    /// <typeparam name="TTenant">The application's tenant class.</typeparam>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <typeparam name="TSeatId">The application's seat id, which the actor on the creation event is closed over.</typeparam>
    /// <param name="id">Its id.</param>
    /// <param name="slug">The slug it is selected by.</param>
    /// <param name="shape">Whether its organization is flat or a tree.</param>
    /// <param name="by">Who makes it, for its creation event; <see langword="null"/> when nobody is named.</param>
    public static TTenant NewTenant<TTenant, TTenantId, TSeatId>(TTenantId id, ValidTenantSlug slug, TenantShape shape, TenancyActor<TSeatId>? by = null)
        where TTenant : TenantAggregate<TTenantId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        var tenant = HostInstances<TTenant>.New();
        tenant.InitializeNew(id, slug, shape, by);
        return tenant;
    }

    /// <summary>
    /// A new organization with its root, through the application's own unit class, and
    /// <see cref="OrganizationUnitAdded{TTenantId, TUnitId, TSeatId}"/> raised for the root. A field the application
    /// added to its unit class is set on <c>organization.Root</c> afterwards.
    /// </summary>
    /// <typeparam name="TOrganization">The application's organization class.</typeparam>
    /// <typeparam name="TTenantId">The application's tenant id, which the organization shares.</typeparam>
    /// <typeparam name="TUnit">The application's unit class.</typeparam>
    /// <typeparam name="TUnitId">The application's unit id.</typeparam>
    /// <typeparam name="TSeatId">The application's seat id, which the actor on the creation event is closed over.</typeparam>
    /// <param name="id">Its tenant's id.</param>
    /// <param name="name">The tenant's name.</param>
    /// <param name="rootId">The root's id.</param>
    /// <param name="rootName">The root's name.</param>
    /// <param name="by">Who makes it, for its creation event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.name-invalid</c>, for the tenant's name or the root's.</exception>
    public static TOrganization NewOrganization<TOrganization, TTenantId, TUnit, TUnitId, TSeatId>(
        TTenantId id, string name, TUnitId rootId, string rootName, TenancyActor<TSeatId>? by = null)
        where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnit : OrganizationUnitEntity<TUnitId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        var organization = HostInstances<TOrganization>.New();
        organization.InitializeNew(id, name, rootId, rootName, by);
        return organization;
    }

    /// <summary>A new active seat, placed nowhere yet, with <see cref="SeatAdded{TTenantId, TSeatId}"/> raised.</summary>
    /// <typeparam name="TSeat">The application's seat class.</typeparam>
    /// <typeparam name="TSeatId">The application's seat id.</typeparam>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <typeparam name="TUnitId">The application's unit id.</typeparam>
    /// <typeparam name="TRoleId">The application's role id.</typeparam>
    /// <param name="id">Its id.</param>
    /// <param name="tenantId">The tenant it is in.</param>
    /// <param name="identity">The verified identity it belongs to: the subject of the person's token.</param>
    /// <param name="displayName">The name it is shown by.</param>
    /// <param name="by">Who makes it, for its creation event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.identity-required</c> or <c>tenancy.name-invalid</c>.</exception>
    public static TSeat NewSeat<TSeat, TSeatId, TTenantId, TUnitId, TRoleId>(
        TSeatId id, TTenantId tenantId, Guid identity, string displayName, TenancyActor<TSeatId>? by = null)
        where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        var seat = HostInstances<TSeat>.New();
        seat.InitializeNew(id, tenantId, identity, displayName, by);
        return seat;
    }

    /// <summary>A new active role, its keys expanded from the catalogue, with <see cref="RoleCreated{TTenantId, TRoleId, TSeatId}"/> raised.</summary>
    /// <typeparam name="TRole">The application's role class.</typeparam>
    /// <typeparam name="TRoleId">The application's role id.</typeparam>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <typeparam name="TSeatId">The application's seat id, which the actor on the creation event is closed over.</typeparam>
    /// <param name="id">Its id.</param>
    /// <param name="tenantId">The tenant it belongs to.</param>
    /// <param name="draft">Its name, description, keys and the pack it comes from.</param>
    /// <param name="catalogue">The catalogue its keys come from.</param>
    /// <param name="by">Who makes it, for its creation event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.name-invalid</c> or <c>tenancy.unknown-permission</c>.</exception>
    public static TRole NewRole<TRole, TRoleId, TTenantId, TSeatId>(
        TRoleId id, TTenantId tenantId, RoleDraft draft, TenancyCatalogue catalogue, TenancyActor<TSeatId>? by = null)
        where TRole : RoleAggregate<TRoleId, TTenantId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        var role = HostInstances<TRole>.New();
        role.InitializeNew(id, tenantId, draft, catalogue, by);
        return role;
    }

    /// <summary>
    /// A new open invitation, with
    /// <see cref="InvitationIssued{TTenantId, TInvitationId, TUnitId, TRoleId, TSeatId}"/> raised. It has no token:
    /// the use case that issues an invitation makes one and hands its digest to the store with the invitation, so
    /// an invitation made here and saved by hand is one nobody can accept.
    /// </summary>
    /// <typeparam name="TInvitation">The application's invitation class.</typeparam>
    /// <typeparam name="TInvitationId">The application's invitation id.</typeparam>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <typeparam name="TUnitId">The application's unit id.</typeparam>
    /// <typeparam name="TRoleId">The application's role id.</typeparam>
    /// <typeparam name="TSeatId">The application's seat id.</typeparam>
    /// <param name="id">Its id.</param>
    /// <param name="tenantId">The tenant it is into.</param>
    /// <param name="address">The address it is for.</param>
    /// <param name="unitId">The unit the seat is placed in.</param>
    /// <param name="roleId">The role the seat is granted there.</param>
    /// <param name="grantUntil">When that grant ends, later than <paramref name="expiresAt"/>, or <see langword="null"/> for no end.</param>
    /// <param name="displayName">A name suggested for the seat, or <see langword="null"/>.</param>
    /// <param name="issuedAt">When it is issued.</param>
    /// <param name="expiresAt">The first moment it can no longer be accepted.</param>
    /// <param name="issuedBy">The seat that issues it, or the seat system work issues it for; <see langword="null"/> for none.</param>
    /// <param name="issuedAsSystem">Whether system work issues it, which is then not held to a seat's rights when it is accepted.</param>
    /// <param name="by">Who makes it, for its creation event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.address-invalid</c>, <c>tenancy.name-invalid</c> or <c>tenancy.invitation-grant-ends-first</c>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="expiresAt"/> is not after <paramref name="issuedAt"/>.</exception>
    public static TInvitation NewInvitation<TInvitation, TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>(
        TInvitationId id,
        TTenantId tenantId,
        string address,
        TUnitId unitId,
        TRoleId roleId,
        DateTimeOffset? grantUntil,
        string? displayName,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        TSeatId? issuedBy = null,
        bool issuedAsSystem = false,
        TenancyActor<TSeatId>? by = null)
        where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
        where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        var invitation = HostInstances<TInvitation>.New();
        invitation.InitializeNew(id, tenantId, address, unitId, roleId, grantUntil, displayName, issuedAt, expiresAt, issuedBy, issuedAsSystem, by);
        return invitation;
    }
}
