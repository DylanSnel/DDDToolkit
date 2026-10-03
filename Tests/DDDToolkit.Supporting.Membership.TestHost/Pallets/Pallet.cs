using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Invariants;
using DDDToolkit.Supporting.Membership.TestHost.Depot;

namespace DDDToolkit.Supporting.Membership.TestHost.Pallets;

/// <summary>A pallet's id.</summary>
[EntityId<Guid>]
public readonly partial record struct PalletId;

/// <summary>The id of a pallet's member row.</summary>
[EntityId<Guid>]
public readonly partial record struct PalletPorterId;

/// <summary>
/// A porter on a pallet: who a member is, is the depot's to say, and the roles are the pallet's own, by name.
/// </summary>
[Member<PalletPorterId, PorterId, NamedRole, Pallet>]
public sealed partial class PalletPorter;

/// <summary>
/// A pallet, worked on by porters of the depot: the host's third kind of resource with members. Its members
/// are not users but what the depot knows its callers as, and its roles are declared in its own rules. It
/// sits nowhere, so nothing above reaches it.
/// </summary>
[AggregateRoot<PalletId>]
public sealed partial class Pallet
{
    /// <summary>Stacks a pallet, with <paramref name="owner"/> on it from <paramref name="now"/> on, for good, in the owner's role.</summary>
    public Pallet(PalletId id, string label, PorterId owner, NamedRole ownerRole, DateTimeOffset now) : base(id)
    {
        Label = label;
        OwnerId = owner;
        Members.Open(ownerRole, now);
    }

    /// <summary>What is written on the pallet.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>The porter that owns the pallet.</summary>
    public PorterId OwnerId { get; private set; }

    /// <summary>The porters on the pallet.</summary>
    public partial IReadOnlyList<PalletPorter> Porters { get; }

    /// <summary>
    /// The codes the rules about a pallet's porters refuse under. With the collection above and the owner, it is what the
    /// toolkit writes the member list from: <c>Members</c>, which the methods below change the members through.
    /// </summary>
    private static MembershipCodes Codes => PalletMembership.Codes;

    /// <summary>Puts a porter on the pallet, with no role yet.</summary>
    public PalletPorter PutOn(PorterId porter, MemberPeriod period, DateTimeOffset now, PorterId? by) => Members.Add(porter, period, now, by);

    /// <summary>Puts a porter on the pallet in a role, both for the same period.</summary>
    public PalletPorter PutOn(PorterId porter, NamedRole role, MemberPeriod period, DateTimeOffset now, PorterId? by) => Members.Add(porter, role, period, now, by);

    /// <summary>Gives a porter on the pallet a role.</summary>
    public void GiveRole(PorterId to, NamedRole role, MemberPeriod period, DateTimeOffset now, PorterId? by) => Members.GiveRole(to, role, period, now, by);

    /// <summary>A porter is on a pallet once.</summary>
    public sealed class OnePlacePerPorter : IInvariant<Pallet>
    {
        /// <inheritdoc />
        public string Code => PalletMembership.Codes[MembershipRefusals.AlreadyMember];

        /// <inheritdoc />
        public InvariantFailure? Check(Pallet entity) => entity.Members.OneMembershipPerMember();
    }

    /// <summary>The owner stays on the pallet, with a role that does not run out.</summary>
    public sealed class OwnerKeepsAPlace : IInvariant<Pallet>
    {
        /// <inheritdoc />
        public string Code => PalletMembership.Codes[MembershipRefusals.OwnerProtected];

        /// <inheritdoc />
        public InvariantFailure? Check(Pallet entity) => entity.Members.OwnerStays();
    }
}

/// <summary>The keys a pallet is asked about.</summary>
public static class PalletKeys
{
    /// <summary>See a pallet.</summary>
    public const string See = "pallets.see";

    /// <summary>Load a pallet.</summary>
    public const string Load = "pallets.load";

    /// <summary>Strap a pallet down: a key the loader's role lists and no member's role gives, so the owner's alone.</summary>
    public const string Strap = "pallets.strap";
}

/// <summary>
/// The rules of access through a pallet's porters. Two of the three things rules say are the defaults here,
/// and one is not: the roles are declared in the rules, nothing above reaches a pallet, and who a member is,
/// is what the depot resolves for the caller.
/// </summary>
public static class PalletMembership
{
    /// <summary>The codes a pallet refuses with.</summary>
    public static MembershipCodes Codes { get; } = MembershipCodes.Under("pallets");

    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = new(
        "pallets",
        keys: [PalletKeys.See, PalletKeys.Load, PalletKeys.Strap],
        roles: [new("loader", [PalletKeys.See, PalletKeys.Load, PalletKeys.Strap]), new("checker", [PalletKeys.See])],
        members: MemberSource.Resolved(DepotFunctions.CallerPorter),
        seeKey: PalletKeys.See,
        memberKeys: MemberKeys.AllBut(PalletKeys.Strap),
        codes: Codes);

    /// <summary>The role that loads.</summary>
    public static NamedRole Loader { get; } = new("loader");

    /// <summary>The role that only looks.</summary>
    public static NamedRole Checker { get; } = new("checker");

    /// <summary>The owner's role, which the rules add, with every key of a pallet a member's role gives.</summary>
    public static NamedRole Owner { get; } = new(MembershipRules.DefaultOwnerRole);
}
