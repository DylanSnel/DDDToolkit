using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

/// <summary>
/// A tenant's life and settings: provisioning it with everything it needs to be used, changing its shape, and
/// suspending, reactivating or closing it; and, for the application's operators, the tenants there are. Your code takes
/// it as <c>TenantManager</c>, a class the generator writes, closed over your classes and ids, and the container makes
/// per scope. Each command returns the tenant as it changed it, the one the unit of work keeps.
/// <para>
/// Provisioning is the one thing system work outside any tenant does. Suspending, reactivating and closing
/// are system work inside the tenant, and act on that tenant: whoever carries them out begins
/// <c>access.BeginSystemIn(tenant)</c> first, or sends a command marked <c>[Operator]</c> with the tenant on a
/// <c>[ForTenant]</c> member, which runs as operator work in that tenant, so acting on another tenant cannot be expressed.
/// </para>
/// <para>
/// The organization's name is the tenant's name, and <c>OrganizationManager.RenameAsync</c> changes it.
/// </para>
/// </summary>
/// <param name="store">Where the tenant is loaded and saved.</param>
/// <param name="catalogue">The starter roles a new tenant is given.</param>
/// <param name="clock">What "now" is.</param>
/// <param name="options">Which token roles are operators', for the tenants an operator lists.</param>
/// <param name="callers">Who is calling, as the toolkit says: the operator that lists the tenants.</param>
/// <param name="starterRoleTexts">
/// The starter roles' names and descriptions in a tenant's language, when the application registered some;
/// otherwise every role made from a starter role gets the catalogue's texts, but the role of the default
/// administrators' starter role, which the package adds, gets the package's own texts in that language, English or
/// Dutch.
/// </param>
public class TenantManager<
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
    TimeProvider clock,
    TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> options,
    ICallerAccessor callers,
    IStarterRoleTexts? starterRoleTexts = null)
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
    /// <summary>How many tenants a page of <see cref="ListAsync"/> holds at most.</summary>
    public const int MostPerPage = 200;

    /// <summary>The reason recorded on the grant that makes the first seat an administrator.</summary>
    private const string ProvisionedReason = "first administrator";

    // ------------------------------------------------------------------ commands

    /// <summary>
    /// Provisions a tenant, in one save: the tenant, its organization with the root, a role copied from
    /// every starter role seeded for its shape, and a first seat for the administrator's identity, placed at the root and
    /// granted the administrators' role there with no end. That role holds what the administrators' starter role seeded for
    /// the shape holds, which is every live permission unless the starter role lists its own. The tenant is active when the save
    /// is done.
    /// <para>
    /// The roles are named as the catalogue names its starter roles, or, with a language and an <see cref="IStarterRoleTexts"/>
    /// registered, in that language. The role of <see cref="TenancyStarterRoles.DefaultAdministrators"/>, for an application
    /// that declares no administrators' starter role, is named in that language by the application's texts, or else by the
    /// package's own, in English or Dutch. The fields the application added to its tenant, unit and seat classes are set
    /// by the command's callbacks, before the tenant is activated and in the same save.
    /// </para>
    /// <para>
    /// Only system work outside any tenant provisions one, and it does not do so with that power: once it
    /// knows the new tenant's id, everything else, the check that the slug is free and the save included,
    /// runs as system work inside the new tenant (<see cref="TenancyWork.BeginSystemIn{TTenantId, TSeatId}(TTenantId, TSeatId?, string)"/>),
    /// so it writes that tenant's rows and nothing else. On a database that keeps tenants apart by itself,
    /// such as Postgres with row level security, the slug check then sees no other tenant, and the unique
    /// index on the slug is what refuses one that is taken.
    /// </para>
    /// <para>
    /// The organization, the first seat and the roles belong to the tenant, and are read through the other managers by
    /// a request that names the new tenant. Imports and seeding that need fixed ids pass them in the command.
    /// </para>
    /// </summary>
    /// <param name="command">What to provision.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The new tenant, active.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>access.system-only</c> for a seat, <c>tenancy.slug-taken</c>, <c>tenancy.role-name-taken</c> when two
    /// starter roles are named alike in the tenant's language, and what the aggregates refuse: an invalid name, a starter role's
    /// translated one included, or an empty identity.
    /// </exception>
    /// <exception cref="Exceptions.InvalidValueObjectException">The slug does not follow <see cref="TenantSlug.Pattern"/>.</exception>
    /// <exception cref="ArgumentException">The command's role ids name a starter role the tenant is not given, or give one id twice.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work in a tenant rather than outside any.</exception>
    /// <example>
    /// An operator's command that names no tenant runs as operator work in no tenant, which provisioning needs. A
    /// sign-up form begins <c>access.BeginSystem()</c> around the call instead.
    /// <code>
    /// [Operator]
    /// public sealed record ProvisionTenant(string Slug, string Name, Guid Administrator) : ICommand&lt;Tenant&gt;, ITenancyRequest;
    ///
    /// // In its handler:
    /// return await tenants.ProvisionAsync(
    ///     new TenantToProvision(command.Slug, command.Name, TenantShape.Hierarchical, RootName: command.Name, AdminIdentity: command.Administrator),
    ///     cancellationToken);
    /// </code>
    /// </example>
    public async Task<TTenant> ProvisionAsync(
        TenantToProvision<TTenant, TTenantId, TUnit, TUnitId, TSeat, TSeatId, TRoleId> command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var gate = Gate();
        gate.RequireSystemOutsideTenants();

        var starterRoles = catalogue.StarterRolesFor(command.Shape).ToArray();
        RequireSeededStarterRoles(command.RoleIds, starterRoles, command.Shape);

        var slug = TenantSlug.Create(command.Slug).ToValid();
        var tenantId = command.TenantId ?? TTenantId.Create();

        // From here on the work acts in the tenant it makes and in no other: every read and the save. It is
        // still recorded as whoever began it: the system, or the operator the tenant is provisioned for, on
        // the save and on every event of the new tenant.
        var by = gate.By ?? TenancyActor<TSeatId>.OfSystem(TenancyWork.SystemScope);
        using (TenancyWork.BeginSystemInAs(tenantId, by))
        {
            return await ProvisionInAsync(command, tenantId, slug, starterRoles, gate.Now, by, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Changes a flat tenant into a hierarchical one, and gives it a copy of every starter role seeded for the new shape
    /// it has no copy of yet. No grant changes: who held what at the root still does. Where the two shapes have
    /// administrators' starter roles of their own, the tenant gets the role of the starter role seeded for the new shape with
    /// the permissions that starter role holds, and nobody is given it: the administrators keep the role they have, and give
    /// the new one as they see fit.
    /// </summary>
    /// <param name="to">The new shape.</param>
    /// <param name="roleIds">The ids of the new roles, by starter role key, for imports and seeding; the rest get new ids.</param>
    /// <param name="language">
    /// The language the new roles are named in, usually the tenant's, which the application keeps: each
    /// starter role's texts are asked of the application's <see cref="IStarterRoleTexts"/> in it. <see langword="null"/>,
    /// or an application that registered no texts, keeps the catalogue's own; the default administrators'
    /// starter role, which the package adds, gets the package's own texts in that language, English or Dutch, where
    /// the application's have none for it. The roles the tenant has already keep their names.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The tenant, in its new shape.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.SettingsManage"/> for the whole tenant,
    /// <c>tenancy.shape-change</c>, <c>tenancy.tenant-state</c> for a closed tenant,
    /// <c>tenancy.role-name-taken</c> when a role of the tenant, or another new role, already has a new
    /// role's name, <c>tenancy.name-invalid</c> for a starter role's translated name or description.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="roleIds"/> names a starter role of no role that would be made, or gives one id twice.</exception>
    /// <example>
    /// <code>
    /// var tenant = await tenants.ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TTenant> ChangeShapeAsync(
        TenantShape to,
        IReadOnlyDictionary<string, TRoleId>? roleIds,
        CultureInfo? language,
        CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.RequireAtTenantAsync(TenancyPermissions.SettingsManage, cancellationToken).ConfigureAwait(false);

        // Everything is decided before the tenant changes, so a refusal leaves nothing for a later save.
        var tenant = await gate.LoadTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        tenant.RequireShapeChange(to);

        var copied = (await store.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false))
            .Select(role => role.FromStarterRole)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var missing = catalogue.StarterRolesFor(to).Where(starterRole => !copied.Contains(starterRole.Key)).ToArray();
        RequireSeededStarterRoles(roleIds, missing, to);

        var added = new List<TRole>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var starterRole in missing)
        {
            var draft = DraftOf(starterRole, language);
            var name = draft.Name?.Trim() ?? string.Empty;
            // A blank name is the role's to refuse, as invalid, when it is made below.
            if (name.Length > 0
                && (!names.Add(name) || await store.RoleNameTakenAsync(tenantId, name, null, cancellationToken).ConfigureAwait(false)))
            {
                throw TenancyRefusals.Refuse(TenancyRefusals.RoleNameTaken, ("Name", name));
            }

            added.Add(NewRoleFrom(draft, tenantId, roleIds, catalogue, gate.By));
        }

        tenant.ChangeShape(to, gate.By);
        foreach (var role in added)
        {
            store.Add(role);
        }

        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return tenant;
    }

    /// <summary>Suspends the caller's tenant: system work in that tenant only.</summary>
    /// <param name="reason">Why; required.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The tenant, suspended.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>access.system-only</c> for a seat, <c>tenancy.tenant-state</c>, <c>tenancy.reason-required</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// An operator's command. <c>[Operator]</c> runs its handler as operator work in the tenant of the
    /// <c>[ForTenant]</c> member:
    /// <code>
    /// [Operator]
    /// public sealed record SuspendTenant([ForTenant] TenantId Tenant, string Reason) : ICommand&lt;Tenant&gt;, ITenancyRequest;
    ///
    /// // In its handler:
    /// return await tenants.SuspendAsync(command.Reason, cancellationToken);
    /// </code>
    /// </example>
    public Task<TTenant> SuspendAsync(string reason, CancellationToken cancellationToken)
        => ChangeStatusAsync((tenant, by) => tenant.Suspend(reason, by), cancellationToken);

    /// <summary>Puts the caller's suspended tenant back in use: system work in that tenant only.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The tenant, active again.</returns>
    /// <exception cref="Exceptions.RefusalException"><c>access.system-only</c> for a seat, <c>tenancy.tenant-state</c>.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// A job that puts a tenant back in use once its invoice is paid:
    /// <code>
    /// using (access.BeginSystemIn(invoice.Tenant))
    /// {
    ///     await tenants.ReactivateAsync(cancellationToken);
    /// }
    /// </code>
    /// </example>
    public Task<TTenant> ReactivateAsync(CancellationToken cancellationToken)
        => ChangeStatusAsync((tenant, by) => tenant.Reactivate(by), cancellationToken);

    /// <summary>Closes the caller's tenant for good: system work in that tenant only.</summary>
    /// <param name="reason">Why; required.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The tenant, closed.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>access.system-only</c> for a seat, <c>tenancy.tenant-state</c>, <c>tenancy.reason-required</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// In the handler of an operator's command marked <c>[Operator]</c>, with the tenant on a <c>[ForTenant]</c> member:
    /// <code>
    /// return await tenants.CloseAsync(command.Reason, cancellationToken);
    /// </code>
    /// </example>
    public Task<TTenant> CloseAsync(string reason, CancellationToken cancellationToken)
        => ChangeStatusAsync((tenant, by) => tenant.Close(reason, by), cancellationToken);

    // ------------------------------------------------------------------ reads

    /// <summary>
    /// A page of the tenants of the application, for its operators, by slug: those after the last tenant of the page
    /// before, or the first ones. Each comes with its name and how many of its seats are in use. It reads, and
    /// changes nothing.
    /// <para>
    /// Only an operator asks: a signed-in user whose token carries one of
    /// <see cref="TenancyOptions{TTenantId, TSeatId, TUnitId, TRoleId}.OperatorTokenRoles"/>. An operator holds no
    /// seat and acts in no tenant, so this is the one read that asks the toolkit's own caller rather than the
    /// Tenancy caller. It reads as that caller, never as the system: on a database that keeps tenants apart by
    /// itself, such as Postgres with row level security, the operator's own database role and its policies decide
    /// what it reads, and a host that mapped no such role reads nothing.
    /// </para>
    /// <para>
    /// The application's own work that has to visit every tenant, a sweep say, does not go through here: it asks
    /// which tenants there are in a scope of its own, <c>access.TenantsToSweepAsync(scope)</c>, and then works in each.
    /// </para>
    /// </summary>
    /// <param name="after">
    /// <see cref="TenantPage.Next"/> of the page before, or <see langword="null"/> for the first page.
    /// It is a marker the manager made, and means nothing to anyone else.
    /// </param>
    /// <param name="size">How many tenants the page holds: 1 to <see cref="MostPerPage"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The page: each tenant with its name and its number of active seats, and the marker of the next page, or
    /// <see langword="null"/> on the last page.
    /// </returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.operators-only</c> for anyone but an operator, checked before anything else, so nobody else
    /// learns what a page holds; <c>tenancy.page-size-invalid</c>, with <c>Max</c>; <c>tenancy.cursor-invalid</c>
    /// for a marker the manager did not make. Each of the last two names its input in the argument
    /// <c>Field</c>: <c>size</c> and <c>after</c>.
    /// </exception>
    /// <example>
    /// The handler of an operator's query marked <c>[Operator]</c>:
    /// <code>
    /// var page = await tenants.ListAsync(query.After, size: 50, cancellationToken);
    /// // page.Items: each listing's Tenant, Name and ActiveSeats; page.Next: the marker of the next page
    /// </code>
    /// </example>
    public async Task<TenantPage> ListAsync(string? after, int size, CancellationToken cancellationToken)
    {
        if (!options.IsOperator(callers.Current))
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.OperatorsOnly);
        }

        if (size is < 1 or > MostPerPage)
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.PageSizeInvalid, ("Max", MostPerPage));
        }

        var afterSlug = after is null ? null : PageMarker.Read(after) ?? throw TenancyRefusals.Refuse(TenancyRefusals.CursorInvalid);

        // One more than the page holds: whether there is a page after this one is known without counting.
        var found = await store.ListTenantsAsync(afterSlug, size + 1, cancellationToken).ConfigureAwait(false);
        var items = found.Count > size ? found.Take(size).ToArray() : [.. found];

        return new TenantPage(items, found.Count > size ? PageMarker.Write(items[^1].Tenant.Slug.Value) : null);
    }

    // ------------------------------------------------------------------ the records it answers

    /// <summary>
    /// A tenant as <see cref="ListAsync"/> lists it for an operator: the tenant, the application's own class, with
    /// what is not on it, its name and how many of its seats are in use. It names no person, and no row of the tenant's.
    /// </summary>
    /// <param name="Tenant">The tenant, the application's own class, read for the answer and tracked by nobody.</param>
    /// <param name="Name">Its name, which is its organization's.</param>
    /// <param name="ActiveSeats">How many of its seats are active now.</param>
    public sealed record TenantListing(TTenant Tenant, string Name, int ActiveSeats);

    /// <summary>One page of the tenants an operator lists.</summary>
    /// <param name="Items">The tenants of the page, by slug.</param>
    /// <param name="Next">What to ask the next page with, or <see langword="null"/> when this is the last one.</param>
    public sealed record TenantPage(IReadOnlyList<TenantListing> Items, string? Next);

    // ------------------------------------------------------------------ helpers

    /// <summary>Everything of <see cref="ProvisionAsync"/> after the new tenant's id is known, run as system work in that tenant.</summary>
    private async Task<TTenant> ProvisionInAsync(
        TenantToProvision<TTenant, TTenantId, TUnit, TUnitId, TSeat, TSeatId, TRoleId> command,
        TTenantId tenantId,
        ValidTenantSlug slug,
        StarterRole[] starterRoles,
        DateTimeOffset now,
        TenancyActor<TSeatId> by,
        CancellationToken cancellationToken)
    {
        if (await store.SlugTakenAsync(slug.Value, cancellationToken).ConfigureAwait(false))
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.SlugTaken, ("Slug", slug.Value));
        }

        var rootId = command.RootId ?? TUnitId.Create();
        var tenant = TenancyInstances.NewTenant<TTenant, TTenantId, TSeatId>(tenantId, slug, command.Shape, by);
        var organization = TenancyInstances.NewOrganization<TOrganization, TTenantId, TUnit, TUnitId, TSeatId>(
            tenantId, command.Name, rootId, command.RootName, by);

        // The catalogue keeps its starter roles' own names apart. Names in another language are the application's,
        // so they are held apart here, the way a role made by hand is held apart from the others.
        var roles = new Dictionary<string, TRole>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var starterRole in starterRoles)
        {
            var role = NewRoleFrom(DraftOf(starterRole, command.Language), tenantId, command.RoleIds, catalogue, by);
            if (!names.Add(role.Name))
            {
                throw TenancyRefusals.Refuse(TenancyRefusals.RoleNameTaken, ("Name", role.Name));
            }

            roles[starterRole.Key] = role;
        }

        var administrators = roles[catalogue.AdministratorStarterRoleFor(command.Shape).Key];
        var seatId = command.AdminSeatId ?? TSeatId.Create();
        var seat = TenancyInstances.NewSeat<TSeat, TSeatId, TTenantId, TUnitId, TRoleId>(seatId, tenantId, command.AdminIdentity, by);
        seat.Place(rootId, primary: true, now, placedBy: null, by);
        seat.Grant(rootId, administrators.Id, administrators.Facts, GrantPeriod.Open(now), grantedBy: null, ProvisionedReason, by);

        // The application's own fields, before anything is handed to the store: a callback that throws
        // leaves nothing to save, and what it sets is written with everything else.
        command.ConfigureTenant?.Invoke(tenant);
        command.ConfigureRoot?.Invoke(organization.Root);
        command.ConfigureFirstSeat?.Invoke(seat);
        tenant.Activate<TSeatId>(by);

        store.Add(tenant);
        store.Add(organization);
        foreach (var role in roles.Values)
        {
            store.Add(role);
        }

        store.Add(seat);
        store.AddAccessRevision(tenantId);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return tenant;
    }

    private async Task<TTenant> ChangeStatusAsync(Action<TTenant, TenancyActor<TSeatId>?> change, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireSystemInTenant("A tenant is suspended, reactivated or closed");

        var tenant = await gate.LoadTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        change(tenant, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return tenant;
    }

    /// <summary>What every command asks before it acts, for one call: the caller and "now" are fixed when it starts.</summary>
    private TenancyGate<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> Gate() => new(store, catalogue, clock);

    /// <summary>
    /// What a role copied from <paramref name="starterRole"/> starts as. It holds the permissions the catalogue built the
    /// starter role with, the administrators' role too: every live permission for a starter role that lists none, and for one that
    /// lists permissions those and no others. Its name and description are the application's texts for the starter role in
    /// <paramref name="language"/> when it has some, and the catalogue's otherwise. The default
    /// administrators' starter role, which the application did not declare, has the package's texts in between: a
    /// tenant in Dutch gets it in Dutch, as it gets the package's refusals.
    /// </summary>
    private RoleDraft DraftOf(StarterRole starterRole, CultureInfo? language)
        => language is not null && (starterRoleTexts?.For(starterRole, language) ?? PackageTextsOf(starterRole, language)) is { } texts
            ? new RoleDraft(texts.Name, texts.Description, starterRole.Permissions, starterRole.Key)
            : new RoleDraft(starterRole.Name, starterRole.Description, starterRole.Permissions, starterRole.Key);

    /// <summary>The package's texts for its own starter role, the default administrators' one, in <paramref name="language"/>; none for the application's starter roles.</summary>
    private (string Name, string Description)? PackageTextsOf(StarterRole starterRole, CultureInfo language)
        => catalogue.HasDefaultAdministrators && string.Equals(starterRole.Key, TenancyStarterRoles.DefaultAdministratorsKey, StringComparison.Ordinal)
            ? TenancyStarterRoleTexts.DefaultAdministratorsIn(language)
            : null;

    /// <summary>
    /// A role made from the <paramref name="draft"/> of a starter role, with the id given for the starter role or a new one.
    /// The role checks the draft's name and description as it checks any role's.
    /// </summary>
    private static TRole NewRoleFrom(
        RoleDraft draft,
        TTenantId tenant,
        IReadOnlyDictionary<string, TRoleId>? roleIds,
        TenancyCatalogue catalogue,
        TenancyActor<TSeatId>? by)
    {
        var id = roleIds is not null && roleIds.TryGetValue(draft.FromStarterRole!, out var given) ? given : TRoleId.Create();
        return TenancyInstances.NewRole<TRole, TRoleId, TTenantId, TSeatId>(id, tenant, draft, catalogue, by);
    }

    /// <summary>
    /// Ids given by starter role key name only starter roles a role is about to be made from, since a typo would otherwise be
    /// ignored, and give each id once, since two roles would otherwise share it.
    /// </summary>
    private static void RequireSeededStarterRoles(IReadOnlyDictionary<string, TRoleId>? roleIds, IReadOnlyCollection<StarterRole> starterRoles, TenantShape shape)
    {
        if (roleIds is null)
        {
            return;
        }

        var unknown = roleIds.Keys
            .Where(starterRoleKey => !starterRoles.Any(starterRole => string.Equals(starterRole.Key, starterRoleKey, StringComparison.Ordinal)))
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException(
                "Role ids are given for " + string.Join(", ", unknown) + ", but no role of a " + shape.ToString().ToLowerInvariant()
                + " tenant is made from such a starter role here. Starter roles: " + string.Join(", ", starterRoles.Select(starterRole => starterRole.Key)) + ".",
                nameof(roleIds));
        }

        var shared = roleIds.GroupBy(pair => pair.Value).Where(group => group.Count() > 1).ToArray();
        if (shared.Length > 0)
        {
            throw new ArgumentException(
                "Role ids are given more than once: " + string.Join("; ", shared.Select(group => group.Key + " for "
                    + string.Join(", ", group.Select(pair => pair.Key).Order(StringComparer.Ordinal)))) + ". Each role needs an id of its own.",
                nameof(roleIds));
        }
    }

    /// <summary>
    /// The marker a page of <see cref="ListAsync"/> continues from: the slug of the last tenant of the page before,
    /// written so that a client passes it on as it is and makes nothing of it.
    /// </summary>
    private static class PageMarker
    {
        /// <summary>The marker for the page after the tenant with <paramref name="slug"/>.</summary>
        public static string Write(string slug) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(slug));

        /// <summary>The slug <paramref name="marker"/> was written from, or <see langword="null"/> when it is no marker of the manager's.</summary>
        public static string? Read(string marker)
        {
            // No slug is longer than a slug may be, so neither is its marker: a longer one is not read at all.
            if (marker.Length == 0 || marker.Length > Base64Url.GetEncodedLength(TenantSlug.MaxLength) || !Base64Url.IsValid(marker))
            {
                return null;
            }

            var slug = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(marker));
            return Regex.IsMatch(slug, TenantSlug.Pattern, RegexOptions.CultureInvariant) ? slug : null;
        }
    }
}
