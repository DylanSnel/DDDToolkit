using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// A tenant's organization: its name and the tree of its units, one root and everything below it. It
/// shares its tenant's id. The application declares its own class with
/// <see cref="OrganizationAggregateAttribute{TTenantId}"/>, and the generator closes this one over the
/// application's tenant id, its unit class and the unit's id.
/// <para>
/// The tree is one aggregate so that its rules hold for the whole of it at once: exactly one root, no unit
/// under itself, at most <see cref="MaxDepth"/> levels. Every change goes through a method here, which
/// refuses what would break them before anything changes; the nested invariants are the net under that.
/// A unit is archived, never deleted, so what points at it keeps something to point at.
/// </para>
/// <para>
/// The walks over the tree keep a visited set, and the walks up and down by level stop one step past
/// <see cref="MaxDepth"/>, so a tree that is broken all the same (written by hand into the database, say)
/// never makes them loop; the invariants report what is wrong with it.
/// </para>
/// <para>
/// Every method that changes the organization takes who makes the change, <c>by</c>, and puts it on the event
/// it raises. The use cases pass the actor of their caller. An organization is declared without the seat class
/// and does not know the seat id's type, so the methods take it: it follows from a caller's actor
/// (<c>organization.ArchiveUnit(unit, caller.Actor)</c>), and code that names nobody, an import or a test,
/// names the type instead (<c>organization.ArchiveUnit&lt;SeatId&gt;(unit)</c>).
/// </para>
/// </summary>
/// <typeparam name="TTenantId">The application's tenant id, which is also this organization's id.</typeparam>
/// <typeparam name="TUnit">The application's unit class.</typeparam>
/// <typeparam name="TUnitId">The application's unit id.</typeparam>
[AggregateRootBase]
public abstract partial class OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
{
    /// <summary>The most levels an organization has, the root being the first.</summary>
    public const int MaxDepth = 32;

    /// <summary>The longest name an organization, which is the tenant's name, may have.</summary>
    public const int MaxNameLength = 200;

    /// <summary>The tenant's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Every unit of the organization, archived ones included, the root first.</summary>
    public partial IReadOnlyList<TUnit> Units { get; }

    /// <summary>The one unit with no parent.</summary>
    /// <exception cref="InvalidOperationException">The organization has no root, which its invariants never let it save.</exception>
    public TUnit Root
        => _units.FirstOrDefault(unit => unit.ParentId is null)
           ?? throw new InvalidOperationException("The organization " + Id + " has no root.");

    /// <summary>
    /// What a constructor would do: gives a new instance its id and name and creates its root, through the
    /// application's own unit class, raising <see cref="OrganizationUnitAdded{TTenantId, TUnitId, TSeatId}"/> for
    /// it. Called once, by <see cref="TenancyInstances"/>, right after the instance is made.
    /// </summary>
    internal void InitializeNew<TSeatId>(TTenantId id, string name, TUnitId rootUnitId, string rootName, TenancyActor<TSeatId>? by)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        var validName = TenancyNames.Required(name, TenancyNames.TenantNameToken, MaxNameLength);
        var root = HostInstances<TUnit>.New();
        root.InitializeNew(rootUnitId, null, rootName);

        Id = id;
        Name = validName;
        _units.Add(root);

        RaiseDomainEvent(new OrganizationUnitAdded<TTenantId, TUnitId, TSeatId>(id, rootUnitId, null, by));
    }

    /// <summary>Renames the organization. The same name again changes nothing and raises nothing.</summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: an organization does not know it by itself.</typeparam>
    /// <param name="name">The new name, 1 to <see cref="MaxNameLength"/> characters.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.name-invalid</c>: blank or too long.</exception>
    public void Rename<TSeatId>(string name, TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        var validName = TenancyNames.Required(name, TenancyNames.TenantNameToken, MaxNameLength);
        if (validName == Name)
        {
            return;
        }

        Name = validName;
        RaiseDomainEvent(new OrganizationRenamed<TTenantId, TSeatId>(Id, by));
    }

    /// <summary>
    /// Adds a unit below an active one, in a hierarchical tenant. A field the application added to its unit class
    /// starts at its default unless <paramref name="configure"/> sets it.
    /// </summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: an organization does not know it by itself.</typeparam>
    /// <param name="id">The new unit's id.</param>
    /// <param name="parentId">The unit it hangs under.</param>
    /// <param name="name">Its name, 1 to <see cref="OrganizationUnitEntity{TUnitId}.MaxNameLength"/> characters.</param>
    /// <param name="shape">The tenant's shape: a flat tenant has only its root.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <param name="configure">
    /// Sets the application's own fields on the new unit, after every check here and before the organization takes
    /// the unit in. When it throws, the organization is as it was: no unit and no event, so a later save in the
    /// same unit of work has nothing of it to write.
    /// </param>
    /// <returns>The new unit, an instance of the application's own class.</returns>
    /// <exception cref="RefusalException">
    /// In this order: <c>tenancy.flat-tenant</c>, <c>tenancy.unit-not-found</c> for the parent,
    /// <c>tenancy.unit-not-active</c> when the parent is archived, <c>tenancy.depth-exceeded</c>,
    /// <c>tenancy.name-invalid</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A unit with <paramref name="id"/> exists. Ids are made to be new, and one is given only by an import or
    /// seeding, so a clash is a mistake in that code rather than something a caller did.
    /// </exception>
    public TUnit AddUnit<TSeatId>(
        TUnitId id,
        TUnitId parentId,
        string name,
        TenantShape shape,
        TenancyActor<TSeatId>? by = null,
        Action<TUnit>? configure = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        if (shape == TenantShape.Flat)
        {
            throw TenancyRefusals.Of(TenancyRefusals.FlatTenant);
        }

        var parent = RequireUnit(parentId);
        if (parent.Status != UnitStatus.Active)
        {
            throw TenancyRefusals.Of(TenancyRefusals.UnitNotActive, ("Unit", parentId));
        }

        if (DepthOf(parentId) + 1 > MaxDepth)
        {
            throw TenancyRefusals.Of(TenancyRefusals.DepthExceeded, ("Max", MaxDepth));
        }

        if (FindUnit(id) is not null)
        {
            throw new ArgumentException("The organization already has a unit " + id + ".", nameof(id));
        }

        var unit = HostInstances<TUnit>.New();
        unit.InitializeNew(id, parentId, name);

        // The application's fields before the unit is the organization's: a callback that throws leaves the
        // organization untouched, even where it is tracked and the caller goes on to save something else.
        configure?.Invoke(unit);
        _units.Add(unit);

        RaiseDomainEvent(new OrganizationUnitAdded<TTenantId, TUnitId, TSeatId>(Id, id, parentId, by));
        return unit;
    }

    /// <summary>Renames an active unit. The same name again changes nothing and raises nothing.</summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: an organization does not know it by itself.</typeparam>
    /// <param name="id">The unit.</param>
    /// <param name="name">Its new name.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException">
    /// <c>tenancy.unit-not-found</c>, <c>tenancy.unit-archived</c>, <c>tenancy.name-invalid</c>.
    /// </exception>
    public void RenameUnit<TSeatId>(TUnitId id, string name, TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        var unit = RequireUnit(id);
        if (unit.Status == UnitStatus.Archived)
        {
            throw TenancyRefusals.Of(TenancyRefusals.UnitArchived);
        }

        var validName = TenancyNames.Required(name, TenancyNames.UnitNameToken, OrganizationUnitEntity<TUnitId>.MaxNameLength);
        if (validName == unit.Name)
        {
            return;
        }

        unit.Rename(validName);
        RaiseDomainEvent(new OrganizationUnitRenamed<TTenantId, TUnitId, TSeatId>(Id, id, by));
    }

    /// <summary>
    /// Moves a unit, and everything below it, under another active unit. What a key held above the unit
    /// reaches changes with it, which is why the use case also guards the move against concurrent grants.
    /// </summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: an organization does not know it by itself.</typeparam>
    /// <param name="id">The unit that moves.</param>
    /// <param name="newParentId">The unit it hangs under from now on.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException">
    /// In this order: <c>tenancy.unit-not-found</c>, <c>tenancy.root-immovable</c>, <c>tenancy.unit-archived</c>,
    /// <c>tenancy.unit-not-found</c> for the new parent, <c>tenancy.unit-not-active</c> when it is archived,
    /// <c>tenancy.cycle</c> when it is the unit or below it, <c>tenancy.same-parent</c>,
    /// <c>tenancy.depth-exceeded</c>.
    /// </exception>
    public void MoveUnit<TSeatId>(TUnitId id, TUnitId newParentId, TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        var unit = RequireMove(id, newParentId);
        var from = unit.ParentId!.Value;

        unit.SetParent(newParentId);
        RaiseDomainEvent(new OrganizationUnitMoved<TTenantId, TUnitId, TSeatId>(Id, id, from, newParentId, by));
    }

    /// <summary>
    /// Refuses what <see cref="MoveUnit{TSeatId}"/> refuses, in the same order, and changes nothing: a use case
    /// checks the move before it asks what the move would change.
    /// </summary>
    /// <returns>The unit that would move.</returns>
    internal TUnit RequireMove(TUnitId id, TUnitId newParentId)
    {
        var unit = RequireUnit(id);
        if (unit.ParentId is not { } from)
        {
            throw TenancyRefusals.Of(TenancyRefusals.RootImmovable);
        }

        if (unit.Status == UnitStatus.Archived)
        {
            throw TenancyRefusals.Of(TenancyRefusals.UnitArchived);
        }

        var newParent = RequireUnit(newParentId);
        if (newParent.Status != UnitStatus.Active)
        {
            throw TenancyRefusals.Of(TenancyRefusals.UnitNotActive, ("Unit", newParentId));
        }

        if (SubtreeOf(id).Contains(newParentId))
        {
            throw TenancyRefusals.Of(TenancyRefusals.Cycle);
        }

        if (from.Equals(newParentId))
        {
            throw TenancyRefusals.Of(TenancyRefusals.SameParent);
        }

        if (DepthOf(newParentId) + HeightOf(id) > MaxDepth)
        {
            throw TenancyRefusals.Of(TenancyRefusals.DepthExceeded, ("Max", MaxDepth));
        }

        return unit;
    }

    /// <summary>
    /// Archives a unit that has no active unit below it. Seats placed there and rights granted there keep
    /// working. Nothing new goes to it: no unit is added below it or moved under it here, and the use cases
    /// place no seat and grant no role there.
    /// </summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: an organization does not know it by itself.</typeparam>
    /// <param name="id">The unit.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException">
    /// <c>tenancy.unit-not-found</c>, <c>tenancy.root-not-archivable</c>, <c>tenancy.unit-archived</c>,
    /// <c>tenancy.unit-has-active-children</c>.
    /// </exception>
    public void ArchiveUnit<TSeatId>(TUnitId id, TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        var unit = RequireUnit(id);
        if (unit.IsRoot)
        {
            throw TenancyRefusals.Of(TenancyRefusals.RootNotArchivable);
        }

        if (unit.Status == UnitStatus.Archived)
        {
            throw TenancyRefusals.Of(TenancyRefusals.UnitArchived);
        }

        if (_units.Any(child => child.Status == UnitStatus.Active && child.ParentId is { } parent && parent.Equals(id)))
        {
            throw TenancyRefusals.Of(TenancyRefusals.UnitHasActiveChildren);
        }

        unit.Archive();
        RaiseDomainEvent(new OrganizationUnitArchived<TTenantId, TUnitId, TSeatId>(Id, id, by));
    }

    /// <summary>The unit with this id, archived or not, or <see langword="null"/> when the organization has none.</summary>
    public TUnit? FindUnit(TUnitId id)
    {
        foreach (var unit in _units)
        {
            if (unit.Id.Equals(id))
            {
                return unit;
            }
        }

        return null;
    }

    /// <summary>Whether the organization has an active unit with this id.</summary>
    public bool IsActiveUnit(TUnitId id) => FindUnit(id) is { Status: UnitStatus.Active };

    /// <summary>How deep a unit is: 1 for the root, 2 for a unit right below it.</summary>
    /// <exception cref="RefusalException"><c>tenancy.unit-not-found</c>.</exception>
    public int DepthOf(TUnitId id) => AncestorsOf(id).Count;

    /// <summary>A unit and the units above it, the unit itself first and the root last.</summary>
    /// <exception cref="RefusalException"><c>tenancy.unit-not-found</c>.</exception>
    public IReadOnlyList<TUnitId> AncestorsOf(TUnitId id) => AncestorsOf(RequireUnit(id), ById());

    private static List<TUnitId> AncestorsOf(TUnit unit, Dictionary<TUnitId, TUnit> byId)
    {
        var ancestors = new List<TUnitId> { unit.Id };
        var visited = new HashSet<TUnitId> { unit.Id };

        // One step more than the deepest a tree may be, so a tree that is too deep still says so.
        while (unit.ParentId is { } parentId
               && ancestors.Count <= MaxDepth
               && visited.Add(parentId)
               && byId.TryGetValue(parentId, out var parent))
        {
            ancestors.Add(parentId);
            unit = parent;
        }

        return ancestors;
    }

    /// <summary>A unit and every unit below it, the unit itself first, archived ones included.</summary>
    /// <exception cref="RefusalException"><c>tenancy.unit-not-found</c>.</exception>
    public IReadOnlyList<TUnitId> SubtreeOf(TUnitId id)
    {
        RequireUnit(id);
        var children = Children();
        var subtree = new List<TUnitId> { id };
        var visited = new HashSet<TUnitId> { id };

        for (var index = 0; index < subtree.Count; index++)
        {
            if (!children.TryGetValue(subtree[index], out var below))
            {
                continue;
            }

            foreach (var child in below)
            {
                if (visited.Add(child))
                {
                    subtree.Add(child);
                }
            }
        }

        return subtree;
    }

    /// <summary>How many levels a unit and the units below it span: 1 for a unit with nothing below it.</summary>
    /// <exception cref="RefusalException"><c>tenancy.unit-not-found</c>.</exception>
    public int HeightOf(TUnitId id)
    {
        RequireUnit(id);
        var children = Children();
        var visited = new HashSet<TUnitId> { id };
        var level = new List<TUnitId> { id };
        var height = 0;

        while (level.Count > 0 && height <= MaxDepth)
        {
            height++;
            var next = new List<TUnitId>();
            foreach (var unit in level)
            {
                if (children.TryGetValue(unit, out var below))
                {
                    next.AddRange(below.Where(visited.Add));
                }
            }

            level = next;
        }

        return height;
    }

    private TUnit RequireUnit(TUnitId id)
        => FindUnit(id) ?? throw TenancyRefusals.Of(TenancyRefusals.UnitNotFound);

    private Dictionary<TUnitId, TUnit> ById()
    {
        var byId = new Dictionary<TUnitId, TUnit>(_units.Count);
        foreach (var unit in _units)
        {
            byId.TryAdd(unit.Id, unit);
        }

        return byId;
    }

    private Dictionary<TUnitId, List<TUnitId>> Children()
    {
        var children = new Dictionary<TUnitId, List<TUnitId>>();
        foreach (var unit in _units)
        {
            if (unit.ParentId is { } parent)
            {
                if (!children.TryGetValue(parent, out var list))
                {
                    children[parent] = list = [];
                }

                list.Add(unit.Id);
            }
        }

        return children;
    }
}
