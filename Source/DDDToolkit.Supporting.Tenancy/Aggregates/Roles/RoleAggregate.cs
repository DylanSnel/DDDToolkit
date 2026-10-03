using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// A tenant's role: a name, and the permission keys it grants wherever it is granted. The application
/// declares its own class with <see cref="RoleAggregateAttribute{TRoleId}"/>.
/// <para>
/// Its keys come from the catalogue only, with the keys they imply added, and are kept distinct and sorted.
/// A key the catalogue later retires, or no longer knows at all, may stay on the role; it grants nothing,
/// and is only refused when someone adds it anew. A role made from a pack remembers which one in
/// <see cref="FromPack"/>, and only when it is made.
/// </para>
/// <para>
/// Whoever holds <see cref="TenancyKeys.RolesManage"/> may put any key into any role. That is on purpose:
/// that key is the administrator's key, and an administrator can give themselves anything already. A key
/// added to a role reaches every seat that holds the role, seats that gave themselves the role included, so
/// the use case lets an administrator alone add or take out a key that manages access, or archive a role that
/// holds one.
/// </para>
/// <para>
/// Every method that changes the role takes who makes the change, <c>by</c>, and puts it on the event it
/// raises. The use cases pass the actor of their caller. A role is declared without the seat class and does not
/// know the seat id's type, so the methods take it: it follows from a caller's actor
/// (<c>role.Archive(caller.Actor)</c>), and code that names nobody, an import or a test, names the type instead
/// (<c>role.Archive&lt;SeatId&gt;()</c>).
/// </para>
/// </summary>
/// <typeparam name="TRoleId">The application's role id.</typeparam>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
[AggregateRootBase]
public abstract partial class RoleAggregate<TRoleId, TTenantId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
{
    /// <summary>The longest name a role may have.</summary>
    public const int MaxNameLength = TenancyNames.MaxRoleNameLength;

    /// <summary>The longest description a role may have.</summary>
    public const int MaxDescriptionLength = TenancyNames.MaxRoleDescriptionLength;

    /// <summary>The tenant the role belongs to.</summary>
    public TTenantId TenantId { get; private set; }

    /// <summary>The role's name, unique in its tenant.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What the role is for.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The key of the pack the role was copied from, or <see langword="null"/> for a role made by hand. Set when it is made.</summary>
    public string? FromPack { get; private set; }

    /// <summary>Whether the role is in use. An archived role grants nothing.</summary>
    public RoleStatus Status { get; private set; }

    /// <summary>The permission keys the role grants: expanded, distinct and in ordinal order.</summary>
    public IReadOnlyList<string> Keys { get; private set; } = [];

    /// <summary>This role's status and keys as a snapshot, for a seat's rules.</summary>
    public RoleFacts Facts => new(Status == RoleStatus.Active, Keys);

    /// <summary>
    /// What a constructor would do: gives a new instance its id, tenant, name, description and keys, and
    /// remembers the pack it came from; starts it active, and raises
    /// <see cref="RoleCreated{TTenantId, TRoleId, TSeatId}"/>. Called once, by <see cref="TenancyInstances"/>,
    /// right after the instance is made.
    /// </summary>
    internal void InitializeNew<TSeatId>(TRoleId id, TTenantId tenantId, RoleDraft draft, TenancyCatalogue catalogue, TenancyActor<TSeatId>? by)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(catalogue);

        var name = TenancyNames.Required(draft.Name, TenancyNames.RoleNameToken, MaxNameLength);
        var description = TenancyNames.Optional(draft.Description, TenancyNames.RoleDescriptionToken, MaxDescriptionLength);
        var keys = catalogue.Expand(draft.Keys ?? []);

        Id = id;
        TenantId = tenantId;
        Name = name;
        Description = description;
        Keys = keys;
        FromPack = string.IsNullOrWhiteSpace(draft.FromPack) ? null : draft.FromPack.Trim();
        Status = RoleStatus.Active;

        RaiseDomainEvent(new RoleCreated<TTenantId, TRoleId, TSeatId>(tenantId, id, FromPack, by));
    }

    /// <summary>Renames the role and describes it again. The same name and description change nothing and raise nothing.</summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: a role does not know it by itself.</typeparam>
    /// <param name="name">Its new name.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.role-archived</c>, <c>tenancy.name-invalid</c>.</exception>
    public void Rename<TSeatId>(string name, string description, TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        RequireActive();

        var validName = TenancyNames.Required(name, TenancyNames.RoleNameToken, MaxNameLength);
        var validDescription = TenancyNames.Optional(description, TenancyNames.RoleDescriptionToken, MaxDescriptionLength);
        if (validName == Name && validDescription == Description)
        {
            return;
        }

        Name = validName;
        Description = validDescription;
        RaiseDomainEvent(new RoleRenamed<TTenantId, TRoleId, TSeatId>(TenantId, Id, by));
    }

    /// <summary>
    /// Sets the keys the role grants. A key the role already holds that the catalogue has since retired, or
    /// no longer knows, may stay as it is; every other key must be a live key of the catalogue, and the keys
    /// it implies are added. The same set again changes nothing and raises nothing. A change raises
    /// <see cref="RoleKeysChanged{TTenantId, TRoleId, TSeatId}"/> with the keys that came in and the keys that
    /// went out, the implied ones included.
    /// </summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: a role does not know it by itself.</typeparam>
    /// <param name="keys">Every key the role grants from now on.</param>
    /// <param name="catalogue">The catalogue the keys come from.</param>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException">
    /// <c>tenancy.role-archived</c>, <c>tenancy.unknown-permission</c> for a key that is new to the role and
    /// not live.
    /// </exception>
    public void SetKeys<TSeatId>(IReadOnlyCollection<string> keys, TenancyCatalogue catalogue, TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        var next = KeysAfterSetting(keys, catalogue);
        var before = Keys;
        if (next.SequenceEqual(before, StringComparer.Ordinal))
        {
            return;
        }

        Keys = next;

        // Both lists are distinct and in ordinal order, as the role's keys are, so what is left of each is too.
        RaiseDomainEvent(new RoleKeysChanged<TTenantId, TRoleId, TSeatId>(
            TenantId,
            Id,
            next.Except(before, StringComparer.Ordinal).ToArray(),
            before.Except(next, StringComparer.Ordinal).ToArray(),
            by));
    }

    /// <summary>
    /// The keys <see cref="SetKeys{TSeatId}"/> would leave the role with, refused the same way, and nothing changed:
    /// a use case decides on the role's future before it changes it.
    /// </summary>
    internal IReadOnlyList<string> KeysAfterSetting(IReadOnlyCollection<string> keys, TenancyCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(catalogue);
        RequireActive();

        var kept = new List<string>();
        var added = new List<string>();
        foreach (var key in keys)
        {
            var trimmed = key?.Trim() ?? string.Empty;
            if (!catalogue.IsLive(trimmed) && Keys.Contains(trimmed, StringComparer.Ordinal))
            {
                kept.Add(trimmed);
            }
            else
            {
                added.Add(trimmed);
            }
        }

        return kept
            .Concat(catalogue.Expand(added))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Archives the role. It stays, granted wherever it was, and grants nothing from now on.</summary>
    /// <typeparam name="TSeatId">The application's seat id, which the actor is closed over: a role does not know it by itself.</typeparam>
    /// <param name="by">Who makes the change, for the event; <see langword="null"/> when nobody is named.</param>
    /// <exception cref="RefusalException"><c>tenancy.role-archived</c>: it already is.</exception>
    public void Archive<TSeatId>(TenancyActor<TSeatId>? by = null)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        RequireActive();

        Status = RoleStatus.Archived;
        RaiseDomainEvent(new RoleArchived<TTenantId, TRoleId, TSeatId>(TenantId, Id, by));
    }

    /// <summary>Whether the role holds <paramref name="key"/>, live or not. Whether it grants it is another question: see <see cref="Status"/> and the catalogue.</summary>
    public bool Holds(string key) => Keys.Contains(key, StringComparer.Ordinal);

    private void RequireActive()
    {
        if (Status == RoleStatus.Archived)
        {
            throw TenancyRefusals.Of(TenancyRefusals.RoleArchived);
        }
    }
}
