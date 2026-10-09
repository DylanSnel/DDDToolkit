using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Membership.TestHost.Depot;

// The application's organization, as small as one can be: a depot. It takes porters on, keeps roles of its
// own with the keys each gives, has bays in a tree, and lets a porter hold a key at a bay. None of it is the
// package's: it is what an application has beside its resources, and what the resources' rules have it answer.
// Plain rows, since nothing here is an aggregate the tests are about.

/// <summary>A porter: who the depot knows a caller as.</summary>
[EntityId<Guid>]
public readonly partial record struct PorterId;

/// <summary>A role the depot keeps.</summary>
[EntityId<Guid>]
public readonly partial record struct DepotRoleId;

/// <summary>A bay: a place of the depot.</summary>
[EntityId<Guid>]
public readonly partial record struct BayId;

/// <summary>Somebody the depot has taken on: a user, known in the depot as a porter, for as long as the depot says.</summary>
public sealed class Porter
{
    /// <summary>The porter.</summary>
    public required PorterId Id { get; init; }

    /// <summary>The user the porter is.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Whether the porter counts: one the depot let go is nobody's member any more.</summary>
    public required bool Active { get; set; }
}

/// <summary>A role of the depot's own, kept as a row.</summary>
public sealed class DepotRole
{
    /// <summary>The role.</summary>
    public required DepotRoleId Id { get; init; }

    /// <summary>What the depot calls it.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the role is in use: one that is not gives nothing.</summary>
    public required bool InUse { get; set; }
}

/// <summary>A key a role of the depot's gives.</summary>
public sealed class DepotRoleKey
{
    /// <summary>The role.</summary>
    public required DepotRoleId RoleId { get; init; }

    /// <summary>The key it gives.</summary>
    public required string Key { get; init; }
}

/// <summary>A bay of the depot, under another or under none.</summary>
public sealed class Bay
{
    /// <summary>The bay.</summary>
    public required BayId Id { get; init; }

    /// <summary>The bay it is part of, or <see langword="null"/> for the whole depot.</summary>
    public BayId? PartOf { get; init; }
}

/// <summary>A bay with one that is above it, or itself: every such pair of the tree, so "at or below" is one comparison.</summary>
public sealed class BayPath
{
    /// <summary>The bay above, or the bay itself.</summary>
    public required BayId AboveId { get; init; }

    /// <summary>The bay.</summary>
    public required BayId BayId { get; init; }
}

/// <summary>A key a porter holds at a bay, and so at every bay below it.</summary>
public sealed class BayHold
{
    /// <summary>The porter.</summary>
    public required PorterId PorterId { get; init; }

    /// <summary>The bay the key is held at.</summary>
    public required BayId BayId { get; init; }

    /// <summary>The key.</summary>
    public required string Key { get; init; }
}
