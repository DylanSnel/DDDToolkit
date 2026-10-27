using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

/// <summary>
/// The tenant's roles: making them, renaming them, changing their permissions and archiving them, and reading them.
/// Your code takes it as <c>RoleManager</c>, a class the generator writes, closed over your classes and ids, and the
/// container makes per scope.
/// <para>
/// Each change of a role takes the role, or its id. Both check the same: the manager loads the role itself and asks the
/// caller's rights over what it loaded, never over the state of a role it was handed. Each returns the role as it
/// changed it, the one the unit of work keeps.
/// </para>
/// <para>
/// Each change needs <see cref="TenancyPermissions.RolesManage"/> for the whole tenant. Following the starter roles the roles
/// were made from, once the application changed them, is system work's alone (<see cref="FollowStarterRolesAsync"/>). A holder
/// of that permission may put any live permission into any role, permissions it does not hold included. That is on
/// purpose: it is the administrator's permission, and an administrator can give themselves anything already. One
/// exception: an administrator alone, holding it at the root with no end, changes which permissions that manage
/// access a role holds, by adding one, taking one out or archiving a role that holds one. A role that gains
/// one gives it to every seat that holds the role, seats that gave themselves the role included, and a role
/// that loses one takes it from every holder at once, so someone holding the permission for a week neither gives
/// lasting power over access that way nor takes away what it could not revoke grant by grant. A change of a
/// role's permissions reaches every seat that holds the role at the next save. Whether a change leaves the tenant
/// an administrator is decided before the role changes, so a refused command leaves nothing for a later save
/// to write.
/// </para>
/// <para>
/// That exception is containment, and goes when the application turns containment off
/// (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>): then the role permission for the whole tenant is all
/// any change of a role takes. The administrator a tenant keeps stays either way.
/// </para>
/// <para>
/// The reads answer roles as the application's own class, whole, and read the roles of the caller's tenant: a seat of
/// it, or system work in it, asks no permission. Whether a role manages access is <see cref="ManagesAccess"/>.
/// </para>
/// </summary>
/// <param name="store">Where roles are loaded and saved.</param>
/// <param name="catalogue">The permissions a role may hold.</param>
/// <param name="clock">What "now" is.</param>
public class RoleManager<
    [TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant,
    [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
    [TemplateType(typeof(OrganizationAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TOrganization,
    [TemplateType(typeof(OrganizationUnitAttribute<>), Take = TemplateArgumentKind.Type)] TUnit,
    [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
    [TemplateType(typeof(SeatAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TSeat,
    [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
    [TemplateType(typeof(RoleAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TRole,
    [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId>(
    ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> store,
    TenancyCatalogue catalogue,
    TimeProvider clock)
    where TTenant : TenantAggregate<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
{
    /// <summary>How many ids one question by id takes.</summary>
    public const int MostIds = TenancyReads.MostIds;

    // ------------------------------------------------------------------ commands

    /// <summary>Makes a role by hand; it comes from no starter role.</summary>
    /// <param name="name">Its name, unique in the tenant, ignoring case.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="permissions">The permissions it grants; the permissions they imply are added.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="id">
    /// Its id, for imports and seeding; a new one otherwise. An id that is already a role's is a mistake in
    /// the import, which the save reports.
    /// </param>
    /// <returns>The new role.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.RolesManage"/> for the whole tenant,
    /// <c>tenancy.role-name-taken</c>, <c>tenancy.name-invalid</c>, <c>tenancy.unknown-permission</c>.
    /// </exception>
    /// <example>
    /// <code>
    /// var planner = await roles.CreateAsync("Planner", "Opens and edits projects", ["projects.open", "projects.edit"], cancellationToken);
    /// </code>
    /// </example>
    public async Task<TRole> CreateAsync(
        string name,
        string description,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken,
        TRoleId? id = null)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.RequireAtTenantAsync(TenancyPermissions.RolesManage, cancellationToken).ConfigureAwait(false);
        await RequireNameFreeAsync(tenantId, name, except: null, cancellationToken).ConfigureAwait(false);

        var role = TenancyInstances.NewRole<TRole, TRoleId, TTenantId, TSeatId>(
            id ?? TRoleId.Create(), tenantId, new RoleDraft(name, description, permissions), catalogue, gate.By);
        store.Add(role);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return role;
    }

    /// <summary>Renames a role and describes it again.</summary>
    /// <param name="role">The role.</param>
    /// <param name="name">Its new name, unique in the tenant, ignoring case.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The role, renamed.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.RolesManage"/> for the whole tenant,
    /// <c>tenancy.role-not-found</c>, <c>tenancy.role-name-taken</c>, <c>tenancy.role-archived</c>,
    /// <c>tenancy.name-invalid</c>.
    /// </exception>
    /// <example>
    /// <code>
    /// var role = await roles.RenameAsync(command.Role, command.Name, command.Description, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TRole> RenameAsync(TRoleId role, string name, string description, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.RequireAtTenantAsync(TenancyPermissions.RolesManage, cancellationToken).ConfigureAwait(false);

        var renamed = await gate.LoadRoleAsync(role, cancellationToken).ConfigureAwait(false);
        await RequireNameFreeAsync(tenantId, name, except: role, cancellationToken).ConfigureAwait(false);
        renamed.Rename(name, description, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return renamed;
    }

    /// <summary>Renames a role and describes it again, as <see cref="RenameAsync(TRoleId, string, string, CancellationToken)"/> does with its id.</summary>
    /// <inheritdoc cref="RenameAsync(TRoleId, string, string, CancellationToken)"/>
    /// <example>
    /// <code>
    /// planner = await roles.RenameAsync(planner, "Project planner", "Opens and edits projects", cancellationToken);
    /// </code>
    /// </example>
    public Task<TRole> RenameAsync(TRole role, string name, string description, CancellationToken cancellationToken)
        => RenameAsync(IdOf(role), name, description, cancellationToken);

    /// <summary>
    /// Sets the permissions a role grants, for every seat that holds it. While containment is on
    /// (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>), a seat that adds or takes out a permission that
    /// manages access must be an administrator: hold <see cref="TenancyPermissions.AdministratorPermission"/> at the root
    /// with no end.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="permissions">The permissions it grants from now on; the permissions they imply are added.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The role, with its new permissions.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.RolesManage"/> for the whole tenant,
    /// <c>tenancy.role-not-found</c>, <c>tenancy.role-archived</c>, <c>tenancy.unknown-permission</c>,
    /// <c>tenancy.grant-exceeds-own</c> when a seat that is not an administrator adds or takes out a permission that
    /// manages access while containment is on, naming <see cref="TenancyPermissions.AdministratorPermission"/>, and
    /// <c>tenancy.last-admin</c> when
    /// the last administrators' role would lose the administrator permission.
    /// </exception>
    /// <example>
    /// <code>
    /// var role = await roles.SetPermissionsAsync(command.Role, command.Permissions, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TRole> SetPermissionsAsync(TRoleId role, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
        await gate.RequireAtTenantAsync(TenancyPermissions.RolesManage, cancellationToken).ConfigureAwait(false);

        var changed = await gate.LoadRoleAsync(role, cancellationToken).ConfigureAwait(false);
        var next = changed.PermissionsAfterSetting(permissions, catalogue);
        if (next.Except(changed.Permissions, StringComparer.Ordinal).Any(catalogue.ManagesAccess)
            || changed.Permissions.Except(next, StringComparer.Ordinal).Any(catalogue.ManagesAccess))
        {
            await gate.RequireAdministratorAsync(tenantId, role, cancellationToken).ConfigureAwait(false);
        }

        if (!next.Contains(TenancyPermissions.AdministratorPermission, StringComparer.Ordinal))
        {
            await gate.EnsureAdministratorRemainsAsync(tenantId, AdministratorLoss<TSeatId, TUnitId, TRoleId>.OfRole(role), cancellationToken).ConfigureAwait(false);
        }

        changed.SetPermissions(permissions, catalogue, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return changed;
    }

    /// <summary>Sets the permissions a role grants, as <see cref="SetPermissionsAsync(TRoleId, IReadOnlyCollection{string}, CancellationToken)"/> does with its id.</summary>
    /// <inheritdoc cref="SetPermissionsAsync(TRoleId, IReadOnlyCollection{string}, CancellationToken)"/>
    /// <example>
    /// <code>
    /// planner = await roles.SetPermissionsAsync(planner, ["projects.open", "projects.edit", "projects.close"], cancellationToken);
    /// </code>
    /// </example>
    public Task<TRole> SetPermissionsAsync(TRole role, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
        => SetPermissionsAsync(IdOf(role), permissions, cancellationToken);

    /// <summary>
    /// Archives a role: it stays where it was granted, and grants nothing from now on. While containment is on
    /// (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>), a seat that archives a role that manages
    /// access must be an administrator: hold <see cref="TenancyPermissions.AdministratorPermission"/> at the root with no end.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The role, archived.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.RolesManage"/> for the whole tenant,
    /// <c>tenancy.role-not-found</c>, <c>tenancy.role-archived</c>, <c>tenancy.grant-exceeds-own</c> when a
    /// seat that is not an administrator archives a role that manages access while containment is on, naming
    /// <see cref="TenancyPermissions.AdministratorPermission"/>, and <c>tenancy.last-admin</c>.
    /// </exception>
    /// <example>
    /// <code>
    /// var role = await roles.ArchiveAsync(command.Role, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TRole> ArchiveAsync(TRoleId role, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);
        await gate.RequireAtTenantAsync(TenancyPermissions.RolesManage, cancellationToken).ConfigureAwait(false);

        var archived = await gate.LoadRoleAsync(role, cancellationToken).ConfigureAwait(false);
        if (catalogue.AccessManagingPermissionsOf(archived.Facts).Count > 0)
        {
            await gate.RequireAdministratorAsync(tenantId, role, cancellationToken).ConfigureAwait(false);
        }

        await gate.EnsureAdministratorRemainsAsync(tenantId, AdministratorLoss<TSeatId, TUnitId, TRoleId>.OfRole(role), cancellationToken).ConfigureAwait(false);
        archived.Archive(gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return archived;
    }

    /// <summary>Archives a role, as <see cref="ArchiveAsync(TRoleId, CancellationToken)"/> does with its id.</summary>
    /// <inheritdoc cref="ArchiveAsync(TRoleId, CancellationToken)"/>
    /// <example>
    /// <code>
    /// planner = await roles.ArchiveAsync(planner, cancellationToken);
    /// </code>
    /// </example>
    public Task<TRole> ArchiveAsync(TRole role, CancellationToken cancellationToken)
        => ArchiveAsync(IdOf(role), cancellationToken);

    /// <summary>
    /// Every active role of the caller's tenant that was made from a starter role follows that starter role, as the catalogue the
    /// application runs with builds it now (<see cref="RoleAggregate{TRoleId, TTenantId}.FollowStarterRole{TSeatId}"/>):
    /// a permission the starter role gained since is added, a permission it lost is taken out, and what the tenant changed itself stays.
    /// Each role whose permissions change raises <see cref="RoleFollowedItsStarterRole{TTenantId, TRoleId, TSeatId}"/>, and the
    /// change reaches every seat that holds the role at the save, the one save of the command.
    /// <para>
    /// System work in the tenant alone runs it, as <c>services.SyncStarterRoles()</c> does for every tenant once
    /// the host has started, through <see cref="IStarterRoleSync"/>; an operator's command marked <c>[Operator]</c> runs it for
    /// one tenant, as operator work there, and the events name the operator. No
    /// seat runs it: what a starter role holds is the application's decision, permissions that manage access included, and an
    /// administrator changes a role with <see cref="SetPermissionsAsync(TRoleId, IReadOnlyCollection{string}, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// It takes the tenant's access revision first, as every change of rights does, and saves only when a role
    /// changed: a second run in a row reads, and writes nothing. Two runs at the same time cannot both commit; the
    /// one that fails on the revision changes nothing when it runs again. The last-administrator rule holds for
    /// it as for anyone: a role whose following would take the administrator permission from the tenant's last
    /// administrators is left as it is, and named in the answer.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The roles that changed, the roles kept for an administrator, and the roles whose starter role is gone.</returns>
    /// <exception cref="Exceptions.RefusalException"><c>access.system-only</c> for a seat.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// using (access.BeginSystemIn(tenant))
    /// {
    ///     var followed = await roles.FollowStarterRolesAsync(cancellationToken);
    /// }
    /// </code>
    /// </example>
    public async Task<StarterRolesFollowed> FollowStarterRolesAsync(CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireSystemInTenant("A tenant's roles are made to follow their starter roles");
        await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var roles = (await store.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false))
            .Where(role => role.FromStarterRole is not null && role.Status == RoleStatus.Active)
            .OrderBy(role => role.FromStarterRole, StringComparer.Ordinal)
            .ToArray();

        // Everything is decided before a role changes, so a refusal leaves nothing for a later save to write. The
        // administrators as last saved are read once, and only when a role would lose the administrator permission: the
        // roles that lose it are counted together, so two of them cannot each leave the other's administrators.
        var following = new List<TRole>();
        var kept = new List<TRoleId>();
        var gone = new List<TRoleId>();
        var losing = new HashSet<TRoleId>();
        IReadOnlyList<(TSeatId Seat, TRoleId Role)>? administrators = null;
        foreach (var role in roles)
        {
            if (role.PermissionsAfterFollowing(catalogue) is not { } next)
            {
                gone.Add(role.Id);
                continue;
            }

            if (role.Holds(TenancyPermissions.AdministratorPermission) && !next.Permissions.Contains(TenancyPermissions.AdministratorPermission, StringComparer.Ordinal))
            {
                administrators ??= await store.AdministratorsAsync(tenantId, gate.Now, cancellationToken).ConfigureAwait(false);
                if (administrators.Count > 0 && administrators.All(pair => losing.Contains(pair.Role) || pair.Role.Equals(role.Id)))
                {
                    kept.Add(role.Id);
                    continue;
                }

                losing.Add(role.Id);
            }

            following.Add(role);
        }

        // A role whose permissions changed raised its event. One that saves only what it remembers of its starter role, or its permissions
        // in the order a role keeps them, is saved without one: the same permissions are no change to tell anyone about.
        var changed = new List<TRoleId>();
        var saves = false;
        foreach (var role in following)
        {
            var before = role.Permissions.ToHashSet(StringComparer.Ordinal);
            if (role.FollowStarterRole(catalogue, gate.By))
            {
                saves = true;
                if (!before.SetEquals(role.Permissions))
                {
                    changed.Add(role.Id);
                }
            }
        }

        if (saves)
        {
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        return new StarterRolesFollowed(changed, kept, gone);
    }

    // ------------------------------------------------------------------ reads

    /// <summary>The tenant's roles, the application's own class, the active ones first, each by name.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The roles, archived ones included.</returns>
    /// <exception cref="Exceptions.RefusalException">The caller is nobody.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// var active = (await roles.ListAsync(cancellationToken)).Where(role =&gt; role.Status == RoleStatus.Active);
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<TRole>> ListAsync(CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();

        return ActiveFirst(await store.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The roles among <paramref name="ids"/> that are the caller's tenant's, the application's own class, the active
    /// ones first, each by name. An id of another tenant, or of no role, is left out without a word.
    /// </summary>
    /// <param name="ids">The roles asked about, at most <see cref="MostIds"/> different ones.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The roles found.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller is nobody, with its own code; or <c>tenancy.too-many-ids</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// The names of the roles that grant <c>projects.edit</c>:
    /// <code>
    /// var ids = await db.Tenancy().RolesWithPermission("projects.edit").ToListAsync(cancellationToken);
    /// var names = (await roles.ByIdAsync(ids, cancellationToken)).Select(role =&gt; role.Name);
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<TRole>> ByIdAsync(IReadOnlyCollection<TRoleId> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var gate = Gate();
        var tenantId = gate.RequireTenant();
        var asked = TenancyReads.Asked(ids);
        if (asked.Count == 0)
        {
            return [];
        }

        // A tenant has few roles, so they are read in one statement and the ones asked for picked from them.
        var roles = await store.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return ActiveFirst(roles.Where(role => asked.Contains(role.Id)));
    }

    /// <summary>
    /// Whether a role manages access: whether the catalogue marks one of its live permissions as managing access, which an
    /// archived role never does. While containment is on (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>),
    /// such a role is given only by a seat that holds those permissions, rather than by whoever holds
    /// <c>tenancy.grants.manage</c> where the seat is placed. Once the application turns it off, it is given as any other
    /// role, and this still says it manages access. It reads nothing.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns><see langword="true"/> when one of the role's live permissions manages access.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="role"/> is null.</exception>
    /// <example>
    /// A role list that marks the roles only some seats may give:
    /// <code>
    /// var list = (await roles.ListAsync(cancellationToken)).Select(role =&gt; new { role.Id, role.Name, Restricted = roles.ManagesAccess(role) });
    /// </code>
    /// </example>
    public bool ManagesAccess(TRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return catalogue.AccessManagingPermissionsOf(role.Facts).Count > 0;
    }

    // ------------------------------------------------------------------ the records it answers

    /// <summary>
    /// What following the starter roles did in one tenant (<see cref="FollowStarterRolesAsync"/>). A role that follows
    /// its starter role already, one made by hand and one that is archived are in none of the lists.
    /// </summary>
    /// <param name="Changed">
    /// The roles whose permissions changed, each with a <see cref="RoleFollowedItsStarterRole{TTenantId, TRoleId, TSeatId}"/> raised.
    /// </param>
    /// <param name="KeptForAnAdministrator">
    /// The roles left as they were, permissions and record, because following their starter role would take the administrator permission
    /// from the tenant's last administrators. Each follows at a later run, once the tenant has an administrator
    /// through another role.
    /// </param>
    /// <param name="WithoutTheirStarterRole">
    /// The roles made from a starter role the catalogue no longer has. They keep their permissions, and follow the starter role again if
    /// the application declares it again under the same permission.
    /// </param>
    public sealed record StarterRolesFollowed(
        IReadOnlyList<TRoleId> Changed,
        IReadOnlyList<TRoleId> KeptForAnAdministrator,
        IReadOnlyList<TRoleId> WithoutTheirStarterRole);

    // ------------------------------------------------------------------ helpers

    /// <summary>What every command asks before it acts, for one call: the caller and "now" are fixed when it starts.</summary>
    private TenancyGate<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> Gate() => new(store, catalogue, clock);

    private async Task RequireNameFreeAsync(TTenantId tenant, string name, TRoleId? except, CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length > 0 && await store.RoleNameTakenAsync(tenant, trimmed, except, cancellationToken).ConfigureAwait(false))
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.RoleNameTaken, ("Name", trimmed));
        }
    }

    private static TRole[] ActiveFirst(IEnumerable<TRole> roles)
        => [.. roles
            .OrderBy(role => role.Status != RoleStatus.Active)
            .ThenBy(role => role.Name, StringComparer.OrdinalIgnoreCase)];

    private static TRoleId IdOf(TRole role) => (role ?? throw new ArgumentNullException(nameof(role))).Id;
}
