using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.Invariants;
using DDDToolkit.Supporting.Membership.TestHost.Depot;

namespace DDDToolkit.Supporting.Membership.TestHost.Crates;

/// <summary>A crate's id.</summary>
[EntityId<Guid>]
public readonly partial record struct CrateId;

/// <summary>The id of a crate's member row.</summary>
[EntityId<Guid>]
public readonly partial record struct CratePorterId;

/// <summary>
/// A porter on a crate: who a member is, is the depot's to say, and so is what a role is: a member holds a
/// role of the depot's, by its id.
/// </summary>
[Member<CratePorterId, PorterId, DepotRoleId, Crate>]
public sealed partial class CratePorter;

/// <summary>
/// A crate, standing in a bay of the depot: the host's fourth kind of resource with members, and the one that
/// stands beside the organization in every way. Its members are porters, its roles are the depot's, and a
/// porter who holds a key at the crate's bay, or above it, holds that key on the crate without being on it.
/// </summary>
[AggregateRoot<CrateId>]
public sealed partial class Crate
{
    /// <summary>Packs a crate in a bay, with <paramref name="owner"/> on it from <paramref name="now"/> on, for good, holding no role.</summary>
    public Crate(CrateId id, string label, BayId bay, PorterId owner, DateTimeOffset now) : base(id)
    {
        Label = label;
        BayId = bay;
        OwnerId = owner;
        Members.Open(now);
    }

    /// <summary>What is written on the crate.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>The bay the crate stands in: where it sits.</summary>
    [At]
    public BayId BayId { get; private set; }

    /// <summary>The porter that owns the crate.</summary>
    [Owner]
    public PorterId OwnerId { get; private set; }

    /// <summary>The porters on the crate.</summary>
    public partial IReadOnlyList<CratePorter> Porters { get; }

    /// <summary>
    /// The codes the rules about a crate's porters refuse under. With the collection above and the owner, it is what the
    /// toolkit writes the member list from: <c>Members</c>, which the methods below change the members through.
    /// </summary>
    private static MembershipCodes Codes => CrateMembership.Codes;

    /// <summary>Puts a porter on the crate, with no role yet.</summary>
    public CratePorter PutOn(PorterId porter, MemberPeriod period, DateTimeOffset now, PorterId? by) => Members.Add(porter, period, now, by);

    /// <summary>Puts a porter on the crate in a role, both for the same period.</summary>
    public CratePorter PutOn(PorterId porter, DepotRoleId role, MemberPeriod period, DateTimeOffset now, PorterId? by) => Members.Add(porter, role, period, now, by);

    /// <summary>Gives a porter on the crate a role.</summary>
    public void GiveRole(PorterId to, DepotRoleId role, MemberPeriod period, DateTimeOffset now, PorterId? by) => Members.GiveRole(to, role, period, now, by);

    /// <summary>Moves the crate to another bay.</summary>
    public void PutIn(BayId bay) => BayId = bay;

    /// <summary>A porter is on a crate once.</summary>
    public sealed class OnePlacePerPorter : IInvariant<Crate>
    {
        /// <inheritdoc />
        public string Code => CrateMembership.Codes[MembershipRefusals.AlreadyMember];

        /// <inheritdoc />
        public InvariantFailure? Check(Crate entity) => entity.Members.OneMembershipPerMember();
    }

    /// <summary>The owner stays on the crate, for good.</summary>
    public sealed class OwnerKeepsAPlace : IInvariant<Crate>
    {
        /// <inheritdoc />
        public string Code => CrateMembership.Codes[MembershipRefusals.OwnerProtected];

        /// <inheritdoc />
        public InvariantFailure? Check(Crate entity) => entity.Members.OwnerStays();
    }
}

/// <summary>
/// The permissions asked on a crate. Its owner holds the ones that pack, weigh and scrap it; every porter on it sees
/// it; a depot role gives those, and keys of other modules besides; one is held from above alone.
/// </summary>
[Permissions]
public static class CratePermissions
{
    /// <summary>See a crate: held by every porter on it, and from above by a hold of it at the crate's bay.</summary>
    [On<Crate>(EveryMember = true, Owner = false)]
    public const string See = "crates.see";

    /// <summary>Pack a crate: its owner's, and given by a depot role.</summary>
    [On<Crate>]
    public const string Pack = "crates.pack";

    /// <summary>Weigh a crate: its owner's, and given by a depot role.</summary>
    [On<Crate>]
    public const string Weigh = "crates.weigh";

    /// <summary>Move a crate: no role on a crate gives it, so it is held from above alone.</summary>
    [On<Crate>(Owner = false, InRoles = false)]
    public const string Move = "crates.move";

    /// <summary>Scrap a crate: its owner's by owning it, a depot role's, or held from above.</summary>
    [On<Crate>]
    public const string Scrap = "crates.scrap";
}

/// <summary>Keys of the depot's other modules that a depot role holds beside a crate's permissions.</summary>
public static class CrateKeys
{
    /// <summary>Print labels: another module's key, which a depot role on a crate gives on it as well.</summary>
    public const string PrintLabels = "labels.print";

    /// <summary>Run the depot: the depot's own key, which no role on a crate gives.</summary>
    public const string ManageDepot = "depot.manage";
}

/// <summary>
/// The rules of access through a crate's porters, with the depot's own functions: porters known by the depot, roles
/// it keeps, and its bays, from which a key held at a crate's bay or above it reaches the crate.
/// </summary>
public static class CrateMembership
{
    /// <summary>The codes a crate refuses under.</summary>
    public static MembershipCodes Codes { get; } = MembershipCodes.Under("crates");

    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = MembershipRules.For<Crate>(
        Roles.External(DepotFunctions.RolesWithKey, otherKeys: OtherKeys.AllBut(CrateKeys.ManageDepot)),
        members: MemberSource.Resolved(DepotFunctions.CallerPorter),
        reachFromAbove: DepotFunctions.BaysWhereIHold);
}
