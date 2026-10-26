using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
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
    /// <summary>Stacks a pallet, with <paramref name="owner"/> on it from <paramref name="now"/> on, for good, holding no role.</summary>
    public Pallet(PalletId id, string label, PorterId owner, DateTimeOffset now) : base(id)
    {
        Label = label;
        OwnerId = owner;
        Members.Open(now);
    }

    /// <summary>What is written on the pallet.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>The porter that owns the pallet.</summary>
    [Owner]
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

/// <summary>The permissions asked on a pallet: its owner holds all three; no role gives the one that straps it.</summary>
[Permissions]
public static class PalletPermissions
{
    /// <summary>See a pallet.</summary>
    [On<Pallet>(EveryMember = true)]
    public const string See = "pallets.see";

    /// <summary>Load a pallet.</summary>
    [On<Pallet>]
    public const string Load = "pallets.load";

    /// <summary>Strap a pallet: no role gives it, so the owner's alone.</summary>
    [On<Pallet>(InRoles = false)]
    public const string Strap = "pallets.strap";
}

/// <summary>The rules of access through a pallet's porters: roles in code, porters known by the depot's function.</summary>
public static class PalletMembership
{
    /// <summary>The codes a pallet refuses under.</summary>
    public static MembershipCodes Codes { get; } = MembershipCodes.Under("pallets");

    /// <summary>The rules.</summary>
    public static MembershipRules Rules { get; } = MembershipRules.For<Pallet>(
        Roles.Fixed([new("loader", [PalletPermissions.See, PalletPermissions.Load]), new("checker", [PalletPermissions.See])]),
        members: MemberSource.Resolved(DepotFunctions.CallerPorter));

    /// <summary>The role that loads.</summary>
    public static NamedRole Loader { get; } = new("loader");

    /// <summary>The role that checks.</summary>
    public static NamedRole Checker { get; } = new("checker");
}
