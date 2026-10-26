using DDDToolkit.Abstractions.Attributes;
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
    /// <summary>Packs a crate in a bay, with <paramref name="owner"/> on it from <paramref name="now"/> on, for good, in the owner's role.</summary>
    public Crate(CrateId id, string label, BayId bay, PorterId owner, DepotRoleId ownerRole, DateTimeOffset now) : base(id)
    {
        Label = label;
        BayId = bay;
        OwnerId = owner;
        Members.Open(ownerRole, now);
    }

    /// <summary>What is written on the crate.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>The bay the crate stands in: where it sits.</summary>
    public BayId BayId { get; private set; }

    /// <summary>The porter that owns the crate.</summary>
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

    /// <summary>The owner stays on the crate, with a role that does not run out.</summary>
    public sealed class OwnerKeepsAPlace : IInvariant<Crate>
    {
        /// <inheritdoc />
        public string Code => CrateMembership.Codes[MembershipRefusals.OwnerProtected];

        /// <inheritdoc />
        public InvariantFailure? Check(Crate entity) => entity.Members.OwnerStays();
    }
}

/// <summary>The keys a crate is asked about, and two that are not a crate's.</summary>
public static class CrateKeys
{
    /// <summary>See a crate: what being on it gives.</summary>
    public const string See = "crates.see";

    /// <summary>Pack a crate.</summary>
    public const string Pack = "crates.pack";

    /// <summary>Weigh a crate.</summary>
    public const string Weigh = "crates.weigh";

    /// <summary>Move a crate to another bay: held at a bay or not at all, never through a role on a crate.</summary>
    public const string Move = "crates.move";

    /// <summary>Scrap a crate: the owner's, by owning it, whatever the depot's roles give.</summary>
    public const string Scrap = "crates.scrap";

    /// <summary>A key of another module's, which a role of the depot's may give on a crate without the crates naming it.</summary>
    public const string PrintLabels = "labels.print";

    /// <summary>A key of the depot's own, which no role gives on a crate, whatever it holds in the depot.</summary>
    public const string ManageDepot = "depot.manage";
}

/// <summary>
/// The rules of access through a crate's porters, with each of the three things rules say said the other
/// way than a document's: who a member is, is what the depot resolves; the roles are kept by the depot; and a
/// key held at the crate's bay, or above it, reaches the crate.
/// </summary>
public static class CrateMembership
{
    /// <summary>What the depot's role for owners is found by: its name.</summary>
    public const string OwnerRole = "crate-lead";

    /// <summary>The codes a crate refuses with.</summary>
    public static MembershipCodes Codes { get; } = MembershipCodes.Under("crates");

    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = new(
        "crates",
        keys: [CrateKeys.Scrap],
        members: MemberSource.Resolved(DepotFunctions.CallerPorter),
        seeKey: CrateKeys.See,
        ownerRole: OwnerRole,
        memberKeys: MemberKeys.AllBut(CrateKeys.Move, CrateKeys.ManageDepot),
        codes: Codes,
        rolesKeptElsewhere: new(DepotFunctions.RolesWithKey),
        above: new(DepotFunctions.BaysWhereIHold));
}
