using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.Managers;

/// <summary>
/// The organization and its tree: renaming the organization, adding, renaming, moving and archiving units, and reading
/// the units. Your code takes it as <c>OrganizationManager</c>, a class the generator writes, closed over your classes
/// and ids, and the container makes per scope.
/// <para>
/// Each change of a unit takes the unit, or its id. Both check the same: the manager loads the organization itself and
/// asks the caller's rights over what it loaded, never over the state of a unit it was handed. Each returns the unit as
/// it changed it, the one the unit of work keeps.
/// </para>
/// <para>
/// Each change of a unit needs <see cref="TenancyPermissions.UnitsManage"/> where the change is made, so a seat that
/// manages a region manages the units below it and nothing beside or above it. Two edits of the tree at the same time
/// are kept apart by the organization's version. A move also changes what every permission held above the moved unit
/// reaches, so it takes the access revision too.
/// </para>
/// <para>
/// The reads answer units as the application's own class, whole, with every field of its own, beside the path and depth
/// that are not on it (<see cref="UnitInTree"/>). Each is read for the answer and tracked by nobody. Paths are written
/// from the root down, joined with <c>" / "</c>, and come from the closure of the tree, so a unit's path names the units
/// above it even where the caller is not placed under those.
/// </para>
/// </summary>
/// <param name="store">Where the organization is loaded and saved.</param>
/// <param name="catalogue">The permissions asked for.</param>
/// <param name="clock">What "now" is.</param>
public class OrganizationManager<
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

    /// <summary>Renames the organization, which is the tenant's name.</summary>
    /// <param name="name">Its new name.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The organization, renamed.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.SettingsManage"/> for the whole tenant,
    /// <c>tenancy.name-invalid</c>.
    /// </exception>
    /// <example>
    /// <code>
    /// var renamed = await organization.RenameAsync("Harbor Works Group", cancellationToken);
    /// </code>
    /// </example>
    public async Task<TOrganization> RenameAsync(string name, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.RequireAtTenantAsync(TenancyPermissions.SettingsManage, cancellationToken).ConfigureAwait(false);

        var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
        organization.Rename(name, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return organization;
    }

    /// <summary>Adds a unit below <paramref name="parent"/>.</summary>
    /// <param name="parent">The unit it hangs under.</param>
    /// <param name="name">Its name.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="id">Its id, for imports and seeding; a new one otherwise.</param>
    /// <param name="configure">
    /// Sets the fields the application added to its unit class, such as what kind of unit it is, on the new
    /// unit once the caller and the tree are checked and before the organization takes the unit in, so they are
    /// saved in the same transaction and the class's own rules judge them there. When it throws, the unit is not
    /// added: nothing is saved, by this call or by a later save in the same scope.
    /// </param>
    /// <returns>The new unit.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.UnitsManage"/> at the parent, and what the
    /// organization refuses, <c>tenancy.flat-tenant</c> first.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is already a unit's: a mistake in the import or seeding that gave it, not a
    /// refusal.
    /// </exception>
    /// <example>
    /// A handler that adds a unit from a request, and sets a field the application added to its unit class:
    /// <code>
    /// var unit = await organization.AddUnitAsync(command.Parent, command.Name, cancellationToken, configure: added =&gt; added.SetKind(command.Kind));
    /// </code>
    /// </example>
    public async Task<TUnit> AddUnitAsync(
        TUnitId parent,
        string name,
        CancellationToken cancellationToken,
        TUnitId? id = null,
        Action<TUnit>? configure = null)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.RequireAtAsync(TenancyPermissions.UnitsManage, parent, cancellationToken).ConfigureAwait(false);

        var tenant = await gate.LoadTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
        // The organization runs the callback before it takes the unit in: one that throws leaves the tracked
        // organization as it was, so no later save in this scope writes the unit without the application's
        // fields. What it sets is written with the unit.
        var unit = organization.AddUnit(id ?? TUnitId.Create(), parent, name, tenant.Shape, gate.By, configure);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return unit;
    }

    /// <summary>Adds a unit below <paramref name="parent"/>, as <see cref="AddUnitAsync(TUnitId, string, CancellationToken, TUnitId?, Action{TUnit}?)"/> does with its id.</summary>
    /// <inheritdoc cref="AddUnitAsync(TUnitId, string, CancellationToken, TUnitId?, Action{TUnit}?)"/>
    /// <example>
    /// <code>
    /// var northCoast = await organization.AddUnitAsync(north, "North Coast", cancellationToken);
    /// </code>
    /// </example>
    public Task<TUnit> AddUnitAsync(
        TUnit parent,
        string name,
        CancellationToken cancellationToken,
        TUnitId? id = null,
        Action<TUnit>? configure = null)
        => AddUnitAsync(IdOf(parent), name, cancellationToken, id, configure);

    /// <summary>Renames a unit.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="name">Its new name.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The unit, renamed.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.UnitsManage"/> at the unit, and what the
    /// organization refuses.
    /// </exception>
    /// <example>
    /// <code>
    /// var unit = await organization.RenameUnitAsync(command.Unit, command.Name, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TUnit> RenameUnitAsync(TUnitId unit, string name, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.RequireAtAsync(TenancyPermissions.UnitsManage, unit, cancellationToken).ConfigureAwait(false);

        var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
        organization.RenameUnit(unit, name, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return Changed(organization, unit);
    }

    /// <summary>Renames a unit, as <see cref="RenameUnitAsync(TUnitId, string, CancellationToken)"/> does with its id.</summary>
    /// <inheritdoc cref="RenameUnitAsync(TUnitId, string, CancellationToken)"/>
    /// <example>
    /// <code>
    /// var renamed = await organization.RenameUnitAsync(northCoast, "North Coast Yard", cancellationToken);
    /// </code>
    /// </example>
    public Task<TUnit> RenameUnitAsync(TUnit unit, string name, CancellationToken cancellationToken)
        => RenameUnitAsync(IdOf(unit), name, cancellationToken);

    /// <summary>
    /// Moves a unit, and everything below it, under another unit. The caller needs the permission where the unit
    /// hangs now and where it goes, so a unit only moves between parts of the tree the caller manages both of.
    /// A move changes which grants reach the unit, so a seat moves it only as far as it could give and take
    /// away what that changes: the move gives the seat itself no permission it does not hold at the unit already,
    /// and none for longer than it holds it there; and it gives or takes away no permission that manages access,
    /// from anyone, that the seat does not hold at the unit for at least as long as that grant runs. The second
    /// part is containment, which goes when the application turns it off
    /// (<see cref="ApplicationCatalogue.ContainAccessManagingPermissions"/>): then a permission that manages access follows a
    /// move as any other does, and the move still gives the seat itself nothing.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <param name="newParent">The unit it hangs under from now on.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The unit, moved.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.unit-not-found</c>, <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.UnitsManage"/>
    /// at the current parent and at the new one, what the organization refuses, and
    /// <c>tenancy.grant-exceeds-own</c> naming the permissions the move would give the calling seat, or, while
    /// containment is on, give or take away from anyone, that it lacks at the unit, or lacks for long enough.
    /// </exception>
    /// <example>
    /// <code>
    /// var moved = await organization.MoveUnitAsync(command.Unit, command.NewParent, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TUnit> MoveUnitAsync(TUnitId unit, TUnitId newParent, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();
        await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var moved = organization.FindUnit(unit) ?? throw TenancyRefusals.Refuse(TenancyRefusals.UnitNotFound);
        await gate.RequireAtAsync(TenancyPermissions.UnitsManage, moved.ParentId ?? unit, cancellationToken).ConfigureAwait(false);
        await gate.RequireAtAsync(TenancyPermissions.UnitsManage, newParent, cancellationToken).ConfigureAwait(false);

        organization.RequireMove(unit, newParent);
        await gate.RequireMovableAsync(unit, moved.ParentId!.Value, newParent, cancellationToken).ConfigureAwait(false);
        organization.MoveUnit(unit, newParent, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return Changed(organization, unit);
    }

    /// <summary>Moves a unit under another unit, as <see cref="MoveUnitAsync(TUnitId, TUnitId, CancellationToken)"/> does with their ids.</summary>
    /// <inheritdoc cref="MoveUnitAsync(TUnitId, TUnitId, CancellationToken)"/>
    /// <example>
    /// North Coast moves from North to South Bay. The grants held at North no longer reach it:
    /// <code>
    /// var moved = await organization.MoveUnitAsync(northCoast, southBay, cancellationToken);
    /// </code>
    /// </example>
    public Task<TUnit> MoveUnitAsync(TUnit unit, TUnit newParent, CancellationToken cancellationToken)
        => MoveUnitAsync(IdOf(unit), IdOf(newParent), cancellationToken);

    /// <summary>
    /// Archives a unit. The caller needs the permission at its parent: archiving takes the unit out of the part of
    /// the tree above it. Seats placed there and rights held there keep working.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The unit, archived.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.unit-not-found</c>, <c>tenancy.not-permitted</c> without <see cref="TenancyPermissions.UnitsManage"/>
    /// at the parent, and what the organization refuses.
    /// </exception>
    /// <example>
    /// <code>
    /// var archived = await organization.ArchiveUnitAsync(command.Unit, cancellationToken);
    /// </code>
    /// </example>
    public async Task<TUnit> ArchiveUnitAsync(TUnitId unit, CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();

        // A placement or a grant at the unit checks it is active without changing the organization, so the
        // organization's version does not stop one saved next to this archive; the revision does.
        await gate.SerializeAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var organization = await gate.LoadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var archived = organization.FindUnit(unit) ?? throw TenancyRefusals.Refuse(TenancyRefusals.UnitNotFound);
        await gate.RequireAtAsync(TenancyPermissions.UnitsManage, archived.ParentId ?? unit, cancellationToken).ConfigureAwait(false);

        organization.ArchiveUnit(unit, gate.By);
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return Changed(organization, unit);
    }

    /// <summary>Archives a unit, as <see cref="ArchiveUnitAsync(TUnitId, CancellationToken)"/> does with its id.</summary>
    /// <inheritdoc cref="ArchiveUnitAsync(TUnitId, CancellationToken)"/>
    /// <example>
    /// <code>
    /// var archived = await organization.ArchiveUnitAsync(northCoast, cancellationToken);
    /// </code>
    /// </example>
    public Task<TUnit> ArchiveUnitAsync(TUnit unit, CancellationToken cancellationToken)
        => ArchiveUnitAsync(IdOf(unit), cancellationToken);

    // ------------------------------------------------------------------ reads

    /// <summary>
    /// The units the caller reads, by path: for a seat, the units it is placed in and every unit below them;
    /// for system work in the tenant, every unit. Each is the application's own unit, whole, with its path and
    /// depth beside it (<see cref="UnitInTree"/>), so a field the application added to its unit class, such as
    /// what kind of unit it is, is shown from the units read here, with no statement more.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The units, each with its path and depth, by path. A seat that reads no unit of the organization gets none.</returns>
    /// <exception cref="Exceptions.RefusalException">The caller is nobody.</exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// <code>
    /// foreach (var (unit, path, depth) in await organization.ListUnitsAsync(cancellationToken))
    /// {
    ///     // path: "Harbor Works / North / North Coast", depth: 3
    /// }
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<UnitInTree>> ListUnitsAsync(CancellationToken cancellationToken)
    {
        var gate = Gate();
        var tenantId = gate.RequireTenant();

        // A seat a restrictive rule of the application's keeps out of the organization reads none of its units.
        if (await gate.OrganizationTheUserReadsAsync(tenantId, cancellationToken).ConfigureAwait(false) is not { } organization)
        {
            return [];
        }

        var units = await TenancyUnitMap<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.ReadAsync(store, organization, cancellationToken).ConfigureAwait(false);
        IEnumerable<TUnitId> readable = gate.BySystem
            ? units.Ids
            : await store.Queries.ListAsync(gate.Queries.ReadableUnits(), cancellationToken).ConfigureAwait(false);

        return units.InTree(readable);
    }

    /// <summary>
    /// What the units among <paramref name="ids"/> that are the caller's tenant's are called, by path, whichever of
    /// them the caller is placed under: a seat that works on something at a unit it is not placed under still reads
    /// what that unit is called, and so does one an application's restrictive rule keeps out of the organization.
    /// So the answer is each unit's name, path and depth, and nothing more of it: on a database that keeps its own
    /// policies it comes from Tenancy's function that runs as its owner and answers the ids asked alone. An id of
    /// another tenant, or of no unit, is left out without a word.
    /// </summary>
    /// <param name="ids">The units asked about, at most <see cref="MostIds"/> different ones.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Each unit's id, name, path and depth, by path.</returns>
    /// <exception cref="Exceptions.RefusalException">
    /// The caller is nobody, with its own code; or <c>tenancy.too-many-ids</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The caller is system work outside any tenant.</exception>
    /// <example>
    /// The paths of the units a page of projects sits at, in one read:
    /// <code>
    /// var named = await organization.UnitsByIdAsync([.. projects.Select(project =&gt; project.UnitId)], cancellationToken);
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<NamedUnit<TUnitId>>> UnitsByIdAsync(IReadOnlyCollection<TUnitId> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var gate = Gate();
        var tenantId = gate.RequireTenant();
        var asked = TenancyReads.Asked(ids);
        if (asked.Count == 0)
        {
            return [];
        }

        // A seat asks the storage, which answers the ids alone where the database keeps its own policies; system
        // work in the tenant reads the organization whole, and no rule of the application's narrows it.
        if (!gate.BySystem && await store.UnitsNamedAsync(tenantId, asked, cancellationToken).ConfigureAwait(false) is { } named)
        {
            return [.. named.OrderBy(unit => unit.Path, StringComparer.OrdinalIgnoreCase)];
        }

        var organization = await gate.ReadOrganizationAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var units = await TenancyUnitMap<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.ReadAsync(store, organization, cancellationToken).ConfigureAwait(false);
        return [.. units.InTree(asked).Select(found => new NamedUnit<TUnitId>(found.Unit.Id, found.Unit.Name, found.Path, found.Depth))];
    }

    // ------------------------------------------------------------------ the records it answers

    /// <summary>
    /// A unit, the application's own and whole, with where it sits in the tree: its path from the root and how deep
    /// it is. The unit carries its id, its parent, its name, its status and every field the application added, such
    /// as what kind of unit it is; the path and the depth are not on it. They come from the closure and the units
    /// above it, which the answer does not carry, as a seat lists only the units under its placements, so the
    /// application would need a read more to work them out. So that is all this adds, and an application shows a
    /// unit by what it selects (<see cref="ListUnitsAsync"/>).
    /// </summary>
    /// <remarks>
    /// The unit was read for this answer and is tracked by nobody: nothing done to it is saved, by this question or by
    /// a save later in the same unit of work.
    /// </remarks>
    /// <param name="Unit">The unit, the application's own class.</param>
    /// <param name="Path">The names of the unit and the units above it, the root first, joined with <c>" / "</c>.</param>
    /// <param name="Depth">How deep the unit is, the root being 1.</param>
    public sealed record UnitInTree(TUnit Unit, string Path, int Depth);

    // ------------------------------------------------------------------ helpers

    /// <summary>What every command asks before it acts, for one call: the caller and "now" are fixed when it starts.</summary>
    private TenancyGate<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> Gate() => new(store, catalogue, clock);

    /// <summary>The unit a command changed, as the organization the unit of work keeps holds it now.</summary>
    private static TUnit Changed(TOrganization organization, TUnitId unit)
        => organization.FindUnit(unit) ?? throw TenancyRefusals.Refuse(TenancyRefusals.UnitNotFound);

    private static TUnitId IdOf(TUnit unit) => (unit ?? throw new ArgumentNullException(nameof(unit))).Id;
}
