using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Membership.TestHost.Gardens;

/// <summary>A shed's id.</summary>
[EntityId<Guid>]
public readonly partial record struct ShedId;

/// <summary>The id of a shed's member row.</summary>
[EntityId<Guid>]
public readonly partial record struct ShedHandId;

/// <summary>The id of a role of sheds.</summary>
[EntityId<Guid>]
public readonly partial record struct ShedRoleId;

/// <summary>A hand in a shed: a user, holding roles of sheds by their id.</summary>
[Member<ShedHandId, UserId, ShedRoleId, Shed>]
public sealed partial class ShedHand;

/// <summary>
/// A role of sheds: the second role class of the project, next to the plots', each named for the resource it
/// is a role of. The host adds nothing to it: the sheds' roles are the whole application's, kept in no
/// customer's name, so there is one set of them and no rule about whose a request reads.
/// </summary>
[KeptRole<ShedRoleId, Shed>]
public sealed partial class ShedRole
{
    /// <summary>Makes a role of sheds.</summary>
    public ShedRole(ShedRoleId id, KeptRoleDraft draft) : base(id, draft, ShedMembership.Rules)
    {
    }
}

/// <summary>
/// A shed, kept by hands: a second kind of resource whose roles are kept, in the same project and the same
/// context as the plots. Its rules differ from a plot's in every way they can: no key comes with being a
/// hand, the owner's role is a starter role the rules declare, and a role may give any key but one, a key of
/// another module among them.
/// </summary>
[AggregateRoot<ShedId>]
public sealed partial class Shed
{
    /// <summary>Builds a shed, with <paramref name="owner"/> in it from <paramref name="now"/> on, for good, in the role for owners.</summary>
    public Shed(ShedId id, string name, UserId owner, ShedRoleId ownerRole, DateTimeOffset now) : base(id)
    {
        Name = name;
        OwnerId = owner;
        Members.Open(ownerRole, now);
    }

    /// <summary>What the shed is called.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The user that owns the shed.</summary>
    public UserId OwnerId { get; private set; }

    /// <summary>The hands in the shed.</summary>
    public partial IReadOnlyList<ShedHand> Hands { get; }

    /// <summary>
    /// The codes the rules about a shed's hands refuse under. With the collection above and the owner, it is what the
    /// toolkit writes the member list from: <c>Members</c>, which the methods below change the members through.
    /// </summary>
    private static MembershipCodes Codes => ShedMembership.Codes;

    /// <summary>Lets a hand into the shed, with no role yet.</summary>
    public ShedHand LetIn(UserId hand, MemberPeriod period, DateTimeOffset now, UserId? by) => Members.Add(hand, period, now, by);

    /// <summary>Lets a hand into the shed in a role, both for the same period.</summary>
    public ShedHand LetIn(UserId hand, ShedRoleId role, MemberPeriod period, DateTimeOffset now, UserId? by) => Members.Add(hand, role, period, now, by);

    /// <summary>A hand is in a shed once.</summary>
    public sealed class OnePlacePerHand : IInvariant<Shed>
    {
        /// <inheritdoc />
        public string Code => ShedMembership.Codes[MembershipRefusals.AlreadyMember];

        /// <inheritdoc />
        public InvariantFailure? Check(Shed entity) => entity.Members.OneMembershipPerMember();
    }

    /// <summary>The owner stays in the shed, with a role that does not run out.</summary>
    public sealed class OwnerKeepsAPlace : IInvariant<Shed>
    {
        /// <inheritdoc />
        public string Code => ShedMembership.Codes[MembershipRefusals.OwnerProtected];

        /// <inheritdoc />
        public InvariantFailure? Check(Shed entity) => entity.Members.OwnerStays();
    }
}

/// <summary>The keys a shed is asked about, and one that is not a shed's.</summary>
public static class ShedKeys
{
    /// <summary>Open a shed.</summary>
    public const string Open = "sheds.open";

    /// <summary>Stock a shed.</summary>
    public const string Stock = "sheds.stock";

    /// <summary>Sell a shed: the one key no role of sheds may give, so the owner's alone.</summary>
    public const string Sell = "sheds.sell";

    /// <summary>A key of another module's, which a role of sheds may give without the sheds naming it.</summary>
    public const string LendTools = "tools.lend";
}

/// <summary>
/// The rules of access through a shed's hands. The roles are kept, as a plot's are, and everything else is
/// said the other way: being a hand gives no key by itself, the owner's role is the starter role declared
/// here, and a role may give every key it holds but selling.
/// </summary>
public static class ShedMembership
{
    /// <summary>The starter role every owner of a shed holds.</summary>
    public const string Keeper = "keeper";

    /// <summary>The codes a shed refuses with.</summary>
    public static MembershipCodes Codes { get; } = MembershipCodes.Under("sheds");

    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = new(
        "sheds",
        keys: [ShedKeys.Open, ShedKeys.Stock, ShedKeys.Sell],
        roles: [new(Keeper, [ShedKeys.Open, ShedKeys.Stock, ShedKeys.Sell])],
        ownerRole: Keeper,
        memberKeys: MemberKeys.AllBut(ShedKeys.Sell),
        codes: Codes,
        rolesKept: true);
}
