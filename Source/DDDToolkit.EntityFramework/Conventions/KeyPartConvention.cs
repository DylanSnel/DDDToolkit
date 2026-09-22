using System.Reflection;
using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DDDToolkit.EntityFramework.Conventions;

/// <summary>
/// Builds composite primary keys from <c>[KeyPart]</c> properties. A type with key parts is keyed on
/// its parts, in declaration order, followed by its <c>Id</c>; every owned relationship from it
/// carries the same parts in its foreign key, taken from the owned type's own property of the same
/// name and type, so an owned row can never point at an owner with a different value:
/// <code>
/// Project   PK (RegionId, Id)
/// Milestone PK (RegionId, ProjectId, Id)   FK (RegionId, ProjectId) -> Project (RegionId, Id)
/// </code>
/// <para>
/// The parts travel down the whole ownership tree: an entity owned by <c>Milestone</c> gets
/// <c>(RegionId, MilestoneProjectId, MilestoneId)</c> as its foreign key. An owned type's own key
/// parts, if it declares any its owner does not have, join its key after the foreign key and before
/// its <c>Id</c>.
/// </para>
/// <para>
/// An owned type that has no property for one of its owner's parts is an error when the model is
/// built, not a silently narrower key: declare the property on the child (and set it from the
/// parent) or configure the relationship yourself. Explicit configuration wins: a primary key set
/// with <c>HasKey</c> or <c>[PrimaryKey]</c>, or an ownership foreign key set with
/// <c>HasForeignKey</c>, is left exactly as written, and so is everything owned below it.
/// </para>
/// <para>
/// A model without <c>[KeyPart]</c> anywhere is not touched at all.
/// </para>
/// </summary>
public sealed class KeyPartConvention : IModelFinalizingConvention
{
    private static readonly MethodInfo ReadKeyPartsMethod =
        typeof(KeyPartConvention).GetMethod(nameof(ReadKeyParts), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var model = modelBuilder.Metadata;
        if (!model.GetEntityTypes().Any(static entityType => typeof(IHasKeyParts).IsAssignableFrom(entityType.ClrType)))
        {
            return;
        }

        // Re-keying a principal makes EF Core rebuild the foreign keys that point at it, with fresh
        // shadow properties ("ProjectId1"). Remember how each ownership looked before anything
        // changes, so the new keys reuse the original properties and therefore the original columns.
        var snapshots = model.GetEntityTypes()
            .SelectMany(static entityType => entityType.GetDeclaredForeignKeys())
            .Where(static foreignKey => foreignKey.IsOwnership)
            .ToDictionary(static foreignKey => foreignKey.DeclaringEntityType.Name, OwnershipSnapshot.Take, StringComparer.Ordinal);

        foreach (var entityType in model.GetEntityTypes().Where(static e => e.BaseType is null && !e.IsOwned()).ToList())
        {
            var key = entityType.FindPrimaryKey();
            if (key is null || IsConfiguredByHost(key.GetConfigurationSource()))
            {
                continue;
            }

            var parts = KeyPartsOf(entityType.ClrType);
            var newKey = parts.Select(part => MapClrProperty(entityType, part)).ToList();
            newKey.AddRange(key.Properties.Where(property => !parts.Contains(property.Name)));

            Rekey(entityType, parts, parts.Count > 0 ? newKey : null, snapshots);
        }
    }

    /// <summary>
    /// Gives <paramref name="entityType"/> its new primary key (when <paramref name="newKey"/> is not
    /// null) and carries <paramref name="parts"/> down to everything it owns.
    /// <para>
    /// The order matters. Replacing a primary key outright removes the foreign keys that point at the
    /// old one, and with them any ownership one level further down. So the new key is added as an
    /// alternate key first, every ownership is moved onto it, and only then is it made primary.
    /// </para>
    /// </summary>
    private static void Rekey(IConventionEntityType entityType, IReadOnlyList<string> parts, List<IConventionProperty>? newKey, Dictionary<string, OwnershipSnapshot> snapshots)
    {
        if (newKey is not null && entityType.Builder.HasKey(newKey, fromDataAnnotation: true) is null)
        {
            return;
        }

        var principalKey = newKey ?? (IReadOnlyList<IConventionProperty>)entityType.FindPrimaryKey()!.Properties;
        var next = new List<(IConventionEntityType Owned, IReadOnlyList<string> Parts, List<IConventionProperty>? NewKey)>();

        foreach (var navigationName in entityType.GetDeclaredNavigations()
                     .Where(static n => n.ForeignKey.IsOwnership && !n.IsOnDependent)
                     .Select(static n => n.Name)
                     .ToList())
        {
            var foreignKey = entityType.FindNavigation(navigationName)!.ForeignKey;
            var owned = foreignKey.DeclaringEntityType;
            var ownParts = KeyPartsOf(owned.ClrType);
            var ownedParts = parts.Concat(ownParts.Where(part => !parts.Contains(part))).ToList();

            if (IsConfiguredByHost(foreignKey.GetPropertiesConfigurationSource())
                || owned.FindPrimaryKey() is { } ownedKey && IsConfiguredByHost(ownedKey.GetConfigurationSource())
                || !snapshots.TryGetValue(owned.Name, out var snapshot))
            {
                continue;
            }

            if (newKey is null && ownParts.Count == 0)
            {
                // Nothing changes here; something further down may still declare key parts.
                next.Add((owned, ownedParts, null));
                continue;
            }

            var foreignKeyProperties = foreignKey.Properties.ToList();
            if (newKey is not null)
            {
                foreignKeyProperties = MapForeignKey(entityType, owned, navigationName, principalKey, parts, snapshot);
                var relationship = foreignKey.Builder.HasPrincipalKey(principalKey, fromDataAnnotation: true)
                    ?.HasForeignKey(foreignKeyProperties, fromDataAnnotation: true);
                if (relationship is null)
                {
                    continue;
                }

                foreignKey = relationship.Metadata;
            }

            var ownedNewKey = new List<IConventionProperty>(foreignKeyProperties);
            if (foreignKey.PrincipalToDependent?.IsCollection == true)
            {
                // Rows of an owned collection are told apart by more than the owner: the owned type's
                // own key parts, then whatever its key had beyond the foreign key (its Id).
                foreach (var part in ownParts.Where(part => !ownedNewKey.Any(property => property.Name == part)))
                {
                    ownedNewKey.Add(MapClrProperty(owned, part));
                }

                foreach (var name in snapshot.KeyBeyondForeignKey)
                {
                    if (owned.FindProperty(name) is { } property && !ownedNewKey.Contains(property))
                    {
                        ownedNewKey.Add(property);
                    }
                }
            }

            next.Add((owned, ownedParts, ownedNewKey));
        }

        if (newKey is not null)
        {
            entityType.Builder.PrimaryKey(newKey, fromDataAnnotation: true);
        }

        foreach (var (owned, ownedParts, ownedNewKey) in next)
        {
            Rekey(owned, ownedParts, ownedNewKey, snapshots);
        }
    }

    /// <summary>
    /// The owned type's side of a foreign key onto <paramref name="principalKey"/>: its own property
    /// for each key part, and the property that already held the rest before anything changed.
    /// </summary>
    private static List<IConventionProperty> MapForeignKey(
        IConventionEntityType owner,
        IConventionEntityType owned,
        string navigationName,
        IReadOnlyList<IConventionProperty> principalKey,
        IReadOnlyList<string> parts,
        OwnershipSnapshot snapshot)
    {
        var properties = new List<IConventionProperty>(principalKey.Count);
        foreach (var principalProperty in principalKey)
        {
            if (parts.Contains(principalProperty.Name))
            {
                properties.Add(MapOwnedKeyPart(owner, owned, navigationName, principalProperty));
                continue;
            }

            var name = snapshot.ForeignKeyByPrincipal.TryGetValue(principalProperty.Name, out var original)
                ? original
                : owner.ClrType.Name + principalProperty.Name;
            properties.Add(owned.Builder.Property(principalProperty.ClrType, name, fromDataAnnotation: true)!.Metadata);
        }

        return properties;
    }

    /// <summary>The owned type's own property for one of its owner's key parts; throws when it has none.</summary>
    private static IConventionProperty MapOwnedKeyPart(IConventionEntityType owner, IConventionEntityType owned, string navigationName, IConventionProperty principalProperty)
    {
        var clrProperty = owned.ClrType.GetProperty(principalProperty.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (clrProperty is null || clrProperty.PropertyType != principalProperty.ClrType)
        {
            var found = clrProperty is null ? "has no such property" : $"has it as '{clrProperty.PropertyType.Name}'";
            throw new InvalidOperationException(
                $"'{owner.ClrType.Name}' is keyed on '{principalProperty.Name}', so the foreign key of its owned '{owned.ClrType.Name}' (through '{owner.ClrType.Name}.{navigationName}') must carry it too, " +
                $"but '{owned.ClrType.Name}' {found}. Declare '[KeyPart] public {principalProperty.ClrType.Name} {principalProperty.Name} {{ get; }}' on '{owned.ClrType.Name}' and set it from '{owner.ClrType.Name}', " +
                "or configure this ownership's foreign key yourself in OnModelCreating.");
        }

        return MapClrProperty(owned, clrProperty);
    }

    private static IConventionProperty MapClrProperty(IConventionEntityType entityType, string name)
    {
        var clrProperty = entityType.ClrType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"'{entityType.ClrType.Name}' names '{name}' as a key part, but has no such property.");

        return MapClrProperty(entityType, clrProperty);
    }

    /// <summary>
    /// Maps the property, which a get-only key part is not by convention: EF Core only discovers
    /// properties with a setter, and <c>{ get; }</c> is exactly how a key part should be declared.
    /// </summary>
    private static IConventionProperty MapClrProperty(IConventionEntityType entityType, PropertyInfo clrProperty)
        => entityType.Builder.Property(clrProperty, fromDataAnnotation: true)?.Metadata
            ?? throw new InvalidOperationException(
                $"The key part '{entityType.ClrType.Name}.{clrProperty.Name}' cannot be mapped; it may be ignored with [NotMapped], [Internal] or Ignore().");

    private static IReadOnlyList<string> KeyPartsOf(Type clrType)
        => typeof(IHasKeyParts).IsAssignableFrom(clrType)
            ? (IReadOnlyList<string>)ReadKeyPartsMethod.MakeGenericMethod(clrType).Invoke(null, null)!
            : [];

    private static IReadOnlyList<string> ReadKeyParts<T>() where T : IHasKeyParts => T.KeyParts;

    /// <summary>
    /// Whether the host configured this itself, with <c>HasKey</c>/<c>HasForeignKey</c> or with an
    /// attribute such as <c>[PrimaryKey]</c>. The key parts are applied at data-annotation strength
    /// (they do come from an attribute), which keeps EF Core's own key discovery from undoing them
    /// while anything the host wrote still wins.
    /// </summary>
    private static bool IsConfiguredByHost(ConfigurationSource? source) => source is ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation;

    /// <summary>How an ownership looked before any key changed.</summary>
    private sealed record OwnershipSnapshot(IReadOnlyDictionary<string, string> ForeignKeyByPrincipal, IReadOnlyList<string> KeyBeyondForeignKey)
    {
        public static OwnershipSnapshot Take(IConventionForeignKey foreignKey)
        {
            var byPrincipal = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < foreignKey.Properties.Count; i++)
            {
                byPrincipal[foreignKey.PrincipalKey.Properties[i].Name] = foreignKey.Properties[i].Name;
            }

            var beyond = foreignKey.DeclaringEntityType.FindPrimaryKey()?.Properties
                .Where(property => !foreignKey.Properties.Contains(property))
                .Select(static property => property.Name)
                .ToList() ?? [];

            return new OwnershipSnapshot(byPrincipal, beyond);
        }
    }
}
